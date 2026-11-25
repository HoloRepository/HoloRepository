import Foundation
import Observation

/// Scans the user wants to find again quickly.
///
/// Nothing is copied. For each scan the app stores a macOS *security-scoped bookmark*:
/// a small, secure reference to where the original files are, which lets this sandboxed
/// app reopen them after a restart. The list is a small JSON file:
///
///     ~/Library/Containers/uk.ac.ucl.holorepository2027.macos/Data/
///         Library/Application Support/HoloRepository2027/recent-scans.json
///
/// If the original files are moved, renamed or deleted, or their drive isn't connected,
/// the scan is shown as missing. Removing a scan only forgets the bookmark.
@Observable
final class RecentScans {
    private(set) var scans: [Scan] = []
    /// Scans whose files can't be found right now.
    private(set) var missing: Set<Scan.ID> = []

    @ObservationIgnored private let storeURL: URL
    @ObservationIgnored private var entries: [Scan.ID: Entry] = [:]
    /// Bookmarked locations currently opened for reading, so they can be released on removal.
    @ObservationIgnored private var accessed: [Scan.ID: [URL]] = [:]

    init(storeURL: URL? = nil) {
        let folder = URL.applicationSupportDirectory.appending(path: "HoloRepository2027", directoryHint: .isDirectory)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        self.storeURL = storeURL ?? folder.appending(path: "recent-scans.json")
        load()
    }

    func scan(withID id: Scan.ID?) -> Scan? {
        scans.first { $0.id == id }
    }

    func isMissing(_ id: Scan.ID?) -> Bool {
        id.map { missing.contains($0) } ?? false
    }

    // MARK: - Adding and removing

    /// Remembers where a scan is. Throws if macOS won't create a bookmark for its files.
    func add(_ scan: Scan) throws -> Scan {
        if let existing = scans.first(where: { $0.id == scan.id }) { return existing }
        var kept = scan
        kept.isRecent = true
        kept.addedAt = .now
        entries[kept.id] = try Entry(scan: kept)
        scans.insert(kept, at: 0)
        missing.remove(kept.id)
        try persist()
        return kept
    }

    /// Forgets a scan. Its files are not touched.
    func remove(_ scan: Scan) {
        accessed.removeValue(forKey: scan.id)?.forEach { $0.stopAccessingSecurityScopedResource() }
        entries[scan.id] = nil
        scans.removeAll { $0.id == scan.id }
        missing.remove(scan.id)
        try? persist()
    }

    func rename(_ scan: Scan, to newName: String) {
        let trimmed = newName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, let index = scans.firstIndex(where: { $0.id == scan.id }) else { return }
        scans[index].name = trimmed
        entries[scan.id]?.name = trimmed
        try? persist()
    }

    /// Checks that a scan's files are still where the bookmark says. Updates `missing`.
    @discardableResult
    func checkAvailability(of scan: Scan) -> Bool {
        let available = !scan.imageFiles.isEmpty && scan.imageFiles.allSatisfy { FileManager.default.fileExists(atPath: $0.path) }
        if available { missing.remove(scan.id) } else { missing.insert(scan.id) }
        return available
    }

    // MARK: - Stored form

    /// One bookmarked location and the scan files inside it.
    private struct Location: Codable {
        /// Security-scoped bookmark to a folder, or to a single file.
        var bookmark: Data
        /// File paths relative to the bookmarked folder. Empty when the bookmark is the file itself.
        var files: [String]
    }

    private struct Entry: Codable {
        var id: UUID
        var name: String
        var format: ScanFormat
        var modality: String
        var summary: String
        var addedAt: Date
        var locations: [Location]

        init(scan: Scan) throws {
            id = scan.id
            name = scan.name
            format = scan.format
            modality = scan.modality
            summary = scan.summary
            addedAt = scan.addedAt
            locations = try Entry.locations(for: scan.imageFiles)
        }

        /// One bookmark for the folder holding the files (usually the series folder). If the
        /// app isn't allowed to bookmark that folder (the user picked files, not the folder),
        /// it falls back to one bookmark per file.
        private static func locations(for files: [URL]) throws -> [Location] {
            guard let first = files.first else { return [] }
            if files.count == 1 {
                return [Location(bookmark: try bookmark(for: first), files: [])]
            }
            let folder = commonFolder(of: files)
            if let data = try? bookmark(for: folder) {
                let prefix = folder.standardizedFileURL.path + "/"
                return [Location(bookmark: data, files: files.map { String($0.standardizedFileURL.path.dropFirst(prefix.count)) })]
            }
            return try files.map { Location(bookmark: try bookmark(for: $0), files: []) }
        }

        private static func bookmark(for url: URL) throws -> Data {
            try url.bookmarkData(options: [.withSecurityScope, .securityScopeAllowOnlyReadAccess],
                                 includingResourceValuesForKeys: nil, relativeTo: nil)
        }

        private static func commonFolder(of files: [URL]) -> URL {
            var parts = files[0].deletingLastPathComponent().standardizedFileURL.pathComponents
            for file in files.dropFirst() {
                let other = file.deletingLastPathComponent().standardizedFileURL.pathComponents
                parts = Array(zip(parts, other).prefix { $0 == $1 }.map(\.0))
            }
            return URL(fileURLWithPath: NSString.path(withComponents: parts), isDirectory: true)
        }
    }

    // MARK: - Loading and saving the list

    private func load() {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        guard let data = try? Data(contentsOf: storeURL),
              let stored = try? decoder.decode([Entry].self, from: data) else { return }

        var changed = false
        for var entry in stored {
            var files: [URL] = []
            var resolvedAll = true
            for index in entry.locations.indices {
                var isStale = false
                guard let url = try? URL(resolvingBookmarkData: entry.locations[index].bookmark, options: .withSecurityScope,
                                         relativeTo: nil, bookmarkDataIsStale: &isStale) else {
                    resolvedAll = false
                    continue
                }
                if url.startAccessingSecurityScopedResource() {
                    accessed[entry.id, default: []].append(url)
                }
                if isStale, let fresh = try? url.bookmarkData(options: [.withSecurityScope, .securityScopeAllowOnlyReadAccess],
                                                              includingResourceValuesForKeys: nil, relativeTo: nil) {
                    entry.locations[index].bookmark = fresh // the files moved but macOS still found them
                    changed = true
                }
                let relative = entry.locations[index].files
                files += relative.isEmpty ? [url] : relative.map { url.appending(path: $0) }
            }
            let scan = Scan(id: entry.id, name: entry.name, format: entry.format, modality: entry.modality,
                            summary: entry.summary, imageFiles: files, isRecent: true, addedAt: entry.addedAt)
            entries[entry.id] = entry
            scans.append(scan)
            if !resolvedAll || !checkAvailability(of: scan) { missing.insert(scan.id) }
        }
        if changed { try? persist() }
    }

    private func persist() throws {
        let ordered = scans.compactMap { entries[$0.id] }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        try encoder.encode(ordered).write(to: storeURL, options: .atomic)
    }
}

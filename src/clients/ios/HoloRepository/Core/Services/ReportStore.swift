//
//  ReportStore.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation

/// Where reports and clinicians' approvals and edits are kept.
/// December: `LocalReportStore` on the device. Later: a daemon-backed store with the same interface.
protocol ReportStore: AnyObject {
    /// Every report, with any saved approvals and edits applied.
    func allReports() -> [Report]
    /// Saves an approved or edited report.
    func save(_ report: Report) throws
}

/// Mock reports from `MockDataService`, with approvals and edits saved to a protected file on the device.
/// Delete the app to reset the demo.
final class LocalReportStore: ReportStore {
    private let baseReports: [Report]
    /// Nil keeps changes in memory only (previews).
    private let fileURL: URL?
    private var savedReports: [String: Report]

    init(baseReports: [Report], fileURL: URL? = LocalReportStore.defaultFileURL) {
        self.baseReports = baseReports
        self.fileURL = fileURL
        savedReports = Self.readSaved(from: fileURL)
    }

    /// For previews: never touches the disk.
    static func inMemory(baseReports: [Report]) -> LocalReportStore {
        LocalReportStore(baseReports: baseReports, fileURL: nil)
    }

    static var defaultFileURL: URL? {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?
            .appending(path: "report-changes.json")
    }

    func allReports() -> [Report] {
        baseReports.map { savedReports[$0.id] ?? $0 }
    }

    func save(_ report: Report) throws {
        savedReports[report.id] = report
        guard let fileURL else { return }
        try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        // Clinical content: only readable while the device is unlocked.
        try encoder.encode(savedReports).write(to: fileURL, options: [.atomic, .completeFileProtection])
    }

    private static func readSaved(from fileURL: URL?) -> [String: Report] {
        guard let fileURL, let data = try? Data(contentsOf: fileURL) else { return [:] }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        do {
            return try decoder.decode([String: Report].self, from: data)
        } catch {
            // Saved with an older report format: start again from the mock data rather than crash.
            print("LocalReportStore: ignoring unreadable saved reports: \(error)")
            return [:]
        }
    }
}

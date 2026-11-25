import AppKit
import Observation

/// Coordinates opening files, Recent scans and the viewer. Views call into this.
@Observable
final class AppModel {
    let recents = RecentScans()
    let viewer = ViewerController()

    /// Scans opened this session and not kept in Recent scans. They disappear when the app quits.
    private(set) var openedScans: [Scan] = []
    private(set) var selectedScanID: Scan.ID?

    /// Set when a picked folder contained several scans, so the user can choose.
    var importChoice: ImportResult?
    private(set) var isImporting = false
    var message: UserMessage?

    struct UserMessage: Identifiable {
        let id = UUID()
        let title: String
        let detail: String
    }

    var selectedScan: Scan? {
        recents.scan(withID: selectedScanID) ?? openedScans.first { $0.id == selectedScanID }
    }

    // MARK: - Opening

    /// Shows the Open panel. Files and folders are both allowed.
    func showOpenPanel() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true
        panel.prompt = "Open"
        panel.message = "Choose a NIfTI file (.nii, .nii.gz), DICOM files, or a folder containing a DICOM series."
        guard panel.runModal() == .OK else { return }
        importItems(panel.urls)
    }

    func importItems(_ urls: [URL]) {
        guard !urls.isEmpty else { return }
        isImporting = true
        Task {
            for url in urls { _ = url.startAccessingSecurityScopedResource() }
            let result = await Task.detached(priority: .userInitiated) { ScanImporter.importItems(at: urls) }.value
            isImporting = false
            if !result.warnings.isEmpty && result.scans.isEmpty {
                message = UserMessage(title: "No scans could be opened", detail: result.warnings.joined(separator: "\n\n"))
            } else if result.scans.isEmpty {
                message = UserMessage(title: "No scans found",
                                      detail: "Couldn't find any NIfTI (.nii, .nii.gz) or DICOM (.dcm) files in what you picked.")
            } else if result.scans.count == 1 {
                open(result.scans[0])
                if !result.warnings.isEmpty {
                    message = UserMessage(title: "Opened with warnings", detail: result.warnings.joined(separator: "\n\n"))
                }
            } else {
                importChoice = result
            }
        }
    }

    /// Shows a scan the importer found. It isn't added to Recent scans until the user asks.
    func open(_ scan: Scan) {
        if !openedScans.contains(where: { $0.id == scan.id }) {
            openedScans.insert(scan, at: 0)
        }
        select(scan.id)
    }

    func select(_ id: Scan.ID?) {
        guard id != selectedScanID else { return }
        selectedScanID = id
        guard let scan = selectedScan else {
            viewer.unload()
            return
        }
        // A recent scan's files may have been moved or deleted since it was added.
        if scan.isRecent && !recents.checkAvailability(of: scan) {
            viewer.unload()
            message = UserMessage(
                title: "Scan not found",
                detail: "“\(scan.name)” is no longer where it was. It may have been moved, renamed or deleted, or its drive isn't connected.\n\nRight-click it in Recent scans to remove it from the list.")
            return
        }
        viewer.load(scan)
    }

    /// Removes an opened scan (not in Recent scans) from the sidebar.
    func close(_ scan: Scan) {
        openedScans.removeAll { $0.id == scan.id }
        if selectedScanID == scan.id { select(nil) }
    }

    // MARK: - Recent scans

    /// Remembers where a scan is so it can be reopened quickly. Nothing is copied.
    func addToRecents(_ scan: Scan) {
        guard !scan.isRecent else { return }
        do {
            let kept = try recents.add(scan)
            openedScans.removeAll { $0.id == scan.id }
            viewer.updateLoadedScan(kept)
        } catch {
            message = UserMessage(title: "Couldn't add to Recent scans", detail: error.localizedDescription)
        }
    }

    func addAllToRecents(_ scans: [Scan]) {
        for scan in scans where recents.scan(withID: scan.id) == nil {
            do {
                _ = try recents.add(scan)
            } catch {
                message = UserMessage(title: "Couldn't add \(scan.name) to Recent scans", detail: error.localizedDescription)
                return
            }
        }
    }

    /// Changes the name shown in the app. The scan's files are not renamed.
    func rename(_ scan: Scan, to newName: String) {
        recents.rename(scan, to: newName)
        if let renamed = recents.scan(withID: scan.id) { viewer.updateLoadedScan(renamed) }
    }

    /// Forgets a scan. Its files are not touched. If it's open, it stays open for this session.
    func removeFromRecents(_ scan: Scan) {
        let wasMissing = recents.isMissing(scan.id)
        recents.remove(scan)
        guard selectedScanID == scan.id else { return }
        if wasMissing {
            select(nil)
        } else {
            var opened = scan
            opened.isRecent = false
            openedScans.insert(opened, at: 0)
            viewer.updateLoadedScan(opened)
        }
    }
}

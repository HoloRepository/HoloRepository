import AppKit
import Observation

/// Coordinates opening files and the viewer. Views call into this.
@Observable
final class AppModel {
    let viewer = ViewerController()

    /// Scans opened this session. They disappear when the app quits.
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
        openedScans.first { $0.id == selectedScanID }
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

    /// Shows a scan the importer found.
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
        viewer.load(scan)
    }

    /// Removes an opened scan from the sidebar.
    func close(_ scan: Scan) {
        openedScans.removeAll { $0.id == scan.id }
        if selectedScanID == scan.id { select(nil) }
    }
}

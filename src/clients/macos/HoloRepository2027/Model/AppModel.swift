import AppKit
import Observation

/// Coordinates opening files and the viewer. Views call into this.
@Observable
final class AppModel {
    let viewer = ViewerController()

    var message: UserMessage?

    struct UserMessage: Identifiable {
        let id = UUID()
        let title: String
        let detail: String
    }

    // MARK: - Opening

    /// Shows the Open panel for a single NIfTI file.
    func showOpenPanel() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.prompt = "Open"
        panel.message = "Choose a NIfTI file (.nii, .nii.gz)."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        open(url)
    }

    /// Shows a NIfTI file in the viewer. The file is read where it is; nothing is copied.
    func open(_ url: URL) {
        guard url.isNIfTI else {
            message = UserMessage(title: "Not a NIfTI file",
                                  detail: "“\(url.lastPathComponent)” isn't a .nii or .nii.gz file.")
            return
        }
        _ = url.startAccessingSecurityScopedResource()
        viewer.load(Scan(name: url.scanBaseName, format: .nifti, modality: "", summary: "NIfTI volume", imageFiles: [url]))
    }
}

import Foundation

/// The file formats the viewer can open.
nonisolated enum ScanFormat: String, Codable, Sendable {
    /// A single NIfTI volume (`.nii` or `.nii.gz`).
    case nifti

    var title: String {
        switch self {
        case .nifti: "NIfTI"
        }
    }
}

/// One scan the user can view.
///
/// A scan is always read from where its files already are; the app never copies it.
nonisolated struct Scan: Identifiable, Hashable, Sendable {
    let id: UUID
    var name: String
    var format: ScanFormat
    /// "CT", "MR", … ("" when unknown, as for most NIfTI files).
    var modality: String
    /// Short human-readable description, e.g. "NIfTI volume".
    var summary: String
    var imageFiles: [URL]

    init(id: UUID = UUID(), name: String, format: ScanFormat, modality: String, summary: String,
         imageFiles: [URL]) {
        self.id = id
        self.name = name
        self.format = format
        self.modality = modality
        self.summary = summary
        self.imageFiles = imageFiles
    }
}

nonisolated extension URL {
    /// True for `.nii` and `.nii.gz` files.
    var isNIfTI: Bool {
        let name = lastPathComponent.lowercased()
        return name.hasSuffix(".nii") || name.hasSuffix(".nii.gz")
    }

    /// File name without `.nii` or `.nii.gz`.
    var scanBaseName: String {
        var name = lastPathComponent
        for ext in [".nii.gz", ".nii"] where name.lowercased().hasSuffix(ext) {
            name.removeLast(ext.count)
            break
        }
        return name
    }
}

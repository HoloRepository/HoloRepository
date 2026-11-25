import Foundation

/// The two file formats the viewer can open.
nonisolated enum ScanFormat: String, Codable, Sendable {
    /// A series of DICOM slices (`.dcm`, or extensionless files with a DICOM header).
    case dicom
    /// A single NIfTI volume (`.nii` or `.nii.gz`).
    case nifti

    var title: String {
        switch self {
        case .dicom: "DICOM"
        case .nifti: "NIfTI"
        }
    }
}

/// One scan the user can view: either a DICOM series or a NIfTI volume.
///
/// A scan is always read from where its files already are; the app never copies it.
nonisolated struct Scan: Identifiable, Hashable, Sendable {
    let id: UUID
    var name: String
    var format: ScanFormat
    /// "CT", "MR", … ("" when unknown, e.g. most NIfTI files).
    var modality: String
    /// Short human-readable description, e.g. "129 slices · 3 Jan 2002".
    var summary: String
    var imageFiles: [URL]

    var symbol: String {
        switch format {
        case .dicom: "square.stack.3d.up"
        case .nifti: "cube"
        }
    }

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

    /// File name without `.nii`, `.nii.gz` or `.dcm`.
    var scanBaseName: String {
        var name = lastPathComponent
        for ext in [".nii.gz", ".nii", ".dcm"] where name.lowercased().hasSuffix(ext) {
            name.removeLast(ext.count)
            break
        }
        return name
    }
}

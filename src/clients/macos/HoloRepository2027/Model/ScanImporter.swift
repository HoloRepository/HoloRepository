import Foundation

/// What the importer found in the files or folders the user picked.
nonisolated struct ImportResult: Sendable {
    var scans: [Scan] = []
    /// Human-readable problems worth telling the user about (empty or unreadable files).
    var warnings: [String] = []
}

/// Turns user-picked files/folders into `Scan`s.
///
/// - `.nii` / `.nii.gz` files become one scan each.
/// - DICOM files are grouped into series by their SeriesInstanceUID.
/// - DICOM-SEG (segmentation) series are skipped with a warning: the viewer shows images only.
nonisolated enum ScanImporter {
    static func importItems(at urls: [URL]) -> ImportResult {
        var niftiFiles: [URL] = []
        var dicomFiles: [(url: URL, header: DicomHeader)] = []
        var emptyFiles = 0
        var unreadable = 0

        for file in allFiles(in: urls) {
            let size = (try? file.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
            if size == 0 { emptyFiles += 1; continue }

            if file.isNIfTI {
                niftiFiles.append(file)
            } else if file.pathExtension.lowercased() == "dcm" || (file.pathExtension.isEmpty && DicomHeaderReader.hasDicomMagic(file)) {
                if let header = DicomHeaderReader.read(file) {
                    dicomFiles.append((file, header))
                } else {
                    unreadable += 1
                }
            }
        }

        var result = ImportResult()
        result.scans += niftiFiles.sorted { $0.path < $1.path }.map { file in
            Scan(name: file.scanBaseName, format: .nifti, modality: "", summary: "NIfTI volume", imageFiles: [file])
        }
        let (dicomScans, segmentations) = groupDicom(dicomFiles)
        result.scans += dicomScans

        if segmentations > 0 {
            result.warnings.append(segmentations == 1
                ? "1 segmentation wasn't opened because the app doesn't support segmentations yet."
                : "\(segmentations) segmentations weren't opened because the app doesn't support segmentations yet.")
        }

        if emptyFiles > 0 {
            result.warnings.append("\(emptyFiles) file\(emptyFiles == 1 ? " is" : "s are") empty (0 bytes) and were skipped. The download may be incomplete.")
        }
        if unreadable > 0 {
            result.warnings.append("\(unreadable) DICOM file\(unreadable == 1 ? "" : "s") couldn't be read (unsupported encoding).")
        }
        return result
    }

    // MARK: - Files

    private static func allFiles(in urls: [URL]) -> [URL] {
        var files: [URL] = []
        for url in urls {
            var isDirectory: ObjCBool = false
            guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory) else { continue }
            if !isDirectory.boolValue {
                files.append(url)
                continue
            }
            let enumerator = FileManager.default.enumerator(
                at: url, includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey], options: [.skipsHiddenFiles])
            while let item = enumerator?.nextObject() as? URL {
                if (try? item.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true {
                    files.append(item)
                }
            }
        }
        return files
    }

    // MARK: - DICOM series

    /// Returns the image series as scans, plus how many segmentation series were skipped.
    private static func groupDicom(_ files: [(url: URL, header: DicomHeader)]) -> (scans: [Scan], segmentations: Int) {
        // Group by series UID; fall back to the containing folder if a file has none.
        let groups = Dictionary(grouping: files) { file in
            file.header.seriesInstanceUID ?? file.url.deletingLastPathComponent().path
        }

        var imageSeries: [(header: DicomHeader, files: [URL])] = []
        var segmentations = 0
        for (_, members) in groups {
            let header = members[0].header
            if header.modality == "SEG" { segmentations += 1; continue } // segmentation objects aren't images
            let urls = members.map(\.url).sorted { $0.lastPathComponent < $1.lastPathComponent }
            imageSeries.append((header, urls))
        }

        let scans = imageSeries.map { series -> Scan in
            let header = series.header
            let modality = header.modality ?? ""
            // Prefer the series description; otherwise "CT series 3" rather than a long UID folder name.
            let fallback = header.seriesNumber.map { "series \($0)" } ?? "series"
            let description = header.seriesDescription ?? fallback
            var name = modality.isEmpty ? description.capitalizedFirst : "\(modality) · \(description)"
            if name.count > 60 { name = String(name.prefix(57)) + "…" }

            var parts: [String] = []
            if series.files.count == 1, let frames = header.numberOfFrames, frames > 1 {
                parts.append("\(frames) frames")
            } else {
                parts.append("\(series.files.count) slice\(series.files.count == 1 ? "" : "s")")
            }
            if let date = formattedDate(header.studyDate) { parts.append(date) }
            return Scan(name: name, format: .dicom, modality: modality, summary: parts.joined(separator: " · "),
                        imageFiles: series.files)
        }

        let sorted = scans.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        return (sorted, segmentations)
    }

    private static func formattedDate(_ dicomDate: String?) -> String? {
        guard let dicomDate, dicomDate.count == 8 else { return nil }
        let parser = DateFormatter()
        parser.dateFormat = "yyyyMMdd"
        parser.locale = Locale(identifier: "en_US_POSIX")
        guard let date = parser.date(from: dicomDate) else { return nil }
        return date.formatted(date: .abbreviated, time: .omitted)
    }
}

nonisolated private extension String {
    var capitalizedFirst: String { prefix(1).uppercased() + dropFirst() }
}

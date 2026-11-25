import Foundation

/// The handful of DICOM attributes the app needs to group files into series and name them.
///
/// Patient-identifying attributes (name, ID, birth date) are deliberately never read.
nonisolated struct DicomHeader: Sendable, Equatable {
    var modality: String?
    var seriesDescription: String?
    var studyDate: String?
    var studyInstanceUID: String?
    var seriesInstanceUID: String?
    var seriesNumber: String?
    var frameOfReferenceUID: String?
    var numberOfFrames: Int?
}

/// A small, read-only DICOM header parser.
///
/// It walks the data elements of the first part of a file and stops before the pixel data.
/// It understands Explicit and Implicit VR Little Endian, which covers nearly all files
/// produced by scanners and archives. Anything it can't read simply returns `nil`.
nonisolated enum DicomHeaderReader {
    /// Reads at most this many bytes; the attributes we need are always near the start.
    static let maxHeaderBytes = 256 * 1024

    static func read(_ url: URL) -> DicomHeader? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        guard let data = try? handle.read(upToCount: maxHeaderBytes) else { return nil }
        return parse(data)
    }

    /// True when the file starts with the standard 128-byte preamble followed by "DICM".
    static func hasDicomMagic(_ url: URL) -> Bool {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return false }
        defer { try? handle.close() }
        guard let data = try? handle.read(upToCount: 132), data.count == 132 else { return false }
        return data[128..<132].elementsEqual("DICM".utf8)
    }

    static func parse(_ data: Data) -> DicomHeader? {
        var reader = ByteReader(bytes: [UInt8](data))
        var explicit = true

        if reader.bytes.count >= 132, reader.bytes[128..<132].elementsEqual("DICM".utf8) {
            reader.offset = 132
            // File meta information (group 0002) is always Explicit VR Little Endian.
            while let tag = reader.peekTag(), tag.group == 0x0002 {
                guard let element = reader.readElement(explicit: true) else { return nil }
                if tag.element == 0x0010, let ts = element.string {
                    if ts == "1.2.840.10008.1.2" { explicit = false }        // Implicit VR LE
                    if ts == "1.2.840.10008.1.2.2" { return nil }            // Big endian: unsupported
                }
            }
        } else {
            // No preamble: old-style file. Guess the encoding from the first element.
            guard reader.bytes.count >= 8 else { return nil }
            explicit = reader.bytes[4].isUppercaseASCII && reader.bytes[5].isUppercaseASCII
        }

        var header = DicomHeader()
        var sawAnything = false
        while let tag = reader.peekTag() {
            if tag.group > 0x0028 { break } // everything we need lives in groups 0008–0028
            guard let element = reader.readElement(explicit: explicit) else { break }
            sawAnything = true
            switch (tag.group, tag.element) {
            case (0x0008, 0x0060): header.modality = element.string
            case (0x0008, 0x103E): header.seriesDescription = element.string
            case (0x0008, 0x0020): header.studyDate = element.string
            case (0x0020, 0x000D): header.studyInstanceUID = element.string
            case (0x0020, 0x000E): header.seriesInstanceUID = element.string
            case (0x0020, 0x0011): header.seriesNumber = element.string
            case (0x0020, 0x0052): header.frameOfReferenceUID = element.string
            case (0x0028, 0x0008): header.numberOfFrames = element.string.flatMap { Int($0) }
            default: break
            }
        }
        return sawAnything ? header : nil
    }
}

// MARK: - Byte-level parsing

nonisolated private struct Tag {
    let group: UInt16
    let element: UInt16
}

nonisolated private struct Element {
    let value: ArraySlice<UInt8>?
    var string: String? {
        guard let value else { return nil }
        let text = String(decoding: value, as: UTF8.self)
            .trimmingCharacters(in: .whitespacesAndNewlines.union(CharacterSet(charactersIn: "\0")))
        return text.isEmpty ? nil : text
    }
}

nonisolated private struct ByteReader {
    let bytes: [UInt8]
    var offset = 0

    private static let longVRs: Set<String> = ["OB", "OD", "OF", "OL", "OV", "OW", "SQ", "SV", "UC", "UN", "UR", "UT", "UV"]
    private static let undefinedLength: UInt32 = 0xFFFF_FFFF

    func u16(_ at: Int) -> UInt16? {
        guard at + 2 <= bytes.count else { return nil }
        return UInt16(bytes[at]) | UInt16(bytes[at + 1]) << 8
    }

    func u32(_ at: Int) -> UInt32? {
        guard at + 4 <= bytes.count else { return nil }
        return UInt32(bytes[at]) | UInt32(bytes[at + 1]) << 8 | UInt32(bytes[at + 2]) << 16 | UInt32(bytes[at + 3]) << 24
    }

    func peekTag() -> Tag? {
        guard let group = u16(offset), let element = u16(offset + 2) else { return nil }
        return Tag(group: group, element: element)
    }

    /// Reads one element and moves past it. Sequences are skipped (value = nil).
    mutating func readElement(explicit: Bool) -> Element? {
        guard let tag = peekTag() else { return nil }
        offset += 4
        let length: UInt32
        var isSequence = false

        if explicit && tag.group != 0xFFFE {
            guard offset + 2 <= bytes.count else { return nil }
            let vr = String(decoding: bytes[offset..<offset + 2], as: UTF8.self)
            offset += 2
            if Self.longVRs.contains(vr) {
                offset += 2 // reserved
                guard let l = u32(offset) else { return nil }
                length = l
                offset += 4
            } else {
                guard let l = u16(offset) else { return nil }
                length = UInt32(l)
                offset += 2
            }
            isSequence = vr == "SQ"
        } else {
            guard let l = u32(offset) else { return nil }
            length = l
            offset += 4
        }

        if length == Self.undefinedLength {
            // Undefined length: a sequence (or encapsulated data) ended by a delimiter.
            guard skipUndefinedLength(explicit: explicit) else { return nil }
            return Element(value: nil)
        }
        let end = offset + Int(length)
        guard end <= bytes.count else { return nil }
        defer { offset = end }
        return Element(value: isSequence ? nil : bytes[offset..<end])
    }

    /// Skips sequence items until the matching Sequence Delimitation Item (FFFE,E0DD).
    mutating func skipUndefinedLength(explicit: Bool) -> Bool {
        while let tag = peekTag() {
            if tag.group == 0xFFFE && tag.element == 0xE0DD { offset += 8; return true }
            if tag.group == 0xFFFE && tag.element == 0xE000 {
                offset += 4
                guard let length = u32(offset) else { return false }
                offset += 4
                if length == Self.undefinedLength {
                    // Item of undefined length: elements until Item Delimitation (FFFE,E00D).
                    while let inner = peekTag() {
                        if inner.group == 0xFFFE && inner.element == 0xE00D { offset += 8; break }
                        guard readElement(explicit: explicit) != nil else { return false }
                    }
                } else {
                    offset += Int(length)
                }
                continue
            }
            return false // malformed
        }
        return false
    }
}

nonisolated private extension UInt8 {
    var isUppercaseASCII: Bool { self >= 65 && self <= 90 }
}

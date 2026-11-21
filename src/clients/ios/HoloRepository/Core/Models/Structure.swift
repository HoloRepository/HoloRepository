//
//  Structure.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation

/// Anatomical group of a segmented structure. Case order is the display order (lesions first).
enum StructureGroup: String, Codable, Hashable, Sendable, CaseIterable, Identifiable {
    case lesions
    case organs
    case vessels
    case bones
    case muscles

    var id: Self { self }

    var displayName: String {
        switch self {
        case .lesions: "Lesions"
        case .organs: "Organs"
        case .vessels: "Vessels"
        case .bones: "Bones"
        case .muscles: "Muscles"
        }
    }

    /// Lesions always stay fully opaque so they can't be faded out by mistake.
    var hasAdjustableOpacity: Bool { self != .lesions }
}

/// One segmented structure in a study's 3D model.
struct Structure: Codable, Identifiable, Hashable, Sendable {
    /// The NV-Segment-CT label name, exactly as the model outputs it, e.g. "kidney mass", "right kidney", "left rib 9".
    let id: String
    /// Display name, e.g. "Kidney mass (right)".
    let name: String
    /// Plain-language name for the patient app, e.g. "Main blood vessel (aorta)".
    let patientFriendlyName: String?
    /// One plain sentence for the patient app.
    let patientDescription: String?
    let group: StructureGroup
    /// "#RRGGBB". Lesions use #FF2D95 (Theme.lesion) and nothing else does.
    let colorHex: String
    let volumeML: Double
}

extension Structure {
    /// Name shown to patients; falls back to the display name.
    var patientName: String { patientFriendlyName ?? name }
}

//
//  Study.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// Imaging modality. CT only for now: the daemon segments CT with NVIDIA NV-Segment-CT.
/// Kept as a type so other modalities can be added later.
enum Modality: String, Codable, Hashable, Sendable {
    case ct
}

/// One imaging study (scan) for a patient. Every study has a 3D model (`StudyModel`).
struct Study: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let modality: Modality
    let bodyPart: String
    let date: Date
    let description: String
}

// MARK: - Display helpers

extension Modality {
    /// Used in scan names such as "CT abdomen".
    var displayName: String {
        switch self {
        case .ct: "CT"
        }
    }
}

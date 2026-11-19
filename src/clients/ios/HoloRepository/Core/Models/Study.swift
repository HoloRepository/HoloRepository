//
//  Study.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// Imaging modality, used for the CT / MRI / X-ray filter.
enum Modality: String, Codable, Hashable, Sendable {
    case ct
    case mri
    case xray
}

/// One imaging study (scan) for a patient.
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
    var displayName: String {
        switch self {
        case .ct: "CT"
        case .mri: "MRI"
        case .xray: "X-ray"
        }
    }

    var systemImage: String {
        switch self {
        case .ct: "cube"
        case .mri: "square.stack.3d.up"
        case .xray: "rectangle.portrait"
        }
    }
}

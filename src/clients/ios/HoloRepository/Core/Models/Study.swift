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

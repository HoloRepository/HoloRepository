//
//  Patient.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// A patient's clinical record.
struct Patient: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let name: String
    let dateOfBirth: Date
    /// Fake hospital number used in mock data only.
    let hospitalNumber: String
}

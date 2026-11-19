//
//  Patient.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// Sex as recorded in the clinical record.
enum Sex: String, Codable, Hashable, Sendable {
    case female
    case male
    case other
}

/// A patient's clinical record.
struct Patient: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let name: String
    let dateOfBirth: Date
    let sex: Sex
    /// Fake hospital number used in mock data only.
    let hospitalNumber: String
    /// The clinician responsible for this patient.
    let assignedDoctorId: String?
    /// Clinicians allowed to view this record. A doctor only ever sees patients listing their id here.
    let authorisedDoctorIds: [String]
    /// Key conditions, as short plain-text lines.
    let conditions: [String]
    /// Previous procedures, as short plain-text lines.
    let previousProcedures: [String]
}

// MARK: - Display helpers

extension Patient {
    /// Age in whole years on the given date.
    func age(on date: Date = .now, calendar: Calendar = .current) -> Int {
        calendar.dateComponents([.year], from: dateOfBirth, to: date).year ?? 0
    }

    /// Only the last 4 characters are shown on screen, e.g. "•••• 0101".
    var maskedHospitalNumber: String {
        "•••• \(hospitalNumber.suffix(4))"
    }

    /// Up to two initials for the avatar, e.g. "AP".
    var initials: String {
        name.split(separator: " ").prefix(2).compactMap(\.first).map(String.init).joined()
    }
}

extension Sex {
    var displayName: String {
        switch self {
        case .female: "Female"
        case .male: "Male"
        case .other: "Other"
        }
    }
}

//
//  Appointment.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// A clinic appointment or MDT consultation for a patient.
struct Appointment: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let date: Date
    let department: String
    let clinicianName: String
}

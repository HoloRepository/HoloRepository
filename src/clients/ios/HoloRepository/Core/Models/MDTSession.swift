//
//  MDTSession.swift
//  
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation

/// Whether an MDT (multidisciplinary team) session is scheduled, running now or finished.
enum MDTSessionStatus: String, Codable, Hashable, Sendable {
    case scheduled
    case active
    case completed
}

/// An MDT consultation about one patient's case.
struct MDTSession: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let caseName: String
    let startTime: Date
    let participantCount: Int
    let status: MDTSessionStatus
}

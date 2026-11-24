//
//  MDTSession.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// Whether an MDT (multidisciplinary team) session is scheduled, running now or finished, as stored.
/// Screens show `liveStatus(at:)`, which is worked out from the times.
enum MDTSessionStatus: String, Codable, Hashable, Sendable {
    case scheduled
    case active
    case completed
}

/// One case discussed in an MDT session: a patient and their CT study.
struct MDTCase: Codable, Hashable, Sendable {
    let patientId: String
    let studyId: String
}

/// An MDT consultation. `patientId` is the main patient; `cases` lists every case discussed.
struct MDTSession: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let caseName: String
    let startTime: Date
    let participantCount: Int
    let status: MDTSessionStatus
    /// Nil means one hour after the start.
    let endTime: Date?
    /// e.g. "Urology".
    let specialty: String?
    /// Room for in-person sessions; nil means online.
    let location: String?
    /// Participant names, if known.
    let participants: [String]?
    /// Cases to discuss. Nil means just `patientId`'s case.
    let cases: [MDTCase]?
}

/// Status shown on screen, worked out from the session times.
enum MDTLiveStatus: Hashable, Sendable {
    case live
    case startingSoon
    case scheduled
    case completed

    var displayName: String {
        switch self {
        case .live: "Live"
        case .startingSoon: "Starting soon"
        case .scheduled: "Scheduled"
        case .completed: "Completed"
        }
    }
}

extension MDTSession {
    /// How long before the start a session counts as "starting soon".
    static let startingSoonInterval: TimeInterval = 30 * 60

    var effectiveEndTime: Date {
        endTime ?? startTime.addingTimeInterval(60 * 60)
    }

    func liveStatus(at now: Date = .now) -> MDTLiveStatus {
        if status == .completed || now >= effectiveEndTime { return .completed }
        if now >= startTime { return .live }
        if startTime.timeIntervalSince(now) <= Self.startingSoonInterval { return .startingSoon }
        return .scheduled
    }
}

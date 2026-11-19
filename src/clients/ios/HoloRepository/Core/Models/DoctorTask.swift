//
//  DoctorTask.swift
//  
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation

/// What a clinician needs to do.
enum DoctorTaskKind: String, Codable, Hashable, Sendable {
    /// A report (often AI-drafted) needs clinician review.
    case reviewReport
    /// Markers on a 3D study need checking.
    case checkMarkers
}

/// A pending task on the doctor's Home screen.
struct DoctorTask: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let kind: DoctorTaskKind
    let title: String
    /// The report or study the task refers to.
    let reportId: String?
    let studyId: String?
    /// Verification state of the linked report, if there is one.
    let verificationState: VerificationState?
}

//
//  DoctorTask.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// What a clinician needs to do. Each kind opens an existing screen (see the doctor Home).
enum DoctorTaskKind: String, Codable, Hashable, Sendable {
    /// Look through a CT study in the 3D viewer.
    case reviewScan
    /// Check markers and comments on a 3D study.
    case reviewAnnotations
    /// Review and approve a clinical report (often AI-drafted).
    case approveReport
    /// Accept an invitation to an MDT session.
    case acceptMDTInvitation
    /// Prepare a patient's case for an MDT session.
    case prepareMDTCase
}

/// Where a task is in the clinician's workflow. Separate from priority.
enum DoctorTaskStatus: String, Codable, Hashable, Sendable, CaseIterable {
    case notStarted
    case inProgress
    case completed

    var displayName: String {
        switch self {
        case .notStarted: "Not started"
        case .inProgress: "In progress"
        case .completed: "Completed"
        }
    }
}

/// How soon a task needs doing. Separate from status: an urgent task can be not started or in progress.
enum DoctorTaskPriority: String, Codable, Hashable, Sendable {
    case normal
    case urgent
}

/// A task on the doctor's Home screen. Tasks are separate from notifications.
struct DoctorTask: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let kind: DoctorTaskKind
    let title: String
    let status: DoctorTaskStatus
    let priority: DoctorTaskPriority
    let dueDate: Date?
    /// The report, study or MDT session the task refers to.
    let reportId: String?
    let studyId: String?
    let sessionId: String?
    /// Verification state of the linked report, if there is one.
    let verificationState: VerificationState?

    var isUrgent: Bool { priority == .urgent }
}

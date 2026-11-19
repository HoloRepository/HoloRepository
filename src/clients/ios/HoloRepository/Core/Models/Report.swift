//
//  Report.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// The real verification state of report content. Never a confidence score.
enum VerificationState: String, Codable, Hashable, Sendable {
    /// Linked to an identifiable clinical source.
    case verified
    /// AI-generated finding that needs clinician verification.
    case requiresReview
    /// Generated automatically and not yet approved.
    case aiGenerated
}

/// A clinical or MDT report linked to a study.
struct Report: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let studyId: String
    let title: String
    let summary: String
    let createdAt: Date
    let verificationState: VerificationState
    /// Patients only ever see reports where this is true.
    let approvedForPatient: Bool
}

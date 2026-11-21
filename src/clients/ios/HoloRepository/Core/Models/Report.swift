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

    var displayName: String {
        switch self {
        case .verified: "Verified reference"
        case .requiresReview: "Requires review"
        case .aiGenerated: "AI-generated"
        }
    }
}

/// One finding in a clinician report, with its own verification state.
struct Finding: Codable, Identifiable, Hashable, Sendable {
    let id: String
    var text: String
    var verificationState: VerificationState
    /// Where a verified reference comes from, e.g. "Radiology report, 10 Nov". Nil for unverified findings.
    var source: String?
}

/// A clinical or MDT report linked to a study.
struct Report: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let studyId: String
    let title: String
    /// One-line summary for lists.
    let summary: String
    let createdAt: Date
    /// State of the report as a whole.
    var verificationState: VerificationState
    /// Patients only ever see reports where this is true.
    var approvedForPatient: Bool

    var findings: [Finding]
    let impression: String
    let recommendations: String
    /// Plain-language version for the patient (reading age about 9).
    var patientSummary: String

    /// Name of the clinician who approved it, e.g. "Dr Sarah Smith".
    var approvedBy: String?
    var approvedAt: Date?
}

// MARK: - Review actions

extension Report {
    /// Clinician approval: the patient-friendly version becomes visible to the patient, and every finding
    /// becomes a verified reference with the approving clinician as its source.
    func approved(by approver: String, at date: Date) -> Report {
        var report = self
        let sourceText = "Approved by \(approver), \(date.formatted(.dateTime.day().month(.abbreviated)))"
        report.findings = findings.map { finding in
            guard finding.verificationState != .verified else { return finding }
            var verified = finding
            verified.verificationState = .verified
            verified.source = sourceText
            return verified
        }
        report.verificationState = .verified
        report.approvedForPatient = true
        report.approvedBy = approver
        report.approvedAt = date
        return report
    }

    /// Applies edited text. Any finding whose text changed loses its source and needs review again,
    /// and so does the report, until it's approved.
    func edited(findingTexts: [String: String], patientSummary newSummary: String) -> Report {
        var report = self
        var changed = false
        report.findings = findings.map { finding in
            guard let text = findingTexts[finding.id], text != finding.text else { return finding }
            changed = true
            var edited = finding
            edited.text = text
            edited.verificationState = .requiresReview
            edited.source = nil
            return edited
        }
        if newSummary != patientSummary {
            changed = true
            report.patientSummary = newSummary
        }
        if changed {
            report.verificationState = .requiresReview
        }
        return report
    }
}

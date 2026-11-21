//
//  PatientContentModel.swift
//
//
//  Created by Ng Xin Ning on 21/11/2026.
//

import Foundation
import Observation

/// One scan the patient may open: a CT study with its 3D model and the report a clinician approved for them.
struct PatientScan: Identifiable, Hashable {
    let study: Study
    let studyModel: StudyModel
    /// The newest report on this study that is approved for the patient.
    let report: Report

    var id: String { study.id }
}

/// One line in the patient's recent activity, already in plain words.
struct PatientActivity: Identifiable, Hashable {
    let id: String
    let systemImage: String
    let text: String
    let timestamp: Date
}

/// Everything the patient app shows, loaded from `MockDataService` and the `ReportStore`.
///
/// This is the one place the patient filter lives: only the signed-in patient's own record, and only
/// content approved for them. No AI drafts, unapproved reports, clinical comments or other patients.
@Observable
final class PatientContentModel {
    private(set) var patient: Patient?
    /// Scans with an approved report, newest first.
    private(set) var scans: [PatientScan] = []
    /// Reports approved for the patient, newest approval first.
    private(set) var approvedReports: [Report] = []
    /// Future appointments, soonest first.
    private(set) var upcomingAppointments: [Appointment] = []
    /// Newest first.
    private(set) var activity: [PatientActivity] = []
    private(set) var hasLoaded = false

    var latestScan: PatientScan? { scans.first }
    var latestReport: Report? { approvedReports.first }
    var nextAppointment: Appointment? { upcomingAppointments.first }

    /// `reportStore` nil uses the unedited mock reports (previews).
    func load(from service: MockDataService, reportStore: (any ReportStore)? = nil,
              user: User?, now: Date = .now) {
        defer { hasLoaded = true }
        // The account must be linked to a verified clinical record; otherwise show nothing.
        guard let user, user.role == .patient, let patientId = user.patientId,
              let patient = service.patients.first(where: { $0.id == patientId }) else {
            clear()
            return
        }
        self.patient = patient

        let reports = reportStore?.allReports() ?? service.reports
        approvedReports = reports
            .filter { $0.patientId == patientId && Self.isApprovedForPatient($0) }
            .sorted { ($0.approvedAt ?? $0.createdAt) > ($1.approvedAt ?? $1.createdAt) }

        let modelsByStudy = Dictionary(service.studyModels.map { ($0.studyId, $0) },
                                       uniquingKeysWith: { first, _ in first })
        scans = service.studies
            .filter { $0.patientId == patientId }
            .sorted { $0.date > $1.date }
            .compactMap { study in
                guard let model = modelsByStudy[study.id],
                      let report = approvedReports.first(where: { $0.studyId == study.id }) else { return nil }
                return PatientScan(study: study, studyModel: model, report: report)
            }

        upcomingAppointments = service.appointments
            .filter { $0.patientId == patientId && $0.date > now }
            .sorted { $0.date < $1.date }

        activity = service.activityItems
            .filter { $0.patientId == patientId }
            .compactMap(patientActivity)
            .sorted { $0.timestamp > $1.timestamp }
    }

    func scan(studyId: String) -> PatientScan? {
        scans.first { $0.study.id == studyId }
    }

    func report(id: String) -> Report? {
        approvedReports.first { $0.id == id }
    }

    func appointment(id: String) -> Appointment? {
        upcomingAppointments.first { $0.id == id }
    }

    // MARK: Private

    private static func isApprovedForPatient(_ report: Report) -> Bool {
        report.approvedForPatient && report.verificationState == .verified
    }

    /// Plain-language version of an activity item. Clinicians' comments are clinical notes and never shown;
    /// scan and report events are shown only once the patient has approved content to open.
    private func patientActivity(_ item: ActivityItem) -> PatientActivity? {
        switch item.kind {
        case .scanUploaded:
            guard !scans.isEmpty else { return nil }
            return PatientActivity(id: item.id, systemImage: "cube",
                                   text: "Your new scan is ready to look at", timestamp: item.timestamp)
        case .reportApproved:
            guard !approvedReports.isEmpty else { return nil }
            return PatientActivity(id: item.id, systemImage: "checkmark.seal",
                                   text: "Your doctor checked and shared your report", timestamp: item.timestamp)
        case .commentAdded:
            return nil
        }
    }

    private func clear() {
        patient = nil
        scans = []
        approvedReports = []
        upcomingAppointments = []
        activity = []
    }
}

// MARK: - Previews

extension PatientContentModel {
    /// Loaded with the mock data for the demo patient (Alex Patel).
    static func demo() -> PatientContentModel {
        let model = PatientContentModel()
        let service = MockDataService()
        model.load(from: service, user: service.patientUser)
        return model
    }

    /// A patient with nothing approved yet.
    static func empty() -> PatientContentModel {
        let model = PatientContentModel()
        model.load(from: MockDataService(), user: nil)
        return model
    }
}

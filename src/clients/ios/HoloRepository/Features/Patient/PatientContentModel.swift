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

/// Patient search results, grouped by type.
struct PatientSearchResults {
    let reports: [Report]
    let scans: [PatientScan]
    let appointments: [Appointment]

    var isEmpty: Bool { reports.isEmpty && scans.isEmpty && appointments.isEmpty }
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
    /// All of the patient's appointments, newest first (for search and detail).
    private(set) var appointments: [Appointment] = []
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

        appointments = service.appointments
            .filter { $0.patientId == patientId }
            .sorted { $0.date > $1.date }
        upcomingAppointments = appointments
            .filter { $0.date > now }
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
        appointments.first { $0.id == id }
    }

    /// Searches only this patient's approved reports, scans and appointments.
    /// An empty query returns everything, grouped.
    // Later: messages and medical history can be added as more result groups.
    func search(_ query: String) -> PatientSearchResults {
        let words = query.lowercased().split(separator: " ").map(String.init)
        func matches(_ fields: [String?]) -> Bool {
            let text = fields.compactMap { $0 }.joined(separator: " ").lowercased()
            return words.allSatisfy { text.contains($0) }
        }
        return PatientSearchResults(
            reports: approvedReports.filter { report in
                let scan = scan(studyId: report.studyId)
                return matches(["report", "results", report.title, report.approvedBy,
                                scan?.study.patientName, scan?.study.bodyPart])
            },
            scans: scans.filter { scan in
                matches(["scan", scan.study.patientName, scan.study.patientDescription,
                         scan.study.description, scan.study.bodyPart, scan.study.modality.displayName,
                         scan.study.date.formatted(.dateTime.day().month(.wide).year())])
            },
            appointments: appointments.filter { appointment in
                matches(["appointment", appointment.clinicianName, appointment.department,
                         appointment.date.formatted(.dateTime.day().month(.wide).year())])
            })
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
        appointments = []
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

    /// The demo patient, seen a year from now: every appointment is in the past.
    static func noUpcomingAppointment() -> PatientContentModel {
        let model = PatientContentModel()
        let service = MockDataService()
        model.load(from: service, user: service.patientUser,
                   now: Calendar.current.date(byAdding: .year, value: 1, to: .now) ?? .distantFuture)
        return model
    }

    /// A patient with nothing approved yet.
    static func empty() -> PatientContentModel {
        let model = PatientContentModel()
        model.load(from: MockDataService(), user: nil)
        return model
    }
}

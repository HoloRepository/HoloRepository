//
//  PatientsModel.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation
import Observation

/// The two patient list filters.
enum PatientFilter: String, CaseIterable, Identifiable, Hashable {
    case assignedToMe
    case allAuthorised

    var id: Self { self }

    var title: String {
        switch self {
        case .assignedToMe: "Assigned to me"
        case .allAuthorised: "All authorised"
        }
    }
}

/// Clinical data for the doctor's Patients, Imaging and Reports tabs, loaded from `MockDataService`
/// and the `ReportStore`. Holds only patients the signed-in doctor is authorised to see, and their records,
/// so all three tabs share one authorisation check and see the same report approvals.
@Observable
final class PatientsModel {
    private(set) var doctorId: String?
    private(set) var patients: [Patient] = []
    /// Studies of authorised patients, newest first.
    private(set) var allStudies: [Study] = []
    /// Reports of authorised patients, newest first, with saved approvals and edits.
    private(set) var allReports: [Report] = []
    private(set) var hasLoaded = false

    private var reportStore: (any ReportStore)?
    private var studiesByPatient: [String: [Study]] = [:]
    private var consultationsByPatient: [String: [MDTSession]] = [:]
    private var studyModelsByStudy: [String: StudyModel] = [:]

    /// `reportStore` nil keeps report changes in memory (previews).
    func load(from service: MockDataService, reportStore: (any ReportStore)? = nil, doctorId: String?) {
        self.doctorId = doctorId
        let reportStore = reportStore ?? LocalReportStore.inMemory(baseReports: service.reports)
        self.reportStore = reportStore
        // Authorisation is the first filter: nothing else in the tab can see other patients.
        patients = service.patients
            .filter { patient in doctorId.map { patient.authorisedDoctorIds.contains($0) } ?? false }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }

        let allowedIds = Set(patients.map(\.id))
        allStudies = service.studies
            .filter { allowedIds.contains($0.patientId) }
            .sorted { $0.date > $1.date }
        studiesByPatient = Dictionary(grouping: allStudies, by: \.patientId)
        let allowedStudyIds = Set(allStudies.map(\.id))
        studyModelsByStudy = Dictionary(
            service.studyModels.filter { allowedStudyIds.contains($0.studyId) }.map { ($0.studyId, $0) },
            uniquingKeysWith: { first, _ in first })
        allReports = reportStore.allReports()
            .filter { allowedIds.contains($0.patientId) }
            .sorted { $0.createdAt > $1.createdAt }
        consultationsByPatient = Dictionary(grouping: service.mdtSessions.filter { allowedIds.contains($0.patientId) },
                                            by: \.patientId)
            .mapValues { $0.sorted { $0.startTime > $1.startTime } }
        hasLoaded = true
    }

    /// Patients for the list, after the segmented filter and search (name or hospital number).
    func patients(for filter: PatientFilter, matching query: String) -> [Patient] {
        let filtered = switch filter {
        case .assignedToMe: patients.filter { $0.assignedDoctorId != nil && $0.assignedDoctorId == doctorId }
        case .allAuthorised: patients
        }

        let trimmed = query.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return filtered }
        return filtered.filter {
            $0.name.localizedCaseInsensitiveContains(trimmed)
                || $0.hospitalNumber.localizedCaseInsensitiveContains(trimmed)
        }
    }

    func patient(id: String) -> Patient? {
        patients.first { $0.id == id }
    }

    /// Newest first.
    func studies(for patientId: String) -> [Study] {
        studiesByPatient[patientId] ?? []
    }

    func latestStudy(for patientId: String) -> Study? {
        studies(for: patientId).first
    }

    /// Newest first.
    func reports(for patientId: String) -> [Report] {
        allReports.filter { $0.patientId == patientId }
    }

    func report(id: String) -> Report? {
        allReports.first { $0.id == id }
    }

    // MARK: Report review

    /// Approves a report for the patient and saves it.
    func approveReport(id: String, by approver: String, at date: Date = .now) throws {
        guard let report = report(id: id) else { return }
        try save(report.approved(by: approver, at: date))
    }

    /// Saves edited finding texts (keyed by finding id) and patient summary.
    func editReport(id: String, findingTexts: [String: String], patientSummary: String) throws {
        guard let report = report(id: id) else { return }
        try save(report.edited(findingTexts: findingTexts, patientSummary: patientSummary))
    }

    private func save(_ report: Report) throws {
        try reportStore?.save(report)
        if let index = allReports.firstIndex(where: { $0.id == report.id }) {
            allReports[index] = report
        }
    }

    /// Newest first, so upcoming meetings are above past ones.
    func consultations(for patientId: String) -> [MDTSession] {
        consultationsByPatient[patientId] ?? []
    }

    func study(id: String) -> Study? {
        allStudies.first { $0.id == id }
    }

    /// The study's 3D model. Every study should have one; nil means the mock data is incomplete.
    func studyModel(for studyId: String) -> StudyModel? {
        studyModelsByStudy[studyId]
    }

    /// Loaded with the mock data for the demo doctor, for previews.
    static func demo() -> PatientsModel {
        let model = PatientsModel()
        let service = MockDataService()
        model.load(from: service, doctorId: service.doctorUser.id)
        return model
    }
}

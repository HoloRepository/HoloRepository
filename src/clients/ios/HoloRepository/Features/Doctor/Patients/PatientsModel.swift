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

/// Clinical data for the doctor's Patients and Imaging tabs, loaded from `MockDataService`.
/// Holds only patients the signed-in doctor is authorised to see, and their records, so both tabs
/// share one authorisation check.
@Observable
final class PatientsModel {
    private(set) var doctorId: String?
    private(set) var patients: [Patient] = []
    /// Studies of authorised patients, newest first.
    private(set) var allStudies: [Study] = []
    private(set) var hasLoaded = false

    private var studiesByPatient: [String: [Study]] = [:]
    private var reportsByPatient: [String: [Report]] = [:]
    private var consultationsByPatient: [String: [MDTSession]] = [:]
    private var studyModelsByStudy: [String: StudyModel] = [:]

    func load(from service: MockDataService, doctorId: String?) {
        self.doctorId = doctorId
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
        reportsByPatient = Dictionary(grouping: service.reports.filter { allowedIds.contains($0.patientId) },
                                      by: \.patientId)
            .mapValues { $0.sorted { $0.createdAt > $1.createdAt } }
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
        reportsByPatient[patientId] ?? []
    }

    /// Newest first, so upcoming meetings are above past ones.
    func consultations(for patientId: String) -> [MDTSession] {
        consultationsByPatient[patientId] ?? []
    }

    func study(id: String) -> Study? {
        allStudies.first { $0.id == id }
    }

    /// The study's 3D model, if it has one ("3D ready").
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

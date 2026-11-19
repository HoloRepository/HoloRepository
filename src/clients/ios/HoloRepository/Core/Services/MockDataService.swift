//
//  MockDataService.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// Hard-coded demo data for the December prototype. No network calls.
/// All names and numbers are fake. A DaemonService with the same interface replaces this later.
struct MockDataService: Sendable {

    // MARK: Users

    let doctorUser = User(id: "u-doc-001", name: "Dr Sarah Smith", role: .doctor)
    let patientUser = User(id: "u-pat-001", name: "Alex Patel", role: .patient)

    /// The demo account to sign in as for a role (any credentials are accepted).
    func demoUser(for role: UserRole) -> User {
        switch role {
        case .doctor: doctorUser
        case .patient: patientUser
        }
    }

    // MARK: Patients

    let patients: [Patient] = [
        Patient(id: "p-001", name: "Alex Patel", dateOfBirth: date(1984, 3, 12), hospitalNumber: "HR0000101"),
        Patient(id: "p-002", name: "Jordan Lee", dateOfBirth: date(1971, 9, 2), hospitalNumber: "HR0000102"),
        Patient(id: "p-003", name: "Morgan Hughes", dateOfBirth: date(1995, 6, 27), hospitalNumber: "HR0000103")
    ]

    // MARK: Studies

    let studies: [Study] = [
        Study(id: "s-001", patientId: "p-001", modality: .ct, bodyPart: "Abdomen",
              date: date(2026, 11, 10), description: "CT abdomen with contrast"),
        Study(id: "s-002", patientId: "p-001", modality: .mri, bodyPart: "Liver",
              date: date(2026, 8, 4), description: "MRI liver follow-up"),
        Study(id: "s-003", patientId: "p-002", modality: .ct, bodyPart: "Chest",
              date: date(2026, 11, 3), description: "CT chest, routine"),
        Study(id: "s-004", patientId: "p-003", modality: .xray, bodyPart: "Left wrist",
              date: date(2026, 10, 21), description: "X-ray left wrist after fall")
    ]

    // MARK: Reports

    let reports: [Report] = [
        Report(id: "r-001", patientId: "p-001", studyId: "s-001", title: "CT abdomen report",
               summary: "Small lesion in the right lobe of the liver, unchanged since the last scan.",
               createdAt: date(2026, 11, 12), verificationState: .verified, approvedForPatient: true),
        Report(id: "r-002", patientId: "p-001", studyId: "s-002", title: "MRI liver MDT summary",
               summary: "AI-drafted MDT summary awaiting clinician review.",
               createdAt: date(2026, 8, 6), verificationState: .requiresReview, approvedForPatient: false),
        Report(id: "r-003", patientId: "p-002", studyId: "s-003", title: "CT chest report",
               summary: "AI-generated draft. Not yet reviewed.",
               createdAt: date(2026, 11, 4), verificationState: .aiGenerated, approvedForPatient: false)
    ]

    // MARK: Appointments

    let appointments: [Appointment] = [
        Appointment(id: "a-001", patientId: "p-001", date: date(2026, 12, 3, 10, 30),
                    department: "Hepatology", clinicianName: "Dr Sarah Smith"),
        Appointment(id: "a-002", patientId: "p-001", date: date(2026, 11, 12, 14, 0),
                    department: "Radiology", clinicianName: "Dr Sarah Smith"),
        Appointment(id: "a-003", patientId: "p-002", date: date(2026, 12, 8, 9, 0),
                    department: "Respiratory", clinicianName: "Dr Sarah Smith")
    ]
}

/// Builds a fixed date for mock data.
private func date(_ year: Int, _ month: Int, _ day: Int, _ hour: Int = 9, _ minute: Int = 0) -> Date {
    let components = DateComponents(year: year, month: month, day: day, hour: hour, minute: minute)
    return Calendar(identifier: .gregorian).date(from: components) ?? .distantPast
}

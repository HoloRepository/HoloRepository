//
//  PatientRecordView.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// One patient's clinical record: summary, scans, reports and MDT consultations.
struct PatientRecordView: View {
    let patientId: String
    let model: PatientsModel

    var body: some View {
        Group {
            if let patient = model.patient(id: patientId) {
                record(for: patient)
            } else if model.hasLoaded {
                // Not in the authorised list: never reveal anything about the record.
                ContentUnavailableView("Record not available", systemImage: "lock",
                                       description: Text("You aren't authorised to view this patient."))
            } else {
                ProgressView()
            }
        }
        .background(Theme.background)
        .navigationTitle("Patient record")
        .navigationBarTitleDisplayMode(.inline)
    }

    private func record(for patient: Patient) -> some View {
        List {
            clinicalSummary(for: patient)
            scanHistory(for: patient)
            reports(for: patient)
            consultations(for: patient)
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .safeAreaInset(edge: .top, spacing: 0) {
            PatientBanner(patient: patient)
        }
    }

    // MARK: Sections

    private func clinicalSummary(for patient: Patient) -> some View {
        Section("Clinical summary") {
            SummaryLines(title: "Key conditions", lines: patient.conditions, emptyText: "None recorded")
            SummaryLines(title: "Previous procedures", lines: patient.previousProcedures, emptyText: "None recorded")
        }
        .listRowBackground(Theme.card)
    }

    private func scanHistory(for patient: Patient) -> some View {
        let studies = model.studies(for: patient.id)
        return Section("Scan history") {
            if studies.isEmpty {
                EmptyRow(text: "No scans yet")
            }
            ForEach(studies) { study in
                NavigationLink(value: PatientsRoute.study(study)) {
                    VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                        Text(study.description)
                            .font(.body)
                        Text(study.date, format: .dateTime.day().month(.abbreviated).year())
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                    .frame(minHeight: 44)
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel("\(study.description), \(study.date.formatted(date: .long, time: .omitted))")
                }
                .accessibilityHint("Opens the 3D viewer")
            }
        }
        .listRowBackground(Theme.card)
    }

    private func reports(for patient: Patient) -> some View {
        let reports = model.reports(for: patient.id)
        return Section("Reports") {
            if reports.isEmpty {
                EmptyRow(text: "No reports yet")
            }
            ForEach(reports) { report in
                NavigationLink(value: PatientsRoute.report(report)) {
                    HStack(spacing: Theme.Spacing.s) {
                        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                            Text(report.title)
                                .font(.body)
                            Text(report.createdAt, format: .dateTime.day().month(.abbreviated).year())
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                        }
                        Spacer(minLength: Theme.Spacing.s)
                        StatusBadge(state: report.verificationState)
                    }
                    .frame(minHeight: 44)
                }
            }
        }
        .listRowBackground(Theme.card)
    }

    private func consultations(for patient: Patient) -> some View {
        let sessions = model.consultations(for: patient.id)
        return Section("Consultation history") {
            if sessions.isEmpty {
                EmptyRow(text: "No MDT consultations yet")
            }
            ForEach(sessions) { session in
                NavigationLink(value: PatientsRoute.consultation(session)) {
                    VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                        Text(session.caseName)
                            .font(.body)
                        Text("\(session.status.label) · \(session.startTime, format: .dateTime.day().month(.abbreviated).year().hour().minute())")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                    }
                    .frame(minHeight: 44)
                }
            }
        }
        .listRowBackground(Theme.card)
    }
}

// MARK: - Small pieces

/// A heading followed by short lines, read by VoiceOver as one element.
private struct SummaryLines: View {
    let title: String
    let lines: [String]
    let emptyText: String

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            Text(title)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(.secondary)
            if lines.isEmpty {
                Text(emptyText)
                    .font(.body)
                    .foregroundStyle(.secondary)
            }
            ForEach(lines, id: \.self) { line in
                Text(line)
                    .font(.body)
            }
        }
        .padding(.vertical, Theme.Spacing.xs)
        .accessibilityElement(children: .combine)
    }
}

/// Quiet text shown when a section has no items. Not an error.
private struct EmptyRow: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.subheadline)
            .foregroundStyle(.secondary)
            .frame(minHeight: 44)
    }
}

private extension MDTSessionStatus {
    var label: String {
        switch self {
        case .scheduled: "Upcoming"
        case .active: "In progress"
        case .completed: "Completed"
        }
    }
}

// MARK: - Previews

#Preview("Record") {
    NavigationStack {
        PatientRecordView(patientId: "p-001", model: .demo())
    }
    .tint(Theme.doctorAccent)
}

#Preview("Record, dark mode") {
    NavigationStack {
        PatientRecordView(patientId: "p-002", model: .demo())
    }
    .tint(Theme.doctorAccent)
    .preferredColorScheme(.dark)
}

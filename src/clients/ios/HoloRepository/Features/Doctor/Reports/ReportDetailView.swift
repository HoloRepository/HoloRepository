//
//  ReportDetailView.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// One report for clinician review: status, findings with their verification labels, impression,
/// recommendations and the patient-friendly version, with Approve and Edit until it's approved.
struct ReportDetailView: View {
    let reportId: String
    let records: PatientsModel
    /// Pushes the 3D viewer for the linked study in the owner's navigation stack.
    var onViewScan: ((String) -> Void)?

    @Environment(AppState.self) private var appState
    @State private var isConfirmingApproval = false
    @State private var isEditing = false
    @State private var saveError: String?

    var body: some View {
        Group {
            if let report = records.report(id: reportId), let patient = records.patient(id: report.patientId) {
                content(report: report, patient: patient)
            } else {
                // Not in the authorised list: never reveal anything about the report.
                ContentUnavailableView("Report not available", systemImage: "lock",
                                       description: Text("You aren't authorised to view this report."))
            }
        }
        .background(Theme.background)
        .navigationTitle("Report")
        .navigationBarTitleDisplayMode(.inline)
        .alert("Couldn't save", isPresented: Binding(get: { saveError != nil }, set: { if !$0 { saveError = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(saveError ?? "")
        }
    }

    private func content(report: Report, patient: Patient) -> some View {
        let study = records.study(id: report.studyId)

        return ScrollView {
            VStack(alignment: .leading, spacing: Theme.Spacing.m) {
                header(report: report, study: study)
                status(report: report)
                clinicianReport(report: report)
                patientVersion(report: report)
            }
            .padding(Theme.Spacing.m)
        }
        .safeAreaInset(edge: .top, spacing: 0) {
            PatientBanner(patient: patient)
        }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            if !report.approvedForPatient {
                reviewBar(report: report)
            }
        }
        .confirmationDialog("Approve this report?", isPresented: $isConfirmingApproval, titleVisibility: .visible) {
            Button("Approve") { approve(report) }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("The patient-friendly version will become visible to the patient.")
        }
        .sheet(isPresented: $isEditing) {
            ReportEditSheet(report: report) { findingTexts, patientSummary in
                do {
                    try records.editReport(id: report.id, findingTexts: findingTexts, patientSummary: patientSummary)
                } catch {
                    saveError = error.localizedDescription
                }
            }
        }
    }

    // MARK: Sections

    private func header(report: Report, study: Study?) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            Text(report.title)
                .font(.largeTitle.weight(.semibold))
                .accessibilityAddTraits(.isHeader)
            Text(scanLine(report: report, study: study))
                .font(.subheadline)
                .foregroundStyle(.secondary)

            if let study, let onViewScan {
                Button {
                    onViewScan(study.id)
                } label: {
                    Label("View scan", systemImage: "cube")
                        .frame(minHeight: 44)
                }
                .buttonStyle(.bordered)
                .accessibilityLabel("View scan, \(study.description)")
            }
        }
    }

    private func status(report: Report) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            StatusBadge(state: report.verificationState)
            if report.approvedForPatient, let approver = report.approvedBy {
                Text(approvalLine(approver: approver, date: report.approvedAt))
                    .font(.subheadline)
                Text("Visible to the patient")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            } else {
                Text("Not yet visible to the patient")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        }
        .cardStyle()
        .accessibilityElement(children: .combine)
    }

    private func clinicianReport(report: Report) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.m) {
            SectionTitle("Findings")
            ForEach(Array(report.findings.enumerated()), id: \.element.id) { index, finding in
                FindingRow(number: index + 1, finding: finding)
                if index < report.findings.count - 1 {
                    Divider()
                }
            }

            SectionTitle("Impression")
            Text(report.impression)
                .font(.body)

            SectionTitle("Recommendations")
            Text(report.recommendations)
                .font(.body)
        }
        .cardStyle()
    }

    private func patientVersion(report: Report) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            SectionTitle("Patient-friendly version")
            VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                Label("What the patient will see", systemImage: "person")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(.secondary)
                // Same style as the patient app: rounded body text.
                Text(report.patientSummary)
                    .font(.body)
                    .fontDesign(.rounded)
            }
            .cardStyle()
            .accessibilityElement(children: .combine)
        }
    }

    private func reviewBar(report: Report) -> some View {
        HStack(spacing: Theme.Spacing.s) {
            Button {
                isEditing = true
            } label: {
                Text("Edit")
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.bordered)
            .accessibilityLabel("Edit report")

            Button {
                isConfirmingApproval = true
            } label: {
                Text("Approve")
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.doctorAccent)
            .accessibilityLabel("Approve report")
        }
        .padding(.horizontal, Theme.Spacing.m)
        .padding(.vertical, Theme.Spacing.s)
        .background(.bar)
    }

    // MARK: Helpers

    private func approve(_ report: Report) {
        let approver = appState.currentUser?.name ?? "Unknown clinician"
        do {
            try records.approveReport(id: report.id, by: approver)
            AccessibilityNotification.Announcement("Report approved").post()
        } catch {
            saveError = error.localizedDescription
        }
    }

    private func scanLine(report: Report, study: Study?) -> String {
        let date = report.createdAt.formatted(.dateTime.day().month(.wide).year())
        return [study?.description, date].compactMap { $0 }.joined(separator: " · ")
    }

    private func approvalLine(approver: String, date: Date?) -> String {
        guard let date else { return "Approved by \(approver)" }
        return "Approved by \(approver), \(date.formatted(.dateTime.day().month(.abbreviated)))"
    }
}

// MARK: - Small pieces

private struct SectionTitle: View {
    let text: String

    init(_ text: String) {
        self.text = text
    }

    var body: some View {
        Text(text)
            .font(.headline)
            .accessibilityAddTraits(.isHeader)
    }
}

/// A numbered finding with its own verification label and, for verified references, its source.
private struct FindingRow: View {
    let number: Int
    let finding: Finding

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            HStack(alignment: .firstTextBaseline, spacing: Theme.Spacing.s) {
                Text("\(number).")
                    .font(.body)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                Text(finding.text)
                    .font(.body)
            }
            HStack(spacing: Theme.Spacing.s) {
                StatusBadge(state: finding.verificationState)
                if finding.verificationState == .verified, let source = finding.source {
                    Text("Source: \(source)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
    }

    private var accessibilityText: String {
        var text = "Finding \(number), \(finding.verificationState.displayName.lowercased()): \(finding.text)"
        if finding.verificationState == .verified, let source = finding.source {
            text += ". Source: \(source)"
        }
        return text
    }
}

// MARK: - Previews

#Preview("Needs review") {
    NavigationStack {
        ReportDetailView(reportId: "r-002", records: .demo(), onViewScan: { _ in })
    }
    .environment(AppState(currentUser: MockDataService().doctorUser))
    .tint(Theme.doctorAccent)
}

#Preview("Approved") {
    NavigationStack {
        ReportDetailView(reportId: "r-001", records: .demo(), onViewScan: { _ in })
    }
    .environment(AppState(currentUser: MockDataService().doctorUser))
    .tint(Theme.doctorAccent)
}

#Preview("Needs review, dark mode") {
    NavigationStack {
        ReportDetailView(reportId: "r-003", records: .demo(), onViewScan: { _ in })
    }
    .environment(AppState(currentUser: MockDataService().doctorUser))
    .tint(Theme.doctorAccent)
    .preferredColorScheme(.dark)
}

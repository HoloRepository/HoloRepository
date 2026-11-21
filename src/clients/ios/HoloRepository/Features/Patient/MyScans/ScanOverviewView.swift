//
//  ScanOverviewView.swift
//
//
//  Created by Ng Xin Ning on 21/11/2026.
//

import SwiftUI

/// One scan explained simply: when it was taken, what kind of scan, what it shows and who checked it.
/// Leads to the guided 3D view and the approved report.
struct ScanOverviewView: View {
    let studyId: String
    let model: PatientContentModel

    var body: some View {
        Group {
            if let scan = model.scan(studyId: studyId) {
                content(scan)
            } else {
                ContentUnavailableView("Scan not available", systemImage: "lock",
                                       description: Text("This scan hasn't been shared with you yet."))
            }
        }
        .background(Theme.background)
        .navigationTitle("Your scan")
        .navigationBarTitleDisplayMode(.inline)
    }

    private func content(_ scan: PatientScan) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Theme.Spacing.m) {
                VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                    Text(scan.study.patientName)
                        .font(.largeTitle.weight(.semibold))
                    if let description = scan.study.patientDescription {
                        Text(description)
                            .font(.body)
                    }
                }
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityElement(children: .combine)

                VStack(alignment: .leading, spacing: Theme.Spacing.m) {
                    detailRow("Date", value: scan.study.date.formatted(.dateTime.day().month(.wide).year()),
                              systemImage: "calendar")
                    detailRow("Type of scan", value: "\(scan.study.modality.displayName) scan",
                              systemImage: "waveform.path.ecg")
                    if let approver = scan.report.approvedBy {
                        detailRow("Checked by", value: approver, systemImage: "checkmark.seal")
                    }
                    StatusBadge(kind: .approved, text: "Approved for you")
                }
                .cardStyle()

                NavigationLink(value: PatientRoute.viewer(studyId: scan.study.id)) {
                    Label("Explore in 3D", systemImage: "rotate.3d")
                        .font(.headline)
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.borderedProminent)
                .buttonBorderShape(.roundedRectangle(radius: Theme.Radius.button))
                .accessibilityLabel("Explore your scan in 3D")

                NavigationLink(value: PatientRoute.report(reportId: scan.report.id)) {
                    HStack {
                        Label("Read your report", systemImage: "doc.text")
                            .font(.headline)
                        Spacer()
                        Image(systemName: "chevron.right")
                            .foregroundStyle(.secondary)
                            .accessibilityHidden(true)
                    }
                    .frame(minHeight: 44)
                    .cardStyle()
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Read your report")
                .accessibilityHint("Your doctor has checked this report.")
            }
            .padding(Theme.Spacing.m)
        }
    }

    private func detailRow(_ title: String, value: String, systemImage: String) -> some View {
        Label {
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(title)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Text(value)
                    .font(.body)
            }
            .fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: systemImage)
                .foregroundStyle(.tint)
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Previews

#Preview("Scan overview") {
    NavigationStack {
        ScanOverviewView(studyId: "s-001", model: .demo())
    }
    .patientStyle()
}

#Preview("Scan overview, dark") {
    NavigationStack {
        ScanOverviewView(studyId: "s-001", model: .demo())
    }
    .patientStyle()
    .preferredColorScheme(.dark)
}

#Preview("Scan overview, largest text") {
    NavigationStack {
        ScanOverviewView(studyId: "s-001", model: .demo())
    }
    .patientStyle()
    .dynamicTypeSize(.accessibility5)
}

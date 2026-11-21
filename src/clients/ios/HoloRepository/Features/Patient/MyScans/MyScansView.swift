//
//  MyScansView.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// The patient's scan library: only their own CT scans that have a report their doctor approved for them.
/// Every scan has a 3D model.
struct MyScansView: View {
    let model: PatientContentModel

    var body: some View {
        NavigationStack {
            Group {
                if model.scans.isEmpty {
                    ContentUnavailableView {
                        Label("No scans yet", systemImage: "cube")
                    } description: {
                        Text("When your doctor has checked a scan and shared it with you, it will show here.")
                    }
                } else {
                    ScrollView {
                        LazyVStack(spacing: Theme.Spacing.m) {
                            ForEach(model.scans) { scan in
                                NavigationLink(value: PatientRoute.scan(studyId: scan.study.id)) {
                                    ScanCard(scan: scan)
                                }
                                .buttonStyle(.plain)
                            }
                        }
                        .padding(Theme.Spacing.m)
                    }
                }
            }
            .background(Theme.background)
            .navigationTitle("My Scans")
            .patientDestinations(model: model)
        }
    }
}

/// One scan in the library, in plain words.
private struct ScanCard: View {
    let scan: PatientScan

    var body: some View {
        HStack(alignment: .center, spacing: Theme.Spacing.m) {
            VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                Text(scan.study.patientName)
                    .font(.headline)
                Text(scan.study.date, format: .dateTime.day().month(.wide).year())
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                if let description = scan.study.patientDescription {
                    Text(description)
                        .font(.body)
                }
            }
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)

            Image(systemName: "chevron.right")
                .foregroundStyle(.secondary)
                .accessibilityHidden(true)
        }
        .cardStyle()
        .contentShape(Rectangle())
        .accessibilityElement(children: .combine)
        .accessibilityHint("Opens this scan.")
    }
}

// MARK: - Previews

#Preview("Scan library") {
    MyScansView(model: .demo())
        .patientStyle()
}

#Preview("Scan library, empty") {
    MyScansView(model: .empty())
        .patientStyle()
}

#Preview("Scan library, dark") {
    MyScansView(model: .demo())
        .patientStyle()
        .preferredColorScheme(.dark)
}

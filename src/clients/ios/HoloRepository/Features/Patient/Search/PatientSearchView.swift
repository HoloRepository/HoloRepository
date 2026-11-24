//
//  PatientSearchView.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

/// Searches the patient's own approved reports, scans and appointments. Results are grouped by type
/// and open their existing detail screens. Opened from the search bar on Home.
// Later: messages and medical history can be added as more result sections (see PatientContentModel.search).
struct PatientSearchView: View {
    let model: PatientContentModel

    @State private var query: String
    @State private var isSearchFocused = false

    init(model: PatientContentModel, query: String = "") {
        self.model = model
        _query = State(initialValue: query)
    }

    var body: some View {
        let results = model.search(query)

        List {
            if results.isEmpty {
                ContentUnavailableView.search(text: query)
                    .listRowBackground(Color.clear)
            } else {
                if !results.reports.isEmpty {
                    Section("Reports") {
                        ForEach(results.reports) { report in
                            NavigationLink(value: PatientRoute.report(reportId: report.id)) {
                                ResultRow(systemImage: "doc.text",
                                          title: reportName(report),
                                          subtitle: report.approvedBy.map { "Checked by \($0)" })
                            }
                        }
                    }
                }
                if !results.scans.isEmpty {
                    Section("Scans") {
                        ForEach(results.scans) { scan in
                            NavigationLink(value: PatientRoute.scan(studyId: scan.study.id)) {
                                ResultRow(systemImage: "cube",
                                          title: scan.study.patientName,
                                          subtitle: scan.study.date.formatted(.dateTime.day().month(.wide).year()))
                            }
                        }
                    }
                }
                if !results.appointments.isEmpty {
                    Section("Appointments") {
                        ForEach(results.appointments) { appointment in
                            NavigationLink(value: PatientRoute.appointment(appointmentId: appointment.id)) {
                                ResultRow(systemImage: "calendar",
                                          title: "Consultation with \(appointment.clinicianName)",
                                          subtitle: appointment.date.formatted(
                                              .dateTime.weekday(.wide).day().month(.wide).hour().minute()))
                            }
                        }
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .scrollContentBackground(.hidden)
        .background(Theme.background)
        .navigationTitle("Search")
        .navigationBarTitleDisplayMode(.inline)
        .searchable(text: $query, isPresented: $isSearchFocused,
                    placement: .navigationBarDrawer(displayMode: .always),
                    prompt: "Search reports, scans, appointments")
        // Came here from the search bar, so start typing straight away.
        .onAppear { if query.isEmpty { isSearchFocused = true } }
    }

    private func reportName(_ report: Report) -> String {
        model.scan(studyId: report.studyId).map { "Report on your \($0.study.patientName.lowercased())" }
            ?? "Your scan report"
    }
}

/// Icon, title and one line of detail.
private struct ResultRow: View {
    let systemImage: String
    let title: String
    let subtitle: String?

    var body: some View {
        Label {
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(title)
                    .font(.body)
                if let subtitle {
                    Text(subtitle)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            }
            .fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: systemImage)
                .foregroundStyle(.tint)
        }
        .frame(minHeight: 44)
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Previews

#Preview("Search results") {
    NavigationStack {
        PatientSearchView(model: .demo(), query: "tummy")
            .patientDestinations(model: .demo())
    }
    .patientStyle()
}

#Preview("Search, everything") {
    NavigationStack {
        PatientSearchView(model: .demo())
    }
    .patientStyle()
}

#Preview("Search, no results") {
    NavigationStack {
        PatientSearchView(model: .demo(), query: "xyz")
    }
    .patientStyle()
}

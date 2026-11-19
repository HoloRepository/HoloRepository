//
//  PatientsListView.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Screens pushed inside the Patients tab. Records are pushed by id so Home can open one directly.
enum PatientsRoute: Hashable {
    case record(patientId: String)
    case study(Study)
    case report(Report)
    case consultation(MDTSession)
}

/// The doctor's patient list: search, assigned / all-authorised filter, and a row per patient.
struct PatientsListView: View {
    @Binding private var path: [PatientsRoute]
    /// Loaded by the owner before this tab first appears. If the rows arrive after the List's first layout,
    /// the navigation bar starts collapsed (small centred title) instead of showing the large title.
    private let model: PatientsModel
    @State private var searchText: String
    @State private var filter: PatientFilter = .assignedToMe

    init(path: Binding<[PatientsRoute]>, model: PatientsModel, searchText: String = "") {
        _path = path
        self.model = model
        _searchText = State(initialValue: searchText)
    }

    var body: some View {
        NavigationStack(path: $path) {
            let visiblePatients = model.patients(for: filter, matching: searchText)

            // The filter and empty state live inside the List (not in a safe-area inset or overlay)
            // so the List stays the navigation bar's scroll view and the large title and search bar show.
            List {
                Picker("Filter", selection: $filter) {
                    ForEach(PatientFilter.allCases) { filter in
                        Text(filter.title).tag(filter)
                    }
                }
                .pickerStyle(.segmented)
                .accessibilityLabel("Patient filter")
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets())

                if model.hasLoaded && visiblePatients.isEmpty {
                    emptyState
                        .listRowBackground(Color.clear)
                } else {
                    Section {
                        ForEach(visiblePatients) { patient in
                            NavigationLink(value: PatientsRoute.record(patientId: patient.id)) {
                                PatientRow(patient: patient, latestStudy: model.latestStudy(for: patient.id))
                            }
                            .listRowBackground(Theme.card)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .scrollContentBackground(.hidden)
            .background(Theme.background)
            .navigationTitle("Patients")
            .navigationBarTitleDisplayMode(.large)
            .searchable(text: $searchText,
                        placement: .navigationBarDrawer(displayMode: .always),
                        prompt: "Search name or hospital number")
            .navigationDestination(for: PatientsRoute.self) { route in
                destination(for: route)
            }
        }
    }

    @ViewBuilder
    private var emptyState: some View {
        if !searchText.trimmingCharacters(in: .whitespaces).isEmpty {
            ContentUnavailableView.search(text: searchText)
        } else {
            ContentUnavailableView {
                Label("No patients", systemImage: "person.2")
            } description: {
                Text(filter == .assignedToMe
                     ? "No patients are assigned to you. Try All authorised."
                     : "You aren't authorised to view any patients yet.")
            }
        }
    }

    @ViewBuilder
    private func destination(for route: PatientsRoute) -> some View {
        switch route {
        case .record(let patientId):
            PatientRecordView(patientId: patientId, model: model)
        case .study(let study):
            HomePlaceholderView(title: study.description, message: "3D viewer coming next")
        case .report(let report):
            HomePlaceholderView(title: report.title)
        case .consultation(let session):
            HomePlaceholderView(title: session.caseName)
        }
    }
}

// MARK: - Row

/// Avatar, name, age and sex with masked hospital number, and the latest scan.
private struct PatientRow: View {
    let patient: Patient
    let latestStudy: Study?

    var body: some View {
        HStack(spacing: Theme.Spacing.m) {
            InitialsAvatar(patient.initials)

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(patient.name)
                    .font(.headline)
                Text("\(patient.age()) · \(patient.sex.displayName) · \(patient.maskedHospitalNumber)")
                    .font(.subheadline)
                    .monospacedDigit()
                Group {
                    if let latestStudy {
                        Text("\(latestStudy.shortName) · \(latestStudy.date, format: .dateTime.day().month(.abbreviated).year())")
                    } else {
                        Text("No scans yet")
                    }
                }
                .font(.subheadline)
                .foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, Theme.Spacing.xs)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
    }

    private var accessibilityText: String {
        var parts = [patient.name, "\(patient.age())", patient.sex.displayName]
        parts.append("hospital number ending \(patient.hospitalNumber.suffix(4))")
        if let latestStudy {
            parts.append("last scan \(latestStudy.shortName) \(latestStudy.date.formatted(.dateTime.day().month(.wide)))")
        } else {
            parts.append("no scans yet")
        }
        return parts.joined(separator: ", ")
    }
}

extension Study {
    /// e.g. "CT abdomen", "X-ray left wrist".
    var shortName: String {
        "\(modality.displayName) \(bodyPart.lowercased())"
    }
}

// MARK: - Previews

#Preview("List") {
    @Previewable @State var path: [PatientsRoute] = []
    PatientsListView(path: $path, model: .demo())
        .tint(Theme.doctorAccent)
}

#Preview("Empty search") {
    @Previewable @State var path: [PatientsRoute] = []
    PatientsListView(path: $path, model: .demo(), searchText: "Zed")
        .tint(Theme.doctorAccent)
}

#Preview("List, dark mode") {
    @Previewable @State var path: [PatientsRoute] = []
    PatientsListView(path: $path, model: .demo())
        .tint(Theme.doctorAccent)
        .preferredColorScheme(.dark)
}

//
//  StudyListView.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Screens pushed inside the Imaging tab.
enum ImagingRoute: Hashable {
    case viewer(studyId: String)
}

/// The doctor's CT studies (authorised patients only), with search. Every study opens in the 3D viewer.
struct StudyListView: View {
    @Binding private var path: [ImagingRoute]
    /// Loaded by the owner before this tab first appears (see PatientsListView for why).
    private let model: PatientsModel
    @State private var searchText: String

    init(path: Binding<[ImagingRoute]>, model: PatientsModel, searchText: String = "") {
        _path = path
        self.model = model
        _searchText = State(initialValue: searchText)
    }

    var body: some View {
        NavigationStack(path: $path) {
            let studies = visibleStudies

            // The empty state lives inside the List so the large title and search bar attach to it.
            List {
                if studies.isEmpty {
                    emptyState
                        .listRowBackground(Color.clear)
                } else {
                    ForEach(studies) { study in
                        NavigationLink(value: ImagingRoute.viewer(studyId: study.id)) {
                            StudyRow(study: study, patientName: model.patient(id: study.patientId)?.name ?? "")
                        }
                        .listRowBackground(Theme.card)
                    }
                }
            }
            .listStyle(.insetGrouped)
            .scrollContentBackground(.hidden)
            .background(Theme.background)
            .navigationTitle("Scans")
            .navigationBarTitleDisplayMode(.large)
            .searchable(text: $searchText,
                        placement: .navigationBarDrawer(displayMode: .always),
                        prompt: "Search patient or exam")
            .navigationDestination(for: ImagingRoute.self) { route in
                switch route {
                case .viewer(let studyId):
                    StudyViewerView(studyId: studyId, records: model)
                }
            }
        }
    }

    /// After search (patient name or exam name).
    private var visibleStudies: [Study] {
        let query = searchText.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return model.allStudies }
        return model.allStudies.filter { study in
            let patientName = model.patient(id: study.patientId)?.name ?? ""
            return patientName.localizedCaseInsensitiveContains(query)
                || study.description.localizedCaseInsensitiveContains(query)
                || study.shortName.localizedCaseInsensitiveContains(query)
        }
    }

    @ViewBuilder
    private var emptyState: some View {
        if !searchText.trimmingCharacters(in: .whitespaces).isEmpty {
            ContentUnavailableView.search(text: searchText)
        } else {
            ContentUnavailableView {
                Label("No studies", systemImage: "photo.on.rectangle")
            } description: {
                Text("CT studies for your patients will appear here.")
            }
        }
    }
}

// MARK: - Row

/// Exam name, patient and date.
private struct StudyRow: View {
    let study: Study
    let patientName: String

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            Text(study.description)
                .font(.headline)
            Text(patientName)
                .font(.subheadline)
            Text(study.date, format: .dateTime.day().month(.abbreviated).year())
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .padding(.vertical, Theme.Spacing.xs)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(study.description), \(patientName), \(study.date.formatted(date: .long, time: .omitted))")
    }
}

// MARK: - Previews

#Preview("Study list") {
    @Previewable @State var path: [ImagingRoute] = []
    StudyListView(path: $path, model: .demo())
        .tint(Theme.doctorAccent)
}

#Preview("No studies") {
    @Previewable @State var path: [ImagingRoute] = []
    // No authorised patients, so there is nothing to show.
    let model = PatientsModel()
    let _ = model.load(from: MockDataService(), doctorId: nil)
    StudyListView(path: $path, model: model)
        .tint(Theme.doctorAccent)
}

#Preview("Study list, dark mode") {
    @Previewable @State var path: [ImagingRoute] = []
    StudyListView(path: $path, model: .demo())
        .tint(Theme.doctorAccent)
        .preferredColorScheme(.dark)
}

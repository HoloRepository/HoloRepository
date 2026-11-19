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

/// The modality filter on the study list.
enum StudyFilter: String, CaseIterable, Identifiable, Hashable {
    case all, ct, mri, xray

    var id: Self { self }

    var title: String {
        switch self {
        case .all: "All"
        case .ct: Modality.ct.displayName
        case .mri: Modality.mri.displayName
        case .xray: Modality.xray.displayName
        }
    }

    func includes(_ study: Study) -> Bool {
        switch self {
        case .all: true
        case .ct: study.modality == .ct
        case .mri: study.modality == .mri
        case .xray: study.modality == .xray
        }
    }
}

/// The doctor's imaging studies (authorised patients only): search, modality filter, and a row per study.
struct StudyListView: View {
    @Binding private var path: [ImagingRoute]
    /// Loaded by the owner before this tab first appears (see PatientsListView for why).
    private let model: PatientsModel
    @State private var searchText: String
    @State private var filter: StudyFilter

    init(path: Binding<[ImagingRoute]>, model: PatientsModel, filter: StudyFilter = .all, searchText: String = "") {
        _path = path
        self.model = model
        _filter = State(initialValue: filter)
        _searchText = State(initialValue: searchText)
    }

    var body: some View {
        NavigationStack(path: $path) {
            let studies = visibleStudies

            // Filter and empty state live inside the List so the large title and search bar attach to it.
            List {
                Picker("Modality", selection: $filter) {
                    ForEach(StudyFilter.allCases) { filter in
                        Text(filter.title).tag(filter)
                    }
                }
                .pickerStyle(.segmented)
                .accessibilityLabel("Modality filter")
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets())

                if studies.isEmpty {
                    emptyState
                        .listRowBackground(Color.clear)
                } else {
                    Section {
                        ForEach(studies) { study in
                            NavigationLink(value: ImagingRoute.viewer(studyId: study.id)) {
                                StudyRow(study: study,
                                         patientName: model.patient(id: study.patientId)?.name ?? "",
                                         has3DModel: model.studyModel(for: study.id) != nil)
                            }
                            .listRowBackground(Theme.card)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .scrollContentBackground(.hidden)
            .background(Theme.background)
            .navigationTitle("Imaging")
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

    /// After the modality filter and search (patient name or exam name).
    private var visibleStudies: [Study] {
        let query = searchText.trimmingCharacters(in: .whitespaces)
        return model.allStudies.filter { study in
            guard filter.includes(study) else { return false }
            guard !query.isEmpty else { return true }
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
                Label(filter == .all ? "No studies" : "No \(filter.title) studies", systemImage: "photo.on.rectangle")
            } description: {
                Text(filter == .all
                     ? "Studies for your patients will appear here."
                     : "None of your patients has a \(filter.title) study.")
            }
        }
    }
}

// MARK: - Row

/// Modality icon, exam name, patient, date, and "3D ready" if the study has a 3D model.
private struct StudyRow: View {
    let study: Study
    let patientName: String
    let has3DModel: Bool

    @ScaledMetric(relativeTo: .body) private var iconWidth: CGFloat = 28

    var body: some View {
        HStack(spacing: Theme.Spacing.m) {
            Image(systemName: study.modality.systemImage)
                .font(.title3)
                .foregroundStyle(Theme.doctorAccent)
                .frame(width: iconWidth)

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(study.description)
                    .font(.headline)
                Text(patientName)
                    .font(.subheadline)
                HStack(spacing: Theme.Spacing.s) {
                    Text("\(study.modality.displayName) · \(study.date, format: .dateTime.day().month(.abbreviated).year())")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                    if has3DModel {
                        Label("3D ready", systemImage: "cube")
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(.secondary)
                            .padding(.horizontal, Theme.Spacing.s)
                            .padding(.vertical, 2)
                            .background(Theme.cardBorder, in: Capsule())
                    }
                }
            }
            // Start the row separator at the exam name, not at the "3D ready" label's text.
            .alignmentGuide(.listRowSeparatorLeading) { $0[.leading] }
        }
        .padding(.vertical, Theme.Spacing.xs)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
    }

    private var accessibilityText: String {
        var parts = ["\(study.modality.displayName) scan", study.description, patientName,
                     study.date.formatted(date: .long, time: .omitted)]
        if has3DModel { parts.append("3D ready") }
        return parts.joined(separator: ", ")
    }
}

// MARK: - Previews

#Preview("Study list") {
    @Previewable @State var path: [ImagingRoute] = []
    StudyListView(path: $path, model: .demo())
        .tint(Theme.doctorAccent)
}

#Preview("Empty filter") {
    @Previewable @State var path: [ImagingRoute] = []
    // No authorised patients, so the CT filter has nothing to show.
    let model = PatientsModel()
    let _ = model.load(from: MockDataService(), doctorId: nil)
    StudyListView(path: $path, model: model, filter: .ct)
        .tint(Theme.doctorAccent)
}

#Preview("Study list, dark mode") {
    @Previewable @State var path: [ImagingRoute] = []
    StudyListView(path: $path, model: .demo())
        .tint(Theme.doctorAccent)
        .preferredColorScheme(.dark)
}

//
//  ReportsListView.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Screens pushed inside the Reports tab.
enum ReportsRoute: Hashable {
    case detail(reportId: String)
    case viewer(studyId: String)
}

/// The review filter on the report list.
enum ReportFilter: String, CaseIterable, Identifiable, Hashable {
    case needsReview, approved, all

    var id: Self { self }

    var title: String {
        switch self {
        case .needsReview: "Needs review"
        case .approved: "Approved"
        case .all: "All"
        }
    }

    /// "Needs review" means not yet approved for the patient.
    func includes(_ report: Report) -> Bool {
        switch self {
        case .needsReview: !report.approvedForPatient
        case .approved: report.approvedForPatient
        case .all: true
        }
    }
}

/// The doctor's reports (authorised patients only): search, review filter, and a row per report.
struct ReportsListView: View {
    @Binding private var path: [ReportsRoute]
    /// Loaded by the owner before this tab first appears (see PatientsListView for why).
    private let model: PatientsModel
    @State private var searchText: String
    @State private var filter: ReportFilter

    init(path: Binding<[ReportsRoute]>, model: PatientsModel,
         filter: ReportFilter = .needsReview, searchText: String = "") {
        _path = path
        self.model = model
        _filter = State(initialValue: filter)
        _searchText = State(initialValue: searchText)
    }

    var body: some View {
        NavigationStack(path: $path) {
            let reports = visibleReports

            // Filter and empty state live inside the List so the large title and search bar attach to it.
            List {
                Picker("Filter", selection: $filter) {
                    ForEach(ReportFilter.allCases) { filter in
                        Text(filter.title).tag(filter)
                    }
                }
                .pickerStyle(.segmented)
                .accessibilityLabel("Report filter")
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets())

                if reports.isEmpty {
                    emptyState
                        .listRowBackground(Color.clear)
                } else {
                    Section {
                        ForEach(reports) { report in
                            NavigationLink(value: ReportsRoute.detail(reportId: report.id)) {
                                ReportRow(report: report,
                                          patientName: model.patient(id: report.patientId)?.name ?? "",
                                          scanName: model.study(id: report.studyId)?.shortName)
                            }
                            .listRowBackground(Theme.card)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .scrollContentBackground(.hidden)
            .background(Theme.background)
            .navigationTitle("Reports")
            .navigationBarTitleDisplayMode(.large)
            .searchable(text: $searchText,
                        placement: .navigationBarDrawer(displayMode: .always),
                        prompt: "Search patient or report")
            .navigationDestination(for: ReportsRoute.self) { route in
                switch route {
                case .detail(let reportId):
                    ReportDetailView(reportId: reportId, records: model) { studyId in
                        path.append(.viewer(studyId: studyId))
                    }
                case .viewer(let studyId):
                    StudyViewerView(studyId: studyId, records: model)
                }
            }
        }
    }

    /// After the review filter and search (patient name or report title).
    private var visibleReports: [Report] {
        let query = searchText.trimmingCharacters(in: .whitespaces)
        return model.allReports.filter { report in
            guard filter.includes(report) else { return false }
            guard !query.isEmpty else { return true }
            let patientName = model.patient(id: report.patientId)?.name ?? ""
            return patientName.localizedCaseInsensitiveContains(query)
                || report.title.localizedCaseInsensitiveContains(query)
        }
    }

    @ViewBuilder
    private var emptyState: some View {
        if !searchText.trimmingCharacters(in: .whitespaces).isEmpty {
            ContentUnavailableView.search(text: searchText)
        } else {
            switch filter {
            case .needsReview:
                ContentUnavailableView("Nothing to review", systemImage: "checkmark.circle",
                                       description: Text("Reports waiting for your review will appear here."))
            case .approved:
                ContentUnavailableView("No approved reports", systemImage: "doc.text",
                                       description: Text("Reports you approve will appear here."))
            case .all:
                ContentUnavailableView("No reports", systemImage: "doc.text",
                                       description: Text("Reports for your patients will appear here."))
            }
        }
    }
}

// MARK: - Row

/// Title, patient, scan and date, and the verification state badge.
private struct ReportRow: View {
    let report: Report
    let patientName: String
    let scanName: String?

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            Text(report.title)
                .font(.headline)
            Text(patientName)
                .font(.subheadline)
            Text(detailLine)
                .font(.subheadline)
                .foregroundStyle(.secondary)
            StatusBadge(state: report.verificationState)
                .padding(.top, Theme.Spacing.xs)
        }
        .padding(.vertical, Theme.Spacing.xs)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
    }

    private var detailLine: String {
        let date = report.createdAt.formatted(.dateTime.day().month(.abbreviated).year())
        return [scanName, date].compactMap { $0 }.joined(separator: " · ")
    }

    private var accessibilityText: String {
        var parts = [report.title, patientName]
        if let scanName { parts.append(scanName) }
        parts.append(report.createdAt.formatted(date: .long, time: .omitted))
        parts.append(report.verificationState.displayName)
        if report.approvedForPatient { parts.append("approved") }
        return parts.joined(separator: ", ")
    }
}

// MARK: - Previews

#Preview("Report list") {
    @Previewable @State var path: [ReportsRoute] = []
    ReportsListView(path: $path, model: .demo())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

#Preview("Empty filter") {
    @Previewable @State var path: [ReportsRoute] = []
    // No authorised patients, so "Needs review" has nothing to show.
    let model = PatientsModel()
    let _ = model.load(from: MockDataService(), doctorId: nil)
    ReportsListView(path: $path, model: model)
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

#Preview("Report list, dark mode") {
    @Previewable @State var path: [ReportsRoute] = []
    ReportsListView(path: $path, model: .demo(), filter: .all)
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
        .preferredColorScheme(.dark)
}

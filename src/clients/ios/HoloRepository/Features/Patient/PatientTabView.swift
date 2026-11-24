//
//  PatientTabView.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// The patient tabs, in order. Appointments is reached from Home (Quick access, the appointment card, search).
enum PatientTab: Hashable {
    case home, reports, scans, messages, profile
}

/// Bottom tabs for the patient app. Teal tint, rounded text.
struct PatientTabView: View {
    @Environment(AppState.self) private var appState
    @State private var selection: PatientTab
    /// The patient's own approved content, shared by every tab.
    @State private var contentModel = PatientContentModel()

    init(selection: PatientTab = .home) {
        _selection = State(initialValue: selection)
    }

    var body: some View {
        TabView(selection: $selection) {
            PatientHomeView(model: contentModel, onSelectTab: { selection = $0 })
                .tabItem { Label("Home", systemImage: "house") }
                .tag(PatientTab.home)

            MyReportsView()
                .tabItem { Label("Reports", systemImage: "doc.text") }
                .tag(PatientTab.reports)

            MyScansView(model: contentModel)
                .tabItem { Label("Scans", systemImage: "cube") }
                .tag(PatientTab.scans)

            NavigationStack {
                ComingSoonView(feature: .messages)
            }
            .tabItem { Label("Messages", systemImage: "message") }
            .tag(PatientTab.messages)

            PatientProfileView()
                .tabItem { Label("Profile", systemImage: "person.crop.circle") }
                .tag(PatientTab.profile)
        }
        .patientStyle()
        .onAppear {
            if !contentModel.hasLoaded {
                contentModel.load(from: appState.dataService, reportStore: appState.reportStore,
                                  user: appState.currentUser)
            }
        }
    }
}

#Preview {
    PatientTabView()
        .environment(AppState(currentUser: MockDataService().patientUser))
}

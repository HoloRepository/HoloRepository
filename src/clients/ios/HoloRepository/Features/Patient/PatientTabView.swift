//
//  PatientTabView.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Bottom tabs for the patient app. Teal tint, rounded text, one text size larger.
struct PatientTabView: View {
    @Environment(AppState.self) private var appState
    /// The patient's own approved content, shared by Home and My Scans.
    @State private var contentModel = PatientContentModel()

    var body: some View {
        TabView {
            PatientHomeView(model: contentModel)
                .tabItem { Label("Home", systemImage: "house") }

            MyScansView(model: contentModel)
                .tabItem { Label("My Scans", systemImage: "cube") }

            MyReportsView()
                .tabItem { Label("My Reports", systemImage: "doc.text") }

            AppointmentsView()
                .tabItem { Label("Appointments", systemImage: "calendar") }
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

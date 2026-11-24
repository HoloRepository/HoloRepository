//
//  DoctorTabView.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// The doctor tabs, in order.
enum DoctorTab: Hashable {
    case home, patients, scans, meetings, reports
}

/// Bottom tabs for the doctor / clinician app.
struct DoctorTabView: View {
    @Environment(AppState.self) private var appState
    @State private var selection: DoctorTab = .home
    @State private var patientsPath: [PatientsRoute] = []
    @State private var imagingPath: [ImagingRoute] = []
    @State private var meetingsPath: [DoctorRoute] = []
    @State private var reportsPath: [ReportsRoute] = []
    /// Authorised clinical data, shared by every tab (records, studies, reports).
    @State private var patientsModel = PatientsModel()
    /// Tasks, MDT sessions and notifications, shared by Home and Video Meeting.
    @State private var homeModel = DoctorHomeModel()

    var body: some View {
        TabView(selection: $selection) {
            DoctorHomeView(model: homeModel, records: patientsModel, onSelectTab: { selection = $0 })
                .tabItem { Label("Home", systemImage: "house") }
                .tag(DoctorTab.home)

            PatientsListView(path: $patientsPath, model: patientsModel)
                .tabItem { Label("Patients", systemImage: "person.2") }
                .tag(DoctorTab.patients)

            // The Imaging tab, shown as "Scans".
            StudyListView(path: $imagingPath, model: patientsModel)
                .tabItem { Label("Scans", systemImage: "cube") }
                .tag(DoctorTab.scans)

            MeetingsView(path: $meetingsPath, model: homeModel, records: patientsModel)
                .tabItem { Label("Video Meeting", systemImage: "video") }
                .tag(DoctorTab.meetings)

            ReportsListView(path: $reportsPath, model: patientsModel)
                .tabItem { Label("Reports", systemImage: "doc.text") }
                .tag(DoctorTab.reports)
        }
        .tint(Theme.doctorAccent)
        // Load while Home is showing, so the other tabs have their data before they first appear.
        .onAppear {
            let doctorId = appState.currentUser?.id
            if !patientsModel.hasLoaded {
                patientsModel.load(from: appState.dataService, reportStore: appState.reportStore,
                                   doctorId: doctorId)
            }
            if !homeModel.hasLoaded {
                homeModel.load(from: appState.dataService, doctorId: doctorId)
            }
        }
    }
}

#Preview {
    DoctorTabView()
        .environment(AppState(currentUser: MockDataService().doctorUser))
}

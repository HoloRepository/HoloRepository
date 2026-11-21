//
//  DoctorTabView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Bottom tabs for the doctor / clinician app.
struct DoctorTabView: View {
    enum Tab: Hashable { case home, patients, imaging, reports }

    @Environment(AppState.self) private var appState
    @State private var selection: Tab = .home
    @State private var patientsPath: [PatientsRoute] = []
    @State private var imagingPath: [ImagingRoute] = []
    @State private var reportsPath: [ReportsRoute] = []
    /// Authorised clinical data, shared by the Patients, Imaging and Reports tabs.
    @State private var patientsModel = PatientsModel()

    var body: some View {
        TabView(selection: $selection) {
            // Tapping a patient name on Home opens that patient's record in the Patients tab.
            DoctorHomeView(onOpenPatient: { patientId in
                patientsPath = [.record(patientId: patientId)]
                selection = .patients
            })
                .tabItem { Label("Home", systemImage: "house") }
                .tag(Tab.home)

            PatientsListView(path: $patientsPath, model: patientsModel)
                .tabItem { Label("Patients", systemImage: "person.2") }
                .tag(Tab.patients)

            StudyListView(path: $imagingPath, model: patientsModel)
                .tabItem { Label("Imaging", systemImage: "cube") }
                .tag(Tab.imaging)

            ReportsListView(path: $reportsPath, model: patientsModel)
                .tabItem { Label("Reports", systemImage: "doc.text") }
                .tag(Tab.reports)
        }
        .tint(Theme.doctorAccent)
        // Load while Home is showing, so the other tabs have their data before they first appear.
        .onAppear {
            if !patientsModel.hasLoaded {
                patientsModel.load(from: appState.dataService, reportStore: appState.reportStore,
                                   doctorId: appState.currentUser?.id)
            }
        }
    }
}

#Preview {
    DoctorTabView()
        .environment(AppState(currentUser: MockDataService().doctorUser))
}

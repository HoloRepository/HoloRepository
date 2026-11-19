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

    @State private var selection: Tab = .home
    @State private var patientsPath: [PatientsRoute] = []

    var body: some View {
        TabView(selection: $selection) {
            // Tapping a patient name on Home opens that patient's record in the Patients tab.
            DoctorHomeView(onOpenPatient: { patientId in
                patientsPath = [.record(patientId: patientId)]
                selection = .patients
            })
                .tabItem { Label("Home", systemImage: "house") }
                .tag(Tab.home)

            PatientsListView(path: $patientsPath)
                .tabItem { Label("Patients", systemImage: "person.2") }
                .tag(Tab.patients)

            ImagingView()
                .tabItem { Label("Imaging", systemImage: "cube") }
                .tag(Tab.imaging)

            ReportsView()
                .tabItem { Label("Reports", systemImage: "doc.text") }
                .tag(Tab.reports)
        }
        .tint(Theme.doctorAccent)
    }
}

#Preview {
    DoctorTabView()
        .environment(AppState(currentUser: MockDataService().doctorUser))
}

//
//  DoctorTabView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Bottom tabs for the doctor / clinician app.
struct DoctorTabView: View {
    var body: some View {
        TabView {
            DoctorHomeView()
                .tabItem { Label("Home", systemImage: "house") }

            PatientsView()
                .tabItem { Label("Patients", systemImage: "person.2") }

            ImagingView()
                .tabItem { Label("Imaging", systemImage: "cube") }

            ReportsView()
                .tabItem { Label("Reports", systemImage: "doc.text") }
        }
        .tint(Theme.doctorAccent)
    }
}

#Preview {
    DoctorTabView()
        .environment(AppState(currentUser: MockDataService().doctorUser))
}

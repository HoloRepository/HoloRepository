//
//  PatientTabView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Bottom tabs for the patient app. Rounded text throughout.
struct PatientTabView: View {
    var body: some View {
        TabView {
            PatientHomeView()
                .tabItem { Label("Home", systemImage: "house") }

            MyScansView()
                .tabItem { Label("My Scans", systemImage: "cube") }

            MyReportsView()
                .tabItem { Label("My Reports", systemImage: "doc.text") }

            AppointmentsView()
                .tabItem { Label("Appointments", systemImage: "calendar") }
        }
        .tint(Theme.patientAccent)
        .fontDesign(.rounded)
    }
}

#Preview {
    PatientTabView()
        .environment(AppState(currentUser: MockDataService().patientUser))
}

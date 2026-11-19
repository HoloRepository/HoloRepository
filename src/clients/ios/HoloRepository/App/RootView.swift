//
//  RootView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Shows sign-in when signed out, otherwise the app for the user's role.
struct RootView: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        switch appState.currentUser?.role {
        case nil:
            RoleChoiceView()
        case .doctor:
            DoctorTabView()
        case .patient:
            PatientTabView()
        }
    }
}

#Preview("Signed out") {
    RootView()
        .environment(AppState())
}

#Preview("Doctor") {
    RootView()
        .environment(AppState(currentUser: MockDataService().doctorUser))
}

#Preview("Patient") {
    RootView()
        .environment(AppState(currentUser: MockDataService().patientUser))
}

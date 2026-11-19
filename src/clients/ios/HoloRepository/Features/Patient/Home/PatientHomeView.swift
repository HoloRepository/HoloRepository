//
//  PatientHomeView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder patient Home. Next appointment and recent activity come later.
struct PatientHomeView: View {
    @Environment(AppState.self) private var appState
    @State private var isShowingProfile = false

    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("Hello, \(appState.currentUser?.name ?? "there")", systemImage: "house")
            } description: {
                Text("Your next appointment and latest updates will show here.")
            }
            .navigationTitle("Home")
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        isShowingProfile = true
                    } label: {
                        Image(systemName: "person.circle")
                            .frame(minWidth: 44, minHeight: 44)
                    }
                    .accessibilityLabel("Profile")
                }
            }
            .sheet(isPresented: $isShowingProfile) {
                PatientProfileView()
            }
        }
    }
}

#Preview {
    PatientHomeView()
        .environment(AppState(currentUser: MockDataService().patientUser))
        .tint(Theme.patientAccent)
        .fontDesign(.rounded)
}

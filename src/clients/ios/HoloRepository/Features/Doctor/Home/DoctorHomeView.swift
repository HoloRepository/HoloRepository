//
//  DoctorHomeView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder doctor Home. Summary cards come in a later task.
struct DoctorHomeView: View {
    @Environment(AppState.self) private var appState
    @State private var isShowingProfile = false

    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("Welcome, \(appState.currentUser?.name ?? "Doctor")", systemImage: "house")
            } description: {
                Text("Pending tasks, MDT sessions and recent activity will appear here.")
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
                DoctorProfileView()
            }
        }
    }
}

#Preview {
    DoctorHomeView()
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

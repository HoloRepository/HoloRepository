//
//  DoctorProfileView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Simple doctor profile with sign out. Full settings come in March.
struct DoctorProfileView: View {
    @Environment(AppState.self) private var appState
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                Section {
                    LabeledContent("Name", value: appState.currentUser?.name ?? "")
                    LabeledContent("Role", value: "Doctor / Clinician")
                }

                Section {
                    Button("Sign out", role: .destructive) {
                        appState.signOut()
                    }
                    .frame(minHeight: 44)
                }
            }
            .listStyle(.insetGrouped)
            .navigationTitle("Profile")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
        }
        .tint(Theme.doctorAccent)
    }
}

#Preview {
    DoctorProfileView()
        .environment(AppState(currentUser: MockDataService().doctorUser))
}

//
//  PatientProfileView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Simple patient profile with sign out. Full settings come in March.
struct PatientProfileView: View {
    @Environment(AppState.self) private var appState
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                Section {
                    LabeledContent("Name", value: appState.currentUser?.name ?? "")
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
        .tint(Theme.patientAccent)
        .fontDesign(.rounded)
    }
}

#Preview {
    PatientProfileView()
        .environment(AppState(currentUser: MockDataService().patientUser))
}

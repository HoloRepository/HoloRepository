//
//  RoleChoiceView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// First screen: choose whether to sign in as a doctor or a patient.
struct RoleChoiceView: View {
    var body: some View {
        NavigationStack {
            List {
                Section {
                    NavigationLink {
                        SignInView(role: .doctor)
                    } label: {
                        Label("Doctor / Clinician", systemImage: "stethoscope")
                            .frame(minHeight: 44)
                    }
                    .accessibilityHint("Sign in with your hospital email and password")

                    NavigationLink {
                        SignInView(role: .patient)
                    } label: {
                        Label("Patient", systemImage: "person")
                            .frame(minHeight: 44)
                    }
                    .accessibilityHint("Sign in to see your scans, reports and appointments")
                } header: {
                    Text("Sign in as")
                } footer: {
                    Text("Prototype – demo data only")
                }
            }
            .listStyle(.insetGrouped)
            .navigationTitle("HoloRepository")
        }
        .tint(Theme.doctorAccent)
    }
}

#Preview {
    RoleChoiceView()
        .environment(AppState())
}

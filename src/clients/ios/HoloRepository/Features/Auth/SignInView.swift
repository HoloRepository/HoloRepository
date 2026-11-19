//
//  SignInView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Mock sign-in. Any input signs in as the demo user for the chosen role.
struct SignInView: View {
    let role: UserRole

    @Environment(AppState.self) private var appState
    @State private var identifier = ""
    @State private var password = ""

    private var isPatient: Bool { role == .patient }
    private var accent: Color { isPatient ? Theme.patientAccent : Theme.doctorAccent }

    var body: some View {
        Form {
            Section {
                TextField(isPatient ? "Email or patient ID" : "Hospital email", text: $identifier)
                    .textContentType(isPatient ? .username : .emailAddress)
                    .keyboardType(isPatient ? .default : .emailAddress)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                SecureField("Password", text: $password)
                    .textContentType(.password)
            } footer: {
                Label("Prototype – demo data only", systemImage: "info.circle")
                    .font(.caption)
            }

            Section {
                Button {
                    appState.signIn(as: role)
                } label: {
                    Text("Sign in")
                        .font(.headline)
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.borderedProminent)
                .buttonBorderShape(.roundedRectangle(radius: Theme.Radius.button))
                .listRowInsets(EdgeInsets())
                .listRowBackground(Color.clear)
                .accessibilityHint(isPatient ? "Signs in as the demo patient" : "Signs in as the demo doctor")
            }

            if isPatient {
                Section {
                    NavigationLink("Activate your account") {
                        InvitationView()
                    }
                } footer: {
                    Text("New here? Your hospital sends you an invitation to set up your account.")
                }
            }
        }
        .navigationTitle(isPatient ? "Patient sign in" : "Clinician sign in")
        .navigationBarTitleDisplayMode(.inline)
        .tint(accent)
        .fontDesign(isPatient ? .rounded : .default)
    }
}

#Preview("Doctor") {
    NavigationStack {
        SignInView(role: .doctor)
    }
    .environment(AppState())
}

#Preview("Patient") {
    NavigationStack {
        SignInView(role: .patient)
    }
    .environment(AppState())
}

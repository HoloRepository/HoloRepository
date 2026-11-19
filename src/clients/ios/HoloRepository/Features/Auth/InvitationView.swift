//
//  InvitationView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder: explains the hospital invitation step before activation.
/// Real SMS / email invitations come in March.
struct InvitationView: View {
    var body: some View {
        VStack(spacing: Theme.Spacing.l) {
            ContentUnavailableView {
                Label("Your invitation", systemImage: "envelope.open")
            } description: {
                Text("Your hospital creates your account and sends you an invitation by text or email. Use it to activate your account.")
            }

            NavigationLink {
                ActivationView()
            } label: {
                Text("I have an invitation")
                    .font(.headline)
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.borderedProminent)
            .buttonBorderShape(.roundedRectangle(radius: Theme.Radius.button))
            .padding(.horizontal, Theme.Spacing.m)
            .padding(.bottom, Theme.Spacing.l)
        }
        .navigationTitle("Invitation")
        .navigationBarTitleDisplayMode(.inline)
        .tint(Theme.patientAccent)
        .fontDesign(.rounded)
    }
}

#Preview {
    NavigationStack {
        InvitationView()
    }
}

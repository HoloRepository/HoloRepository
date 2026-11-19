//
//  ActivationView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder for account activation. Real activation comes in March.
struct ActivationView: View {
    var body: some View {
        ContentUnavailableView {
            Label("Activate your account", systemImage: "person.badge.key")
        } description: {
            Text("Here you'll enter your invitation code and create a password. This isn't available in the prototype yet.")
        }
        .navigationTitle("Activation")
        .navigationBarTitleDisplayMode(.inline)
        .tint(Theme.patientAccent)
        .fontDesign(.rounded)
    }
}

#Preview {
    NavigationStack {
        ActivationView()
    }
}

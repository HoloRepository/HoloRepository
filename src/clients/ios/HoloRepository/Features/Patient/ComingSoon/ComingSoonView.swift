//
//  ComingSoonView.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

/// Patient features that are planned but not built yet. They open `ComingSoonView`.
enum ComingSoonFeature: String, Hashable, CaseIterable, Identifiable {
    case messages
    case familyAccess
    case aiAssistant
    case notifications

    var id: Self { self }

    var title: String {
        switch self {
        case .messages: "Messages"
        case .familyAccess: "Family Access"
        case .aiAssistant: "AI Assistant"
        case .notifications: "Notifications"
        }
    }

    var systemImage: String {
        switch self {
        case .messages: "message"
        case .familyAccess: "person.2"
        case .aiAssistant: "sparkles"
        case .notifications: "bell"
        }
    }

    /// One plain line on what the feature will do.
    var plainDescription: String {
        switch self {
        case .messages: "Send messages to your care team and read their replies."
        case .familyAccess: "Choose family members or carers who can see your scans and reports."
        case .aiAssistant: "Ask questions about your results in everyday words."
        case .notifications: "Get told when a new scan, report or appointment is ready for you."
        }
    }
}

/// One shared placeholder for patient features that aren't built yet.
struct ComingSoonView: View {
    let feature: ComingSoonFeature

    var body: some View {
        ContentUnavailableView {
            Label(feature.title, systemImage: feature.systemImage)
        } description: {
            VStack(spacing: Theme.Spacing.s) {
                Text("Coming soon")
                    .font(.headline)
                Text(feature.plainDescription)
            }
        }
        .background(Theme.background)
        .navigationTitle(feature.title)
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview {
    NavigationStack {
        ComingSoonView(feature: .familyAccess)
    }
    .patientStyle()
}

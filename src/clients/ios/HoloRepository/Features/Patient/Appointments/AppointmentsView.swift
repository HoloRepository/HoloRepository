//
//  AppointmentsView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder for upcoming and previous appointments.
struct AppointmentsView: View {
    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("Appointments", systemImage: "calendar")
            } description: {
                Text("Your upcoming and past appointments will show here.")
            }
            .navigationTitle("Appointments")
        }
    }
}

#Preview {
    AppointmentsView()
        .tint(Theme.patientAccent)
        .fontDesign(.rounded)
}

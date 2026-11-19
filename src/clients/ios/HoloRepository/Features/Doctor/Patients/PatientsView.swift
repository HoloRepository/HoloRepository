//
//  PatientsView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder for the doctor's patient list.
struct PatientsView: View {
    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("Patients", systemImage: "person.2")
            } description: {
                Text("Search and browse your assigned patients here.")
            }
            .navigationTitle("Patients")
        }
    }
}

#Preview {
    PatientsView()
        .tint(Theme.doctorAccent)
}

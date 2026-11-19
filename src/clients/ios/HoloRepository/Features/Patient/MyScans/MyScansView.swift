//
//  MyScansView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder for the patient's scans library.
struct MyScansView: View {
    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("My Scans", systemImage: "cube")
            } description: {
                Text("Scans your care team has shared with you will show here.")
            }
            .navigationTitle("My Scans")
        }
    }
}

#Preview {
    MyScansView()
        .tint(Theme.patientAccent)
        .fontDesign(.rounded)
}

//
//  MyReportsView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder for the patient's approved reports.
struct MyReportsView: View {
    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("My Reports", systemImage: "doc.text")
            } description: {
                Text("Reports your doctor has approved for you will show here, explained in plain language.")
            }
            .navigationTitle("My Reports")
        }
    }
}

#Preview {
    MyReportsView()
        .tint(Theme.patientAccent)
        .fontDesign(.rounded)
}

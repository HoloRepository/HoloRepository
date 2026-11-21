//
//  PatientReportPlaceholderView.swift
//
//
//  Created by Ng Xin Ning on 21/11/2026.
//

import SwiftUI

/// Placeholder for reading an approved report. The full plain-language report screen is built with My Reports.
struct PatientReportPlaceholderView: View {
    /// Nil when the report isn't approved for the patient.
    let report: Report?

    var body: some View {
        Group {
            if report != nil {
                ContentUnavailableView {
                    Label("Your report", systemImage: "doc.text")
                } description: {
                    Text("Your doctor has checked this report. You'll be able to read it here soon.")
                }
            } else {
                ContentUnavailableView("Report not available", systemImage: "lock",
                                       description: Text("This report hasn't been shared with you yet."))
            }
        }
        .navigationTitle("Your report")
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview {
    NavigationStack {
        PatientReportPlaceholderView(report: PatientContentModel.demo().latestReport)
    }
    .patientStyle()
}

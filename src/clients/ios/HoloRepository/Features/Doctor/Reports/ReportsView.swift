//
//  ReportsView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder for AI drafts, clinical reports and approvals.
struct ReportsView: View {
    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("Reports", systemImage: "doc.text")
            } description: {
                Text("MDT summaries, clinical reports and approvals will appear here.")
            }
            .navigationTitle("Reports")
        }
    }
}

#Preview {
    ReportsView()
        .tint(Theme.doctorAccent)
}

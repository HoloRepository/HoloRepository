//
//  HomePlaceholderView.swift
//  
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Temporary detail screen pushed from Home and Patients until the real screens are built.
struct HomePlaceholderView: View {
    let title: String
    var message: String = "Coming next"

    var body: some View {
        ContentUnavailableView {
            Label(title, systemImage: "hammer")
        } description: {
            Text(message)
        }
        .background(Theme.background)
        .navigationTitle(title)
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview {
    NavigationStack {
        HomePlaceholderView(title: "Pending tasks")
    }
}

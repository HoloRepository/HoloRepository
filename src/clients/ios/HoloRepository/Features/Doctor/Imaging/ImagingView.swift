//
//  ImagingView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Placeholder for the study list and 3D viewer.
struct ImagingView: View {
    var body: some View {
        NavigationStack {
            ContentUnavailableView {
                Label("Imaging", systemImage: "cube")
            } description: {
                Text("CT, MRI and X-ray studies and the 3D viewer will appear here.")
            }
            .navigationTitle("Imaging")
        }
    }
}

#Preview {
    ImagingView()
        .tint(Theme.doctorAccent)
}

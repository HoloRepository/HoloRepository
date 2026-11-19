//
//  InitialsAvatar.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Round avatar showing a patient's initials, tinted with the doctor accent. Decorative for VoiceOver.
struct InitialsAvatar: View {
    let initials: String
    /// Grows with Dynamic Type.
    @ScaledMetric(relativeTo: .headline) private var size: CGFloat = 44

    init(_ initials: String) {
        self.initials = initials
    }

    var body: some View {
        Text(initials)
            .font(.headline)
            .foregroundStyle(Theme.doctorAccent)
            .frame(width: size, height: size)
            .background(Theme.doctorAccent.opacity(0.15), in: Circle())
            .accessibilityHidden(true)
    }
}

#Preview {
    InitialsAvatar("AP")
        .padding()
}

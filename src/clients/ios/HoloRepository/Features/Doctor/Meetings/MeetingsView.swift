//
//  MeetingsView.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

/// The Video Meeting tab: the active MDT session and upcoming ones, with the same rows as Home.
/// Each opens the session detail. Real video calls (Webex) come in March.
struct MeetingsView: View {
    @Binding var path: [DoctorRoute]
    let model: DoctorHomeModel
    let records: PatientsModel

    var body: some View {
        NavigationStack(path: $path) {
            TimelineView(.everyMinute) { context in
                let active = model.activeSession(at: context.date)
                let upcoming = model.upcomingSessions(at: context.date)

                ScrollView {
                    VStack(alignment: .leading, spacing: Theme.Spacing.l) {
                        section("Active", sessions: active.map { [$0] } ?? [], now: context.date,
                                empty: "No MDT session running or starting soon.")
                        section("Upcoming", sessions: upcoming, now: context.date,
                                empty: "No upcoming MDT sessions.")
                    }
                    .padding(Theme.Spacing.m)
                }
            }
            .background(Theme.background)
            .navigationTitle("Video Meeting")
            .doctorDestinations(home: model, records: records) { path.append($0) }
        }
    }

    private func section(_ title: String, sessions: [MDTSession], now: Date, empty: String) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            Text(title)
                .font(.headline)
                .accessibilityAddTraits(.isHeader)
            VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                if sessions.isEmpty {
                    Text(empty)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .frame(minHeight: 44)
                }
                ForEach(Array(sessions.enumerated()), id: \.element.id) { index, session in
                    if index > 0 { Divider() }
                    Button {
                        path.append(.session(sessionId: session.id))
                    } label: {
                        MeetingRow(session: session, now: now)
                    }
                    .buttonStyle(.plain)
                }
            }
            .cardStyle()
        }
    }
}

// MARK: - Previews

#Preview("Meetings tab") {
    @Previewable @State var path: [DoctorRoute] = []
    MeetingsView(path: $path, model: .demo(), records: .demo())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

#Preview("Meetings tab, no active session") {
    @Previewable @State var path: [DoctorRoute] = []
    MeetingsView(path: $path, model: .noActiveSession(), records: .demo())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
        .preferredColorScheme(.dark)
}

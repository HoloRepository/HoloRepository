//
//  NotificationsView.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

/// The doctor's in-app notifications, newest first, opened from the bell on Home.
/// Notifications are information only; things to do are tasks. Push notifications come in March.
struct NotificationsView: View {
    let model: DoctorHomeModel

    var body: some View {
        Group {
            if model.notifications.isEmpty {
                ContentUnavailableView("No notifications", systemImage: "bell",
                                       description: Text("New scans, comments and invitations will show here."))
            } else {
                List(model.notifications) { notification in
                    NotificationRow(notification: notification,
                                    patientName: model.patientName(for: notification.patientId))
                        .listRowBackground(Theme.card)
                }
                .listStyle(.insetGrouped)
                .scrollContentBackground(.hidden)
            }
        }
        .background(Theme.background)
        .navigationTitle("Notifications")
        .navigationBarTitleDisplayMode(.inline)
    }
}

private struct NotificationRow: View {
    let notification: AppNotification
    let patientName: String?

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: Theme.Spacing.m) {
            Image(systemName: notification.kind.systemImage)
                .foregroundStyle(Theme.doctorAccent)
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(notification.title)
                    .font(.body.weight(notification.isRead ? .regular : .semibold))
                if let patientName {
                    Text(patientName)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                Text(notification.timestamp, format: .relative(presentation: .named))
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
            if !notification.isRead {
                Circle()
                    .fill(Theme.doctorAccent)
                    .frame(width: Theme.Spacing.s, height: Theme.Spacing.s)
            }
        }
        .padding(.vertical, Theme.Spacing.xs)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
    }

    private var accessibilityText: String {
        var parts = notification.isRead ? [] : ["Unread"]
        parts.append(notification.title)
        if let patientName { parts.append(patientName) }
        parts.append(notification.timestamp.formatted(.relative(presentation: .named)))
        return parts.joined(separator: ", ")
    }
}

extension AppNotificationKind {
    var systemImage: String {
        switch self {
        case .newScan: "cube"
        case .comment: "text.bubble"
        case .meetingInvitation: "person.3"
        }
    }
}

#Preview("Notifications") {
    NavigationStack {
        NotificationsView(model: .demo())
    }
    .tint(Theme.doctorAccent)
}

#Preview("Notifications, empty") {
    NavigationStack {
        NotificationsView(model: .empty())
    }
    .tint(Theme.doctorAccent)
}

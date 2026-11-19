//
//  AppNotification.swift
//  
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation

/// The type of in-app notification.
enum AppNotificationKind: String, Codable, Hashable, Sendable {
    case newScan
    case comment
    case meetingInvitation
}

/// An in-app notification. Push notifications come later (March).
struct AppNotification: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String?
    let kind: AppNotificationKind
    let title: String
    let timestamp: Date
    let isRead: Bool
}

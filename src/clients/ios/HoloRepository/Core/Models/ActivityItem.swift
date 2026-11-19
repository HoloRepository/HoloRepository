//
//  ActivityItem.swift
//  
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation

/// The type of event in the recent activity feed.
enum ActivityKind: String, Codable, Hashable, Sendable {
    case scanUploaded
    case commentAdded
    case reportApproved
}

/// Something that recently happened on one of the doctor's cases.
struct ActivityItem: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let patientId: String
    let kind: ActivityKind
    let title: String
    let timestamp: Date
}

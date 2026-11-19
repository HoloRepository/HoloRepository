//
//  User.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// The two roles that decide which app a signed-in user sees.
enum UserRole: String, Codable, Hashable, Sendable {
    case doctor
    case patient
}

/// A signed-in account.
struct User: Codable, Identifiable, Hashable, Sendable {
    let id: String
    let name: String
    let role: UserRole
}

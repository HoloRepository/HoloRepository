//
//  AppState.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation
import Observation

/// App-wide state: who is signed in. `nil` means signed out.
@Observable
final class AppState {
    var currentUser: User?
    let dataService: MockDataService

    init(currentUser: User? = nil, dataService: MockDataService = MockDataService()) {
        self.currentUser = currentUser
        self.dataService = dataService
    }

    /// Mock sign-in: any credentials sign in as the demo user for the role.
    func signIn(as role: UserRole) {
        currentUser = dataService.demoUser(for: role)
    }

    func signOut() {
        currentUser = nil
    }
}

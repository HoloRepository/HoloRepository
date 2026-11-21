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
    /// Reports with clinicians' approvals and edits (saved on the device in December).
    let reportStore: any ReportStore

    init(currentUser: User? = nil, dataService: MockDataService = MockDataService(),
         reportStore: (any ReportStore)? = nil) {
        self.currentUser = currentUser
        self.dataService = dataService
        self.reportStore = reportStore ?? LocalReportStore(baseReports: dataService.reports)
    }

    /// Mock sign-in: any credentials sign in as the demo user for the role.
    func signIn(as role: UserRole) {
        currentUser = dataService.demoUser(for: role)
    }

    func signOut() {
        currentUser = nil
    }
}

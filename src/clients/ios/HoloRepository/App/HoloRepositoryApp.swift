//
//  HoloRepositoryApp.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//


import SwiftUI

@main
struct HoloRepositoryApp: App {
    @State private var appState = AppState()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(appState)
        }
    }
}

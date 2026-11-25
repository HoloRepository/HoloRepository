import SwiftUI

/// App entry point.
@main
struct HoloRepository2027App: App {
    var body: some Scene {
        WindowGroup("HoloRepository 2027") {
            ContentView()
                .frame(minWidth: 1000, minHeight: 640)
        }
        .defaultSize(width: 1500, height: 920)
    }
}

import SwiftUI

/// App entry point. Owns the app model and injects it (and its parts) into the view tree.
///
/// - `ViewerController`: drives the NiiVue viewer that runs inside a WKWebView.
/// - `AppModel`: opens files and tells the viewer what to show.
@main
struct HoloRepository2027App: App {
    @State private var model = AppModel()

    var body: some Scene {
        WindowGroup("HoloRepository 2027") {
            ContentView()
                .environment(model)
                .environment(model.viewer)
                .frame(minWidth: 1000, minHeight: 640)
        }
        .defaultSize(width: 1500, height: 920)
        .commands {
            CommandGroup(replacing: .newItem) {
                Button("Open Scan…") { model.showOpenPanel() }
                    .keyboardShortcut("o")
            }
        }
    }
}

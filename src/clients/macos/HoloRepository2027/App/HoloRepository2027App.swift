import SwiftUI

/// App entry point. Owns the app model and injects it (and its parts) into the view tree.
///
/// - `RecentScans`: scans kept for quick access (bookmarks to their files, never copies).
/// - `ViewerController`: drives the NiiVue viewer that runs inside a WKWebView.
/// - `AppModel`: ties them together (opening files, Recent scans).
@main
struct HoloRepository2027App: App {
    @State private var model = AppModel()

    var body: some Scene {
        WindowGroup("HoloRepository 2027") {
            ContentView()
                .environment(model)
                .environment(model.recents)
                .environment(model.viewer)
                .frame(minWidth: 1000, minHeight: 640)
        }
        .defaultSize(width: 1500, height: 920)
        .commands {
            CommandGroup(replacing: .newItem) {
                Button("Open Scan…") { model.showOpenPanel() }
                    .keyboardShortcut("o")
                Button("Add to Recent Scans") {
                    if let scan = model.selectedScan { model.addToRecents(scan) }
                }
                .keyboardShortcut("s")
                .disabled(model.selectedScan?.isRecent ?? true)
            }
            // Added to the standard View menu (a CommandMenu would create a second "View").
            CommandGroup(before: .toolbar) {
                ForEach(SliceLayout.allCases) { layout in
                    Button(layout.title) { model.viewer.setLayout(layout) }
                        .keyboardShortcut(layout.shortcut, modifiers: .command)
                }
                Divider()
                Button("Reset View") { model.viewer.resetView() }
                    .keyboardShortcut("r", modifiers: [.command, .shift])
                Divider()
            }
        }
    }
}

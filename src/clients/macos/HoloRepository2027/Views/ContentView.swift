import SwiftUI

/// Main window. The scan viewer is added in the next step.
struct ContentView: View {
    var body: some View {
        ContentUnavailableView("HoloRepository 2027", systemImage: "cube.transparent",
                               description: Text("View CT and MR scans on your Mac."))
    }
}

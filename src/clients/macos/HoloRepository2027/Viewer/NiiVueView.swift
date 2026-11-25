import SwiftUI
import WebKit

/// Puts the controller's WKWebView (which runs NiiVue) into the SwiftUI layout.
struct NiiVueView: NSViewRepresentable {
    let controller: ViewerController

    func makeNSView(context: Context) -> WKWebView {
        controller.webView
    }

    func updateNSView(_ nsView: WKWebView, context: Context) {}
}

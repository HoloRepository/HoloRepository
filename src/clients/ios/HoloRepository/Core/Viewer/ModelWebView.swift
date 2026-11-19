//
//  ModelWebView.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI
import WebKit

/// The shared web viewer (bundled holo_viewer.html) in a transparent WKWebView.
/// Draw `Theme.viewerBackground` behind it. Talk to the page only through `ViewerBridge`.
struct ModelWebView: UIViewRepresentable {
    let bridge: ViewerBridge
    /// Read by VoiceOver, e.g. "3D model of Alex Patel's CT abdomen".
    let accessibilityText: String

    /// The bundled viewer page, or nil if it's missing from the app.
    static var viewerURL: URL? {
        Bundle.main.url(forResource: "holo_viewer", withExtension: "html")
            ?? Bundle.main.url(forResource: "holo_viewer", withExtension: "html", subdirectory: "Viewer")
    }

    func makeUIView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.userContentController.add(ViewerMessageHandler(bridge: bridge),
                                                name: ViewerBridge.messageHandlerName)

        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.isOpaque = false
        webView.backgroundColor = .clear
        webView.scrollView.backgroundColor = .clear
        // The page handles rotate and zoom itself; the web view must not scroll or zoom.
        webView.scrollView.isScrollEnabled = false
        webView.scrollView.bounces = false
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        #if DEBUG
        webView.isInspectable = true
        #endif

        webView.isAccessibilityElement = true
        webView.accessibilityLabel = accessibilityText
        webView.accessibilityHint = "Drag to rotate, pinch to zoom, tap a structure to name it."
        webView.accessibilityTraits = .allowsDirectInteraction

        bridge.attach(webView)
        if let url = Self.viewerURL {
            webView.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
        }
        return webView
    }

    func updateUIView(_ webView: WKWebView, context: Context) {
        webView.accessibilityLabel = accessibilityText
    }

    static func dismantleUIView(_ webView: WKWebView, coordinator: ()) {
        webView.configuration.userContentController.removeScriptMessageHandler(forName: ViewerBridge.messageHandlerName)
    }
}

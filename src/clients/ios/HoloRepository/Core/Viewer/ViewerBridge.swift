//
//  ViewerBridge.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation
import Observation
import WebKit

/// The only way the app talks to the shared web viewer (holo_viewer.html).
///
/// Swift → JS: `HoloViewer.loadCase(case)`, `setVisible(ids)`, `resetView()`, `setOpacity(group, value)`.
/// JS → Swift: `{type: "ready"}`, `{type: "tap", id}` (id is null when tapping empty space).
/// Calls made before the page reports "ready" are queued and sent in order once it does.
@Observable
final class ViewerBridge {
    /// Name of the script message handler the page posts to (`window.webkit.messageHandlers.holoViewer`).
    static let messageHandlerName = "holoViewer"

    private(set) var isReady = false
    /// The structure last tapped in the 3D view, or nil after tapping empty space.
    private(set) var tappedStructureID: String?

    @ObservationIgnored private weak var webView: WKWebView?
    @ObservationIgnored private var pendingScripts: [String] = []

    // MARK: Swift → JS

    func loadCase(_ model: StudyModel, visibleIDs: [String]) {
        let payload = ViewerCasePayload(id: model.studyId, structures: model.structures, visible: visibleIDs)
        tappedStructureID = nil
        send("HoloViewer.loadCase(\(json(payload)))")
    }

    func setVisible(_ ids: [String]) {
        send("HoloViewer.setVisible(\(json(ids)))")
    }

    func resetView() {
        send("HoloViewer.resetView()")
    }

    /// Opacity 0...1 for every structure in a group. The viewer ignores this for lesions.
    func setOpacity(_ group: StructureGroup, _ value: Double) {
        send("HoloViewer.setOpacity(\(json(group.rawValue)), \(min(max(value, 0), 1)))")
    }

    // MARK: Web view wiring (used by ModelWebView)

    /// Called from `makeUIView`, i.e. during a view update, so it only touches observed state if needed.
    func attach(_ webView: WKWebView) {
        self.webView = webView
        if isReady { isReady = false }
    }

    /// Handles a message posted by the page.
    func receive(_ body: Any) {
        guard let message = body as? [String: Any], let type = message["type"] as? String else { return }
        switch type {
        case "ready":
            isReady = true
            let scripts = pendingScripts
            pendingScripts.removeAll()
            scripts.forEach(evaluate)
        case "tap":
            tappedStructureID = message["id"] as? String
        default:
            break
        }
    }

    // MARK: Private

    private func send(_ script: String) {
        guard isReady, webView != nil else {
            pendingScripts.append(script)
            return
        }
        evaluate(script)
    }

    private func evaluate(_ script: String) {
        webView?.evaluateJavaScript(script) { _, error in
            if let error {
                print("ViewerBridge: \(script.prefix(40))… failed: \(error)")
            }
        }
    }

    private func json<T: Encodable>(_ value: T) -> String {
        guard let data = try? JSONEncoder().encode(value) else { return "null" }
        return String(decoding: data, as: UTF8.self)
    }
}

/// The case object passed to `HoloViewer.loadCase`.
private struct ViewerCasePayload: Encodable {
    let id: String
    let structures: [Structure]
    let visible: [String]
}

/// Forwards page messages to the bridge. Separate object with a weak reference, because
/// `WKUserContentController` keeps its handlers alive.
final class ViewerMessageHandler: NSObject, WKScriptMessageHandler {
    weak var bridge: ViewerBridge?

    init(bridge: ViewerBridge) {
        self.bridge = bridge
    }

    func userContentController(_ userContentController: WKUserContentController,
                               didReceive message: WKScriptMessage) {
        bridge?.receive(message.body)
    }
}

import Observation
import OSLog
import WebKit

/// Messages from the web viewer. Read them with:
/// `log stream --predicate 'subsystem == "uk.ac.ucl.holorepository2027"'`
private let viewerLog = Logger(subsystem: "uk.ac.ucl.holorepository2027", category: "viewer")

/// Owns the WKWebView that runs NiiVue and is the only place that talks to it.
///
/// Swift → JavaScript: `callAsyncJavaScript("window.holo.<function>(…)")`
/// JavaScript → Swift: `window.webkit.messageHandlers.holo.postMessage({...})`
@Observable
final class ViewerController {
    enum Status: Equatable {
        case starting, idle, loading(String), ready, failed(String)
    }

    private(set) var status: Status = .starting
    private(set) var loadedScan: Scan?
    private(set) var locationText = ""
    private(set) var volumeInfo = ""

    @ObservationIgnored let webView: WKWebView
    @ObservationIgnored private let fileServer = ViewerSchemeHandler()
    @ObservationIgnored private var pendingScan: Scan?

    init() {
        let config = WKWebViewConfiguration()
        config.setURLSchemeHandler(fileServer, forURLScheme: ViewerSchemeHandler.scheme)
        webView = WKWebView(frame: .zero, configuration: config)
        #if DEBUG
        // Safari ▸ Develop ▸ <this Mac> ▸ HoloRepository 2027 gives you the JS console.
        webView.isInspectable = true
        #endif
        // WKWebView copies its configuration, so register on the web view's own copy.
        webView.configuration.userContentController.add(WeakMessageHandler(self), name: "holo")
        webView.load(URLRequest(url: ViewerSchemeHandler.appURL(for: "index.html")))
    }

    // MARK: - Scans

    func load(_ scan: Scan) {
        guard status != .starting else {
            pendingScan = scan // viewer page not ready yet; load once it is
            return
        }
        loadedScan = scan
        status = .loading(scan.format == .dicom ? "Reading \(scan.imageFiles.count) DICOM files…" : "Reading NIfTI file…")
        volumeInfo = ""
        let urls = fileServer.publish(scan.imageFiles).map(\.absoluteString)
        let names = scan.imageFiles.map(\.lastPathComponent)
        call("return await window.holo.loadScan(name, format, urls, names)",
             ["name": scan.name, "format": scan.format.rawValue, "urls": urls, "names": names])
    }

    func unload() {
        loadedScan = nil
        status = .idle
        volumeInfo = ""
        locationText = ""
        call("window.holo.clearAll()", [:])
    }

    // MARK: - Bridge

    private func call(_ script: String, _ arguments: [String: Any]) {
        webView.callAsyncJavaScript(script, arguments: arguments, in: nil, in: .page) { [weak self] result in
            if case .failure(let error) = result {
                self?.status = .failed(Self.describe(error))
            }
        }
    }

    private static func describe(_ error: Error) -> String {
        let nsError = error as NSError
        return (nsError.userInfo["WKJavaScriptExceptionMessage"] as? String) ?? error.localizedDescription
    }

    fileprivate func handle(_ body: [String: Any]) {
        switch body["type"] as? String {
        case "ready":
            status = .idle
            if let pending = pendingScan {
                pendingScan = nil
                load(pending)
            }
        case "progress":
            status = .loading(body["message"] as? String ?? "Loading…")
        case "loaded":
            status = .ready
            volumeInfo = body["info"] as? String ?? ""
            fileServer.unpublish(group: "scan")
        case "error":
            status = .failed(body["message"] as? String ?? "Unknown viewer error")
            fileServer.unpublish(group: "scan")
        case "location":
            locationText = body["text"] as? String ?? ""
        case "log":
            viewerLog.notice("\(String(describing: body["message"] ?? ""), privacy: .public)")
        default:
            break
        }
    }
}

/// WKUserContentController retains its handlers strongly; this breaks the cycle.
private final class WeakMessageHandler: NSObject, WKScriptMessageHandler {
    weak var controller: ViewerController?
    init(_ controller: ViewerController) { self.controller = controller }

    func userContentController(_ userContentController: WKUserContentController,
                               didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any] else { return }
        controller?.handle(body)
    }
}

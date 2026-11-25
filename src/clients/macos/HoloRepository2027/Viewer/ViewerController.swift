import Observation
import OSLog
import SwiftUI
import WebKit

/// Messages from the web viewer. Read them with:
/// `log stream --predicate 'subsystem == "uk.ac.ucl.holorepository2027"'`
private let viewerLog = Logger(subsystem: "uk.ac.ucl.holorepository2027", category: "viewer")

/// The NiiVue view layouts. Raw values match NiiVue's `SLICE_TYPE` enum.
enum SliceLayout: Int, CaseIterable, Identifiable {
    case axial = 0, coronal, sagittal, multiplanar, render

    var id: Int { rawValue }

    var title: String {
        switch self {
        case .axial: "Axial"
        case .coronal: "Coronal"
        case .sagittal: "Sagittal"
        case .multiplanar: "All views"
        case .render: "3D"
        }
    }

    var help: String {
        switch self {
        case .axial: "Show slices from top to bottom (axial plane)"
        case .coronal: "Show slices from front to back (coronal plane)"
        case .sagittal: "Show slices from side to side (sagittal plane)"
        case .multiplanar: "Show axial, coronal, sagittal and 3D together"
        case .render: "Show only the 3D view. Drag to rotate."
        }
    }

    var symbol: String {
        switch self {
        case .axial: "square.split.1x2"
        case .coronal: "person"
        case .sagittal: "person.fill.turn.right"
        case .multiplanar: "square.grid.2x2"
        case .render: "cube"
        }
    }

    var shortcut: KeyEquivalent { KeyEquivalent(Character(String(rawValue + 1))) }
}

/// What a left-drag does in the viewer. Raw values are NiiVue `DRAG_MODE` names.
enum MouseTool: String, CaseIterable, Identifiable {
    case crosshair, windowing, pan, measurement

    var id: String { rawValue }

    var title: String {
        switch self {
        case .crosshair: "Point"
        case .windowing: "Contrast"
        case .pan: "Pan"
        case .measurement: "Measure"
        }
    }

    var help: String {
        switch self {
        case .crosshair: "Click to move the crosshair and read the value under it"
        case .windowing: "Drag to change brightness (level) and contrast (width)"
        case .pan: "Drag to move the image. Scroll to zoom."
        case .measurement: "Drag across the image to measure a distance in mm"
        }
    }

    var symbol: String {
        switch self {
        case .crosshair: "scope"
        case .windowing: "circle.lefthalf.filled"
        case .pan: "hand.draw"
        case .measurement: "ruler"
        }
    }
}

/// Standard CT window presets (Hounsfield units).
struct WindowPreset: Identifiable, Hashable {
    let name: String
    let center: Double
    let width: Double
    var id: String { name }

    static let ct: [WindowPreset] = [
        .init(name: "Soft tissue", center: 40, width: 400),
        .init(name: "Lung", center: -600, width: 1500),
        .init(name: "Bone", center: 400, width: 1800),
        .init(name: "Brain", center: 40, width: 80),
        .init(name: "Liver", center: 60, width: 160),
    ]
}

/// How the 3D-only view shows the scan.
enum RenderPreset: String, CaseIterable, Identifiable {
    case standard, bone, skin

    var id: String { rawValue }

    var title: String {
        switch self {
        case .standard: "Standard"
        case .bone: "Bone"
        case .skin: "Skin"
        }
    }

    var help: String {
        switch self {
        case .standard: "Uses the same brightness/contrast as the 2D views"
        case .bone: "Makes everything except bone transparent (CT only)"
        case .skin: "Shows the outer skin surface (CT only)"
        }
    }
}

/// Owns the WKWebView that runs NiiVue and is the only place that talks to it.
///
/// Swift → JavaScript: `callAsyncJavaScript("window.holo.<function>(…)")`
/// JavaScript → Swift: `window.webkit.messageHandlers.holo.postMessage({...})`
@Observable
final class ViewerController {
    enum Status: Equatable {
        case starting, idle, loading(String), ready, failed(String)
    }

    // Scan
    private(set) var status: Status = .starting
    private(set) var loadedScan: Scan?
    private(set) var locationText = ""
    private(set) var windowCenter: Double?
    private(set) var windowWidth: Double?
    private(set) var volumeInfo = ""
    /// True when the volume's values look like Hounsfield units, so CT presets make sense.
    private(set) var looksLikeCT = false
    private(set) var layout: SliceLayout = .multiplanar
    private(set) var tool: MouseTool = .crosshair

    // 3D
    private(set) var renderPreset: RenderPreset = .standard
    private(set) var lighting: Double = 0.6
    private(set) var cutaway = false

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

    /// Updates the scan's details (e.g. after saving it) without reloading the images.
    func updateLoadedScan(_ scan: Scan) {
        guard loadedScan?.id == scan.id else { return }
        loadedScan = scan
    }

    func unload() {
        loadedScan = nil
        status = .idle
        volumeInfo = ""
        windowCenter = nil
        windowWidth = nil
        locationText = ""
        call("window.holo.clearAll()", [:])
    }

    // MARK: - View controls

    func setLayout(_ newLayout: SliceLayout) {
        layout = newLayout
        call("window.holo.setLayout(value)", ["value": newLayout.rawValue])
    }

    func setTool(_ newTool: MouseTool) {
        tool = newTool
        call("window.holo.setTool(value)", ["value": newTool.rawValue])
    }

    func applyWindow(center: Double, width: Double) {
        call("window.holo.setWindow(center, width)", ["center": center, "width": width])
    }

    func resetView() {
        call("window.holo.resetView()", [:])
    }

    func setRenderPreset(_ preset: RenderPreset) {
        renderPreset = preset
        // Presets only affect the 3D-only view, so switch to it when one is picked.
        if preset != .standard && layout != .render && status == .ready { setLayout(.render) }
        call("window.holo.setRenderPreset(value)", ["value": preset.rawValue])
    }

    func setLighting(_ amount: Double) {
        lighting = amount
        call("window.holo.setLighting(value)", ["value": amount])
    }

    func setCutaway(_ enabled: Bool) {
        cutaway = enabled
        call("window.holo.setCutaway(value)", ["value": enabled])
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
            setLayout(layout)
            setLighting(lighting)
            if let pending = pendingScan {
                pendingScan = nil
                load(pending)
            }
        case "progress":
            status = .loading(body["message"] as? String ?? "Loading…")
        case "loaded":
            status = .ready
            volumeInfo = body["info"] as? String ?? ""
            looksLikeCT = body["looksLikeCT"] as? Bool ?? false
            fileServer.unpublish(group: "scan")
            setRenderPreset(renderPreset == .standard || looksLikeCT ? renderPreset : .standard)
            setCutaway(cutaway)
        case "error":
            status = .failed(body["message"] as? String ?? "Unknown viewer error")
            fileServer.unpublish(group: "scan")
        case "location":
            locationText = body["text"] as? String ?? ""
        case "window":
            windowCenter = body["center"] as? Double
            windowWidth = body["width"] as? Double
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

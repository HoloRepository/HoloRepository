import Foundation
import UniformTypeIdentifiers
import WebKit

/// Serves everything the web viewer needs over a private `holo://` URL scheme:
///
/// - `holo://app/<path>`               → files bundled in the app's `WebViewer` folder (NiiVue, HTML, WASM)
/// - `holo://app/_files/<token>/<n>`   → the n-th file of the scan currently being loaded
///
/// Both live under the same host (`app`) on purpose: to WebKit a different host is a
/// different website, and cross-site `fetch` would be blocked (CORS).
///
/// Why not `file://`? WebKit blocks ES modules, Web Workers and `fetch` on `file://` pages.
/// A custom scheme behaves like a normal website, but nothing ever leaves this Mac.
/// Only files Swift explicitly `publish`es can be read, under a random token.
final class ViewerSchemeHandler: NSObject, WKURLSchemeHandler {
    static let scheme = "holo"

    static func appURL(for path: String) -> URL {
        URL(string: "\(scheme)://app/\(path)")!
    }

    private let webRoot = Bundle.main.resourceURL!.appending(path: "WebViewer", directoryHint: .isDirectory)
    /// Files the page may read right now, by group (for example "scan"), each under a random token.
    private var published: [String: (token: String, files: [URL])] = [:]
    private var stoppedTasks = Set<ObjectIdentifier>()

    /// Makes `files` readable by the web page and returns the URLs to fetch them with.
    /// Publishing again in the same group replaces what was there.
    func publish(_ files: [URL], group: String = "scan") -> [URL] {
        let token = UUID().uuidString
        published[group] = (token, files)
        return files.indices.map { Self.appURL(for: "_files/\(token)/\($0)") }
    }

    func unpublish(group: String) {
        published[group] = nil
    }

    // MARK: WKURLSchemeHandler

    func webView(_ webView: WKWebView, start urlSchemeTask: any WKURLSchemeTask) {
        guard let url = urlSchemeTask.request.url, let fileURL = resolve(url) else {
            respond(urlSchemeTask, status: 404, data: Data("Not found".utf8), mimeType: "text/plain")
            return
        }
        let taskID = ObjectIdentifier(urlSchemeTask)
        stoppedTasks.remove(taskID)

        Task {
            // Read on a background thread so big DICOM series don't freeze the UI.
            let data = await Task.detached(priority: .userInitiated) { try? Data(contentsOf: fileURL) }.value
            guard !stoppedTasks.contains(taskID) else { return } // page cancelled the request
            if let data {
                respond(urlSchemeTask, status: 200, data: data, mimeType: Self.mimeType(for: fileURL))
            } else {
                respond(urlSchemeTask, status: 404, data: Data("Unreadable".utf8), mimeType: "text/plain")
            }
        }
    }

    func webView(_ webView: WKWebView, stop urlSchemeTask: any WKURLSchemeTask) {
        stoppedTasks.insert(ObjectIdentifier(urlSchemeTask))
    }

    // MARK: Helpers

    private func resolve(_ url: URL) -> URL? {
        guard url.host() == "app" else { return nil }
        let parts = url.pathComponents.filter { $0 != "/" }
        switch parts.first {
        case "_files":
            guard parts.count == 3, let index = Int(parts[2]),
                  let entry = published.values.first(where: { $0.token == parts[1] }),
                  entry.files.indices.contains(index) else { return nil }
            return entry.files[index]
        default:
            let candidate = webRoot.appending(path: parts.joined(separator: "/")).standardizedFileURL
            // Never serve anything outside the WebViewer folder (e.g. "../../").
            guard candidate.path.hasPrefix(webRoot.standardizedFileURL.path) else { return nil }
            return candidate
        }
    }

    private func respond(_ task: any WKURLSchemeTask, status: Int, data: Data, mimeType: String) {
        let headers = [
            "Content-Type": mimeType,
            "Content-Length": String(data.count),
            "Cache-Control": "no-store",
        ]
        let response = HTTPURLResponse(url: task.request.url!, statusCode: status,
                                       httpVersion: "HTTP/1.1", headerFields: headers)!
        task.didReceive(response)
        task.didReceive(data)
        task.didFinish()
    }

    private static func mimeType(for url: URL) -> String {
        switch url.pathExtension.lowercased() {
        case "js", "mjs": "text/javascript"
        case "wasm": "application/wasm"
        case "html": "text/html; charset=utf-8"
        case "css": "text/css"
        case "json": "application/json"
        case "dcm": "application/dicom"
        default: UTType(filenameExtension: url.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
        }
    }
}

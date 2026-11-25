import SwiftUI

/// Right-hand panel: scan details and 3D options.
struct InspectorView: View {
    @Environment(AppModel.self) private var model
    @Environment(ViewerController.self) private var viewer

    var body: some View {
        if viewer.loadedScan == nil || viewer.status == .starting {
            ContentUnavailableView("No scan open", systemImage: "sidebar.right",
                                   description: Text("Scan details and 3D options appear here once a scan is open."))
        } else {
            Form {
                ScanSection()
                ThreeDSection()
            }
            .formStyle(.grouped)
            .disabled(viewer.status != .ready)
        }
    }
}

// MARK: - Scan

private struct ScanSection: View {
    @Environment(ViewerController.self) private var viewer

    var body: some View {
        Section("Scan") {
            if let scan = viewer.loadedScan {
                LabeledContent("Name") {
                    Text(scan.name).lineLimit(2).truncationMode(.middle).textSelection(.enabled)
                }
                LabeledContent("Format", value: scan.format.title)
                if !scan.modality.isEmpty { LabeledContent("Modality", value: scan.modality) }
                LabeledContent("Recent scans", value: scan.isRecent ? "Yes" : "No (opened this session)")
                if let folder = scan.imageFiles.first?.deletingLastPathComponent() {
                    LabeledContent("Location") {
                        Text(Self.abbreviated(folder)).lineLimit(2).truncationMode(.middle).textSelection(.enabled)
                    }
                    .help("Where the scan's files are: \(folder.path). The app reads them from here and never copies them.")
                }
                if !viewer.volumeInfo.isEmpty {
                    Text(viewer.volumeInfo).font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }

    /// Shows paths in the home folder as "~/…" (the sandbox's own home is a container folder,
    /// so the real one is looked up).
    private static func abbreviated(_ url: URL) -> String {
        guard let entry = getpwuid(getuid()) else { return url.path }
        let home = String(cString: entry.pointee.pw_dir)
        return url.path.hasPrefix(home + "/") ? "~" + url.path.dropFirst(home.count) : url.path
    }
}

// MARK: - 3D

private struct ThreeDSection: View {
    @Environment(ViewerController.self) private var viewer

    var body: some View {
        Section {
            VStack(alignment: .leading, spacing: 6) {
                Text("Show scan in 3D as")
                Picker("Show scan in 3D as", selection: Binding(get: { viewer.renderPreset }, set: { viewer.setRenderPreset($0) })) {
                    ForEach(RenderPreset.allCases) { preset in
                        Text(preset.title).tag(preset)
                    }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .help(viewer.renderPreset.help)
            }
            Text(viewer.renderPreset.help).font(.caption).foregroundStyle(.secondary)

            LabeledContent("Lighting") {
                Slider(value: Binding(get: { viewer.lighting }, set: { viewer.setLighting($0) }), in: 0...1) {
                    Text("Lighting")
                } minimumValueLabel: {
                    Image(systemName: "sun.min")
                } maximumValueLabel: {
                    Image(systemName: "sun.max")
                }
                .labelsHidden()
            }
            .help("Adds shading so surfaces look solid")

            Toggle("Cut away the front half", isOn: Binding(get: { viewer.cutaway }, set: { viewer.setCutaway($0) }))
                .help("Slices the 3D view open so you can see inside")
        } header: {
            Text("3D view")
        } footer: {
            Text("Drag the 3D view to rotate it. Scroll to zoom.").font(.caption2)
        }
    }
}

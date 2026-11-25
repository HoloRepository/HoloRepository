import SwiftUI

/// Main window: scans on the left, viewer in the middle, inspector on the right.
struct ContentView: View {
    @Environment(AppModel.self) private var model
    @Environment(ViewerController.self) private var viewer
    @State private var showInspector = false

    var body: some View {
        @Bindable var model = model
        NavigationSplitView {
            SidebarView()
                .navigationSplitViewColumnWidth(min: 230, ideal: 270, max: 400)
        } detail: {
            ViewerPane()
                .inspector(isPresented: $showInspector) {
                    InspectorView()
                        .inspectorColumnWidth(min: 270, ideal: 300, max: 400)
                }
        }
        .toolbar { MainToolbar(showInspector: $showInspector) }
        .onChange(of: viewer.loadedScan == nil) { _, noScan in
            if !noScan { showInspector = true }
        }
        .dropDestination(for: URL.self) { urls, _ in
            model.importItems(urls)
            return true
        }
        .sheet(item: $model.importChoice) { result in
            ScanPickerSheet(result: result)
        }
        .alert(item: $model.message) { message in
            Alert(title: Text(message.title), message: Text(message.detail))
        }
    }
}

extension ImportResult: Identifiable {
    var id: [Scan.ID] { scans.map(\.id) }
}

private struct ViewerPane: View {
    @Environment(AppModel.self) private var model
    @Environment(ViewerController.self) private var viewer

    var body: some View {
        VStack(spacing: 0) {
            ViewerControlBar()
            Divider()
            ZStack {
                NiiVueView(controller: viewer)
                overlay
            }
            Divider()
            StatusBar()
        }
        .navigationTitle(viewer.loadedScan?.name ?? "HoloRepository 2027")
        .navigationSubtitle(viewer.loadedScan.map { "\($0.format.title) · \($0.summary)" } ?? "")
    }

    @ViewBuilder private var overlay: some View {
        switch viewer.status {
        case .starting:
            ProgressView("Starting viewer…").padding().background(.regularMaterial, in: .rect(cornerRadius: 10))
        case .idle where viewer.loadedScan == nil:
            WelcomeView()
        case .loading(let message):
            ProgressView(message).padding().background(.regularMaterial, in: .rect(cornerRadius: 10))
        case .failed(let message):
            ContentUnavailableView("Couldn't show this scan", systemImage: "exclamationmark.triangle",
                                   description: Text(message))
                .padding().background(.regularMaterial, in: .rect(cornerRadius: 10))
                .frame(maxWidth: 480)
        default:
            EmptyView()
        }
    }
}

/// Shown when no scan is open.
private struct WelcomeView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(spacing: 14) {
            Image(systemName: "cube.transparent").font(.system(size: 48)).foregroundStyle(.secondary)
            Text("Open a scan to get started").font(.title2.weight(.semibold))
            Text("NIfTI (.nii, .nii.gz) files, DICOM (.dcm) files, or a folder with a DICOM series.\nYou can also drag files onto this window.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)
            Button {
                model.showOpenPanel()
            } label: {
                Label("Browse This Mac…", systemImage: "folder")
                    .padding(.horizontal, 6)
            }
            .controlSize(.large)
            .buttonStyle(.borderedProminent)
            .disabled(model.isImporting)
            if model.isImporting { ProgressView("Looking for scans…").controlSize(.small) }
        }
        .padding(30)
        .background(.regularMaterial, in: .rect(cornerRadius: 14))
        .environment(\.colorScheme, .dark)
    }
}

private struct StatusBar: View {
    @Environment(ViewerController.self) private var viewer

    var body: some View {
        HStack(spacing: 16) {
            if let c = viewer.windowCenter, let w = viewer.windowWidth {
                Label("Level \(Int(c.rounded()))  Width \(Int(w.rounded()))", systemImage: "circle.lefthalf.filled")
                    .help("Current brightness (level) and contrast (width)")
            }
            if !viewer.volumeInfo.isEmpty {
                Text(viewer.volumeInfo)
            }
            Spacer()
            Text(viewer.locationText).monospacedDigit().lineLimit(1).truncationMode(.head)
        }
        .font(.caption)
        .foregroundStyle(.secondary)
        .padding(.horizontal, 12)
        .padding(.vertical, 5)
    }
}

private struct MainToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model
    @Binding var showInspector: Bool

    var body: some ToolbarContent {
        ToolbarItemGroup(placement: .navigation) {
            Button {
                model.showOpenPanel()
            } label: {
                Label("Open…", systemImage: "folder").labelStyle(.titleAndIcon)
            }
            .help("Open a NIfTI file, DICOM files or a DICOM folder from this Mac (⌘O)")
        }
        ToolbarItemGroup(placement: .primaryAction) {
            if let scan = model.selectedScan, !scan.isRecent {
                Button {
                    model.addToRecents(scan)
                } label: {
                    Label("Add to Recent", systemImage: "clock.arrow.circlepath").labelStyle(.titleAndIcon)
                }
                .help("Remember where this scan is so you can reopen it quickly. Nothing is copied. (⌘S)")
            }
            Button {
                showInspector.toggle()
            } label: {
                Label(showInspector ? "Hide Panel" : "Show Panel", systemImage: "sidebar.right").labelStyle(.titleAndIcon)
            }
            .help("Show or hide the panel with scan details and 3D options")
        }
    }
}

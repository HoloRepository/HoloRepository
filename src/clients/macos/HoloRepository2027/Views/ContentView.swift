import SwiftUI

/// Main window: scans on the left, viewer on the right.
struct ContentView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var model = model
        NavigationSplitView {
            SidebarView()
                .navigationSplitViewColumnWidth(min: 230, ideal: 270, max: 400)
        } detail: {
            ViewerPane()
        }
        .toolbar { MainToolbar() }
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

    var body: some ToolbarContent {
        ToolbarItemGroup(placement: .navigation) {
            Button {
                model.showOpenPanel()
            } label: {
                Label("Open…", systemImage: "folder").labelStyle(.titleAndIcon)
            }
            .help("Open a NIfTI file, DICOM files or a DICOM folder from this Mac (⌘O)")
        }
    }
}

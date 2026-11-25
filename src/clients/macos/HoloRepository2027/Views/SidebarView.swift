import SwiftUI

/// Left column. Shows only scans the user opened this session. Nothing on disk is listed
/// automatically, and nothing is copied.
struct SidebarView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(spacing: 0) {
            if model.openedScans.isEmpty {
                ContentUnavailableView {
                    Label("No scans yet", systemImage: "tray")
                } description: {
                    Text("Open a scan from this Mac.")
                } actions: {
                    Button("Browse This Mac…") { model.showOpenPanel() }
                }
            } else {
                list
            }
            Divider()
            footer
        }
    }

    private var list: some View {
        List(selection: Binding(get: { model.selectedScanID }, set: { model.select($0) })) {
            Section {
                ForEach(model.openedScans) { scan in
                    ScanRow(scan: scan)
                        .tag(scan.id)
                        .contextMenu {
                            Button("Close") { model.close(scan) }
                        }
                }
            } header: {
                Text("Opened")
            } footer: {
                Text("These close when you quit the app.").font(.caption2).foregroundStyle(.tertiary)
            }
        }
        .onDeleteCommand {
            if let scan = model.selectedScan { model.close(scan) }
        }
    }

    private var footer: some View {
        HStack(spacing: 8) {
            Button {
                model.showOpenPanel()
            } label: {
                Label("Open…", systemImage: "plus")
            }
            .help("Open a scan from this Mac")

            Spacer()

            if let scan = model.selectedScan {
                Button {
                    model.close(scan)
                } label: {
                    Label("Close", systemImage: "xmark")
                }
                .help("Close this scan")
            }
        }
        .labelStyle(.titleAndIcon)
        .buttonStyle(.borderless)
        .controlSize(.small)
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .overlay(alignment: .top) {
            if model.isImporting {
                ProgressView().progressViewStyle(.linear).controlSize(.mini)
            }
        }
    }
}

private struct ScanRow: View {
    let scan: Scan

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: scan.symbol)
                .foregroundStyle(Color.accentColor)
                .frame(width: 18)
            VStack(alignment: .leading, spacing: 2) {
                Text(scan.name).lineLimit(1)
                HStack(spacing: 4) {
                    Text(scan.format.title)
                    Text("·")
                    Text(scan.summary).lineLimit(1)
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 2)
        .help(scan.imageFiles.first?.deletingLastPathComponent().path ?? "")
    }
}

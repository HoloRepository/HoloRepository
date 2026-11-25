import AppKit
import SwiftUI

/// Left column. Shows only scans the user opened this session ("Opened") and scans they
/// kept for quick access ("Recent scans"). Nothing on disk is listed automatically, and
/// nothing is copied: Recent scans only remembers where each scan is.
struct SidebarView: View {
    @Environment(AppModel.self) private var model
    @Environment(RecentScans.self) private var recents
    @State private var scanBeingRenamed: Scan?
    @State private var newName = ""

    var body: some View {
        VStack(spacing: 0) {
            if model.openedScans.isEmpty && recents.scans.isEmpty {
                ContentUnavailableView {
                    Label("No scans yet", systemImage: "tray")
                } description: {
                    Text("Open a scan from this Mac. Add it to Recent scans to find it again quickly.")
                } actions: {
                    Button("Browse This Mac…") { model.showOpenPanel() }
                }
            } else {
                list
            }
            Divider()
            footer
        }
        .alert("Rename Scan", isPresented: Binding(get: { scanBeingRenamed != nil }, set: { if !$0 { scanBeingRenamed = nil } })) {
            TextField("Name", text: $newName)
            Button("Rename") {
                if let scan = scanBeingRenamed { model.rename(scan, to: newName) }
                scanBeingRenamed = nil
            }
            Button("Cancel", role: .cancel) { scanBeingRenamed = nil }
        } message: {
            Text("Changes the name shown in the app. The scan's files are not renamed.")
        }
    }

    private var list: some View {
        List(selection: Binding(get: { model.selectedScanID }, set: { model.select($0) })) {
            if !model.openedScans.isEmpty {
                Section {
                    ForEach(model.openedScans) { scan in
                        ScanRow(scan: scan, isMissing: false)
                            .tag(scan.id)
                            .contextMenu {
                                Button("Add to Recent Scans") { model.addToRecents(scan) }
                                Button("Close") { model.close(scan) }
                            }
                    }
                } header: {
                    Text("Opened")
                } footer: {
                    Text("These close when you quit the app.").font(.caption2).foregroundStyle(.tertiary)
                }
            }
            Section("Recent scans") {
                if recents.scans.isEmpty {
                    Text("Scans you add appear here, so you can reopen them quickly.")
                        .font(.callout).foregroundStyle(.secondary)
                }
                ForEach(recents.scans) { scan in
                    ScanRow(scan: scan, isMissing: recents.isMissing(scan.id))
                        .tag(scan.id)
                        .contextMenu {
                            Button("Show in Finder") { showInFinder(scan) }
                                .disabled(recents.isMissing(scan.id))
                            Button("Rename…") { newName = scan.name; scanBeingRenamed = scan }
                            Divider()
                            Button("Remove from Recent Scans") { model.removeFromRecents(scan) }
                        }
                }
            }
        }
        .onDeleteCommand {
            guard let scan = model.selectedScan else { return }
            if scan.isRecent { model.removeFromRecents(scan) } else { model.close(scan) }
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
                if scan.isRecent {
                    Button {
                        model.removeFromRecents(scan)
                    } label: {
                        Label("Remove", systemImage: "minus.circle")
                    }
                    .help("Remove from Recent scans. The scan's files are not touched.")
                } else {
                    Button {
                        model.close(scan)
                    } label: {
                        Label("Close", systemImage: "xmark")
                    }
                    .help("Close this scan")
                    Button {
                        model.addToRecents(scan)
                    } label: {
                        Label("Add to Recent", systemImage: "clock.arrow.circlepath")
                    }
                    .help("Remember where this scan is so you can reopen it quickly. Nothing is copied.")
                }
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

    private func showInFinder(_ scan: Scan) {
        NSWorkspace.shared.activateFileViewerSelecting(Array(scan.imageFiles.prefix(1)))
    }
}

private struct ScanRow: View {
    let scan: Scan
    let isMissing: Bool

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: isMissing ? "exclamationmark.triangle" : scan.symbol)
                .foregroundStyle(isMissing ? Color.orange : Color.accentColor)
                .frame(width: 18)
            VStack(alignment: .leading, spacing: 2) {
                Text(scan.name).lineLimit(1)
                HStack(spacing: 4) {
                    if isMissing {
                        Text("File not found")
                    } else {
                        Text(scan.format.title)
                        Text("·")
                        Text(scan.summary).lineLimit(1)
                    }
                }
                .font(.caption)
                .foregroundStyle(isMissing ? Color.orange : .secondary)
            }
        }
        .padding(.vertical, 2)
        .opacity(isMissing ? 0.7 : 1)
        .help(isMissing ? "The files have been moved, renamed or deleted, or their drive isn't connected." : (scan.imageFiles.first?.deletingLastPathComponent().path ?? ""))
    }
}

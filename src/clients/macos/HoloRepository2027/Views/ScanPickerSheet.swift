import SwiftUI

/// Shown when the files/folder the user picked contain more than one scan.
struct ScanPickerSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let result: ImportResult
    @State private var selection: Scan.ID?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Choose a scan to open").font(.title2.weight(.semibold))
            Text("What you picked contains \(result.scans.count) scans. Open one now, or add them all to Recent scans.")
                .foregroundStyle(.secondary)

            List(result.scans, selection: $selection) { scan in
                HStack {
                    Image(systemName: scan.symbol).foregroundStyle(Color.accentColor)
                    VStack(alignment: .leading) {
                        Text(scan.name)
                        Text("\(scan.format.title) · \(scan.summary)")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
                .tag(scan.id)
            }
            .frame(minHeight: 220)
            .contextMenu(forSelectionType: Scan.ID.self) { _ in } primaryAction: { ids in
                if let id = ids.first { openScan(id) }
            }

            ForEach(result.warnings, id: \.self) { warning in
                Label(warning, systemImage: "exclamationmark.triangle").font(.caption).foregroundStyle(.orange)
            }

            HStack {
                Button("Add All to Recent Scans") {
                    model.addAllToRecents(result.scans)
                    dismiss()
                }
                .help("Remember where every scan listed here is, so you can reopen them quickly. Nothing is copied.")
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Open") { if let selection { openScan(selection) } }
                    .keyboardShortcut(.defaultAction)
                    .disabled(selection == nil)
            }
        }
        .padding(20)
        .frame(width: 560, height: 440)
        .onAppear { selection = result.scans.first?.id }
    }

    private func openScan(_ id: Scan.ID) {
        guard let scan = result.scans.first(where: { $0.id == id }) else { return }
        dismiss()
        model.open(scan)
    }
}

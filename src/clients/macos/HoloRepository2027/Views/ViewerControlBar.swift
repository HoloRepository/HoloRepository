import SwiftUI

/// The bar above the viewer. Every control has a visible text label and a tooltip.
///
/// The bar adapts to the space it has (for example when the right-hand panel is open
/// or the window is small) so no button is ever cut off:
/// 1. one row, normal size;
/// 2. one row, compact;
/// 3. two rows (Layout on top, Mouse and Image below);
/// 4. two rows, compact.
struct ViewerControlBar: View {
    var body: some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .top, spacing: 10) { groups(compact: false) }
                .padding(.horizontal, 12)
            HStack(alignment: .top, spacing: 6) { groups(compact: true) }
                .padding(.horizontal, 8)
            twoRows(compact: false)
            twoRows(compact: true)
        }
        .padding(.vertical, 6)
        .frame(maxWidth: .infinity)
        .background(.bar)
    }

    @ViewBuilder private func groups(compact: Bool) -> some View {
        LayoutGroup(compact: compact)
        GroupDivider()
        MouseGroup(compact: compact)
        GroupDivider()
        ImageGroup(compact: compact)
    }

    private func twoRows(compact: Bool) -> some View {
        VStack(spacing: 6) {
            LayoutGroup(compact: compact)
            HStack(alignment: .top, spacing: compact ? 6 : 10) {
                MouseGroup(compact: compact)
                GroupDivider()
                ImageGroup(compact: compact)
            }
        }
        .padding(.horizontal, 8)
    }
}

// MARK: - Groups

private struct LayoutGroup: View {
    @Environment(ViewerController.self) private var viewer
    let compact: Bool

    var body: some View {
        ControlGroupBox(title: "Layout") {
            ForEach(SliceLayout.allCases) { layout in
                ToolButton(title: layout.title, symbol: layout.symbol, help: layout.help,
                           isSelected: viewer.layout == layout, compact: compact) {
                    viewer.setLayout(layout)
                }
            }
        }
    }
}

private struct MouseGroup: View {
    @Environment(ViewerController.self) private var viewer
    let compact: Bool

    var body: some View {
        ControlGroupBox(title: "Mouse (left button)") {
            ForEach(MouseTool.allCases) { tool in
                ToolButton(title: tool.title, symbol: tool.symbol, help: tool.help,
                           isSelected: viewer.tool == tool, compact: compact) {
                    viewer.setTool(tool)
                }
            }
        }
    }
}

private struct ImageGroup: View {
    @Environment(ViewerController.self) private var viewer
    let compact: Bool

    var body: some View {
        ControlGroupBox(title: "Image") {
            Menu {
                Section("CT presets (level / width)") {
                    ForEach(WindowPreset.ct) { preset in
                        Button("\(preset.name)  (\(Int(preset.center)) / \(Int(preset.width)))") {
                            viewer.applyWindow(center: preset.center, width: preset.width)
                        }
                    }
                }
            } label: {
                ToolLabel(title: "Window", symbol: "slider.horizontal.below.square.filled.and.square", compact: compact)
            }
            .menuStyle(.button)
            .buttonStyle(.plain)
            .menuIndicator(.hidden)
            .fixedSize()
            .help("Brightness/contrast presets for CT (bone, lung, soft tissue…). Right-drag also adjusts it.")
            .disabled(viewer.status != .ready)

            ToolButton(title: "Reset", symbol: "arrow.counterclockwise",
                       help: "Reset zoom, position, rotation and brightness (⇧⌘R)", isSelected: false,
                       compact: compact) {
                viewer.resetView()
            }
        }
    }
}

private struct GroupDivider: View {
    var body: some View {
        Divider().frame(height: 52)
    }
}

// MARK: - Building blocks

/// A titled group of tool buttons.
private struct ControlGroupBox<Content: View>: View {
    let title: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(spacing: 2) {
            HStack(spacing: 2) { content }
            Text(title.uppercased())
                .font(.system(size: 9, weight: .semibold))
                .foregroundStyle(.tertiary)
                .lineLimit(1)
        }
        .fixedSize()
    }
}

/// An icon with a caption underneath, used for all viewer controls.
struct ToolLabel: View {
    let title: String
    let symbol: String
    var isSelected = false
    var compact = false

    var body: some View {
        VStack(spacing: compact ? 2 : 3) {
            Image(systemName: symbol)
                .font(.system(size: compact ? 13 : 15))
                .frame(height: compact ? 16 : 18)
            Text(title)
                .font(compact ? .caption2 : .caption)
                .lineLimit(1)
        }
        .frame(minWidth: compact ? 40 : 50)
        .padding(.vertical, compact ? 3 : 4)
        .padding(.horizontal, compact ? 2 : 4)
        .foregroundStyle(isSelected ? Color.accentColor : .primary)
        .background(isSelected ? Color.accentColor.opacity(0.18) : .clear, in: .rect(cornerRadius: 7))
        .contentShape(.rect)
    }
}

struct ToolButton: View {
    let title: String
    let symbol: String
    let help: String
    let isSelected: Bool
    var compact = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ToolLabel(title: title, symbol: symbol, isSelected: isSelected, compact: compact)
        }
        .buttonStyle(.plain)
        .help(help)
        .accessibilityLabel(title)
        .accessibilityHint(help)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

//
//  StructuresSheet.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Bottom sheet listing a 3D model's structures by group: show all / relevant, isolate, show/hide, group opacity.
struct StructuresSheet: View {
    let model: StudyViewerModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List {
                Section {
                    HStack(spacing: Theme.Spacing.s) {
                        Button("Show all") { model.showAll() }
                            .frame(maxWidth: .infinity, minHeight: 44)
                        Button("Show relevant") { model.showRelevant() }
                            .frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.bordered)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())
                }

                ForEach(model.groups) { group in
                    Section(group.displayName) {
                        ForEach(model.studyModel.structures(in: group)) { structure in
                            StructureRow(structure: structure, isVisible: model.isVisible(structure),
                                         isolate: { model.isolate(structure) },
                                         toggle: { model.toggleVisibility(structure) })
                        }
                        if group.hasAdjustableOpacity {
                            OpacityRow(group: group, value: Binding(
                                get: { model.opacity[group] ?? 1 },
                                set: { model.setOpacity($0, for: group) }
                            ))
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .navigationTitle("Structures")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
        }
    }
}

// MARK: - Rows

/// Colour dot, name and volume (tap to isolate), and an eye button to show or hide.
private struct StructureRow: View {
    let structure: Structure
    let isVisible: Bool
    let isolate: () -> Void
    let toggle: () -> Void

    @ScaledMetric(relativeTo: .body) private var dotSize: CGFloat = 12

    var body: some View {
        HStack(spacing: Theme.Spacing.s) {
            Button(action: isolate) {
                HStack(spacing: Theme.Spacing.s) {
                    Circle()
                        .fill(Color(hex: structure.colorHex))
                        .frame(width: dotSize, height: dotSize)
                        .overlay(Circle().stroke(Theme.cardBorder, lineWidth: 1))
                    Text(structure.name)
                        .font(.body)
                        .foregroundStyle(isVisible ? .primary : .secondary)
                        .multilineTextAlignment(.leading)
                    Spacer(minLength: Theme.Spacing.s)
                    Text(structure.volumeText)
                        .font(.subheadline)
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                }
                .frame(minHeight: 44)
                .contentShape(Rectangle())
            }
            .accessibilityLabel("\(structure.name), \(structure.volumeSpoken), \(isVisible ? "shown" : "hidden")")
            .accessibilityHint("Shows only this structure")

            Button(action: toggle) {
                Image(systemName: isVisible ? "eye" : "eye.slash")
                    .font(.body)
                    .foregroundStyle(isVisible ? Theme.doctorAccent : .secondary)
                    .frame(minWidth: 44, minHeight: 44)
                    .contentShape(Rectangle())
            }
            .accessibilityLabel(isVisible ? "Hide \(structure.name)" : "Show \(structure.name)")
        }
        // Two buttons in one row: borderless keeps each tappable on its own.
        .buttonStyle(.borderless)
        .tint(.primary)
    }
}

/// Opacity slider for a whole group.
private struct OpacityRow: View {
    let group: StructureGroup
    @Binding var value: Double

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            HStack {
                Text("Opacity")
                    .font(.subheadline)
                Spacer()
                Text(value, format: .percent.precision(.fractionLength(0)))
                    .font(.subheadline)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            Slider(value: $value, in: 0...1)
                .tint(Theme.doctorAccent)
                .accessibilityLabel("\(group.displayName) opacity")
                .accessibilityValue(value.formatted(.percent.precision(.fractionLength(0))))
        }
        .padding(.vertical, Theme.Spacing.xs)
    }
}

private extension Structure {
    /// e.g. "42.6 ml", "1,580 ml", "0.11 ml".
    var volumeText: String {
        "\(volumeML.formatted(.number.precision(.fractionLength(0...2)))) ml"
    }

    var volumeSpoken: String {
        "\(volumeML.formatted(.number.precision(.fractionLength(0...2)))) millilitres"
    }
}

// MARK: - Previews

#Preview("Structures sheet") {
    let service = MockDataService()
    Color.clear
        .sheet(isPresented: .constant(true)) {
            if let studyModel = service.studyModels.first {
                StructuresSheet(model: StudyViewerModel(studyModel: studyModel))
                    .presentationDetents([.medium, .large])
            }
        }
}

#Preview("Structures sheet, dark mode") {
    let service = MockDataService()
    Color.clear
        .sheet(isPresented: .constant(true)) {
            if let studyModel = service.studyModels.first {
                StructuresSheet(model: StudyViewerModel(studyModel: studyModel))
                    .presentationDetents([.medium, .large])
            }
        }
        .preferredColorScheme(.dark)
}

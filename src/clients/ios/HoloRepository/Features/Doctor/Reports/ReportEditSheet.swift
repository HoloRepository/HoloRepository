//
//  ReportEditSheet.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Edits a report's findings and patient-friendly summary. Edited findings need review again until approved.
struct ReportEditSheet: View {
    let report: Report
    /// Called with the edited finding texts (keyed by finding id) and patient summary.
    let onSave: ([String: String], String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var findingTexts: [String: String]
    @State private var patientSummary: String

    init(report: Report, onSave: @escaping ([String: String], String) -> Void) {
        self.report = report
        self.onSave = onSave
        _findingTexts = State(initialValue: Dictionary(uniqueKeysWithValues: report.findings.map { ($0.id, $0.text) }))
        _patientSummary = State(initialValue: report.patientSummary)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    ForEach(Array(report.findings.enumerated()), id: \.element.id) { index, finding in
                        TextField("Finding \(index + 1)", text: binding(for: finding), axis: .vertical)
                            .font(.body)
                            .lineLimit(2...8)
                            .accessibilityLabel("Finding \(index + 1)")
                    }
                } header: {
                    Text("Findings")
                } footer: {
                    Text("Edited findings are marked \"Requires review\" until the report is approved.")
                }

                Section {
                    TextField("Patient-friendly summary", text: $patientSummary, axis: .vertical)
                        .font(.body)
                        .fontDesign(.rounded)
                        .lineLimit(4...12)
                        .accessibilityLabel("Patient-friendly summary")
                } header: {
                    Text("What the patient will see")
                } footer: {
                    Text("Use short sentences and everyday words (reading age about 9).")
                }
            }
            .navigationTitle("Edit report")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        onSave(findingTexts, patientSummary)
                        dismiss()
                    }
                    .disabled(!hasChanges)
                }
            }
        }
    }

    private var hasChanges: Bool {
        patientSummary != report.patientSummary
            || report.findings.contains { findingTexts[$0.id] != $0.text }
    }

    private func binding(for finding: Finding) -> Binding<String> {
        Binding(
            get: { findingTexts[finding.id] ?? finding.text },
            set: { findingTexts[finding.id] = $0 }
        )
    }
}

// MARK: - Previews

#Preview("Edit sheet") {
    Color.clear
        .sheet(isPresented: .constant(true)) {
            if let report = MockDataService().reports.first(where: { $0.id == "r-002" }) {
                ReportEditSheet(report: report) { _, _ in }
            }
        }
        .tint(Theme.doctorAccent)
}

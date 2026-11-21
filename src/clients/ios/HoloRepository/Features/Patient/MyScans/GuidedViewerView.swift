//
//  GuidedViewerView.swift
//
//
//  Created by Ng Xin Ning on 21/11/2026.
//

import SwiftUI

/// The patient's simple 3D view: dark background, only the structures that matter for this scan,
/// a plain-language explanation, and the name of whatever the patient taps.
/// No volumes, opacity sliders or structure lists: those are clinician tools.
struct GuidedViewerView: View {
    private let scan: PatientScan?
    @State private var viewerModel: StudyViewerModel?
    @State private var isShowingMore = false

    /// Looks the scan up in the patient's approved content, so nothing else can be opened.
    init(studyId: String, model: PatientContentModel) {
        let scan = model.scan(studyId: studyId)
        self.scan = scan
        _viewerModel = State(initialValue: scan.map {
            StudyViewerModel(studyModel: $0.studyModel, region: $0.study.bodyPart.lowercased(),
                             showingRelevantOnly: true)
        })
    }

    var body: some View {
        ZStack {
            Theme.viewerBackground
                .ignoresSafeArea()

            if let scan, let viewerModel {
                ModelWebView(bridge: viewerModel.bridge,
                             accessibilityText: "3D model of your \(scan.study.patientName.lowercased())")
                    .ignoresSafeArea()
                    .onAppear { viewerModel.loadInViewer() }
                    .overlay {
                        if !viewerModel.bridge.isReady {
                            ProgressView("Getting your 3D model ready…")
                        }
                    }
                    // TODO(March): markers the care team has approved and shared with the patient
                    // ("Share with patient" on the doctor's markers) appear here, read-only.
                    .safeAreaInset(edge: .top) { tappedCapsule(viewerModel.tappedStructure) }
                    .safeAreaInset(edge: .bottom) { explanationCard(scan: scan, tapped: viewerModel.tappedStructure) }
            } else {
                ContentUnavailableView("Scan not available", systemImage: "lock",
                                       description: Text("This scan hasn't been shared with you yet."))
            }
        }
        // The viewer is always dark, whatever the phone's setting.
        .environment(\.colorScheme, .dark)
        .navigationTitle(scan?.study.patientName ?? "3D view")
        .navigationBarTitleDisplayMode(.inline)
        .toolbarBackground(Theme.viewerBackground, for: .navigationBar)
        .toolbarColorScheme(.dark, for: .navigationBar)
        .toolbar(.hidden, for: .tabBar)
        .toolbar {
            ToolbarItemGroup(placement: .bottomBar) {
                Toggle(isOn: $isShowingMore) {
                    Label("Show more", systemImage: isShowingMore ? "eye.fill" : "eye")
                        .labelStyle(.titleAndIcon)
                }
                .toggleStyle(.button)
                .accessibilityLabel("Show more of your body")
                .accessibilityHint(isShowingMore ? "Now showing everything in the scan."
                                                 : "Now showing only the parts your doctor picked.")
                .disabled(viewerModel == nil)

                Spacer()

                Button {
                    viewerModel?.resetView()
                } label: {
                    Label("Reset view", systemImage: "arrow.counterclockwise")
                        .labelStyle(.titleAndIcon)
                }
                .accessibilityLabel("Reset view")
                .accessibilityHint("Turns the model back to the start.")
                .disabled(viewerModel == nil)
            }
        }
        .toolbarColorScheme(.dark, for: .bottomBar)
        .onChange(of: isShowingMore) { _, showMore in
            if showMore { viewerModel?.showAll() } else { viewerModel?.showRelevant() }
        }
        .onChange(of: viewerModel?.tappedStructure?.patientName) { _, name in
            if let name {
                AccessibilityNotification.Announcement(name).post()
            }
        }
    }

    @ViewBuilder
    private func tappedCapsule(_ structure: Structure?) -> some View {
        Group {
            if let structure {
                Text(structure.patientName)
                    .font(.headline)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, Theme.Spacing.m)
                    .padding(.vertical, Theme.Spacing.s)
                    .background(.ultraThinMaterial, in: Capsule())
                    .accessibilityLabel("You tapped: \(structure.patientName)")
                    .transition(.opacity)
            }
        }
        .padding(.top, Theme.Spacing.s)
        .padding(.horizontal, Theme.Spacing.m)
        .animation(.default, value: structure?.id)
    }

    private func explanationCard(scan: PatientScan, tapped: Structure?) -> some View {
        let title = tapped?.patientName ?? "What you're looking at"
        let text = tapped?.patientDescription
            ?? scan.study.patientDescription
            ?? "This is a 3D picture made from your scan."
        let content = VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            Text(title)
                .font(.headline)
            Text(text)
                .font(.body)
            if tapped == nil {
                Text("Drag to turn it. Pinch to zoom. Tap a part to see its name.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)

        // Short text sits as it is; very large text scrolls inside the card so the model stays visible.
        return ViewThatFits(in: .vertical) {
            content
            ScrollView { content }
        }
        .cardStyle()
        .frame(maxHeight: 260)
        .padding(Theme.Spacing.m)
    }
}

// MARK: - Previews

#Preview("Guided 3D view") {
    NavigationStack {
        GuidedViewerView(studyId: "s-001", model: .demo())
    }
    .patientStyle()
}

#Preview("Guided 3D view, not shared") {
    NavigationStack {
        GuidedViewerView(studyId: "s-003", model: .demo())
    }
    .patientStyle()
}

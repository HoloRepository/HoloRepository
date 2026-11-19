//
//  StudyViewerView.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Full-screen dark 3D viewer for one study, with the patient banner on top and Structures / Reset view below.
struct StudyViewerView: View {
    private let study: Study?
    private let patient: Patient?
    @State private var viewerModel: StudyViewerModel?
    @State private var isShowingStructures = false

    /// Looks the study up in the doctor's authorised records, so unauthorised studies can't be opened.
    /// Every CT study has a 3D model (segmented by NV-Segment-CT).
    init(studyId: String, records: PatientsModel) {
        let study = records.study(id: studyId)
        self.study = study
        patient = study.flatMap { records.patient(id: $0.patientId) }
        _viewerModel = State(initialValue: study.flatMap { study in
            records.studyModel(for: studyId).map {
                StudyViewerModel(studyModel: $0, region: study.bodyPart.lowercased())
            }
        })
    }

    var body: some View {
        ZStack(alignment: .top) {
            Theme.viewerBackground
                .ignoresSafeArea()

            if let study, let patient, let viewerModel {
                viewer(study: study, patient: patient, viewerModel: viewerModel)
                header(study: study, patient: patient)
            } else {
                ContentUnavailableView("Study not available", systemImage: "lock",
                                       description: Text("You aren't authorised to view this study."))
            }
        }
        // The viewer is always dark, whatever the phone's setting.
        .environment(\.colorScheme, .dark)
        .navigationTitle("3D viewer")
        .navigationBarTitleDisplayMode(.inline)
        .toolbarBackground(Theme.viewerBackground, for: .navigationBar)
        .toolbarColorScheme(.dark, for: .navigationBar)
        .toolbar(.hidden, for: .tabBar)
        .toolbar {
            ToolbarItemGroup(placement: .bottomBar) {
                // Title on the label itself: toolbars otherwise show icon-only buttons.
                Button {
                    isShowingStructures = true
                } label: {
                    Label("Structures", systemImage: "list.bullet")
                        .labelStyle(.titleAndIcon)
                }
                .disabled(viewerModel == nil)
                // Room for Markers and Patient view (next task).
                Spacer()
                Button {
                    viewerModel?.resetView()
                } label: {
                    Label("Reset view", systemImage: "arrow.counterclockwise")
                        .labelStyle(.titleAndIcon)
                }
                .disabled(viewerModel == nil)
            }
        }
        .toolbarColorScheme(.dark, for: .bottomBar)
        .sheet(isPresented: $isShowingStructures) {
            if let viewerModel {
                StructuresSheet(model: viewerModel)
                    .presentationDetents([.medium, .large])
                    .presentationBackgroundInteraction(.enabled(upThrough: .medium))
                    // Solid, so the colourful model behind doesn't make the list hard to read.
                    .presentationBackground(Theme.card)
            }
        }
        .onChange(of: viewerModel?.tappedStructureName) { _, name in
            if let name {
                AccessibilityNotification.Announcement(name).post()
            }
        }
    }

    private func viewer(study: Study, patient: Patient, viewerModel: StudyViewerModel) -> some View {
        ModelWebView(bridge: viewerModel.bridge,
                     accessibilityText: "3D model of \(patient.name)'s \(study.shortName)")
            .ignoresSafeArea()
            .onAppear { viewerModel.loadInViewer() }
            .overlay {
                if !viewerModel.bridge.isReady {
                    ProgressView("Loading 3D model…")
                }
            }
    }

    private func header(study: Study, patient: Patient) -> some View {
        VStack(spacing: Theme.Spacing.s) {
            PatientBanner(patient: patient, isCompact: true)

            VStack(spacing: Theme.Spacing.xs) {
                Text(study.description)
                    .font(.headline)
                Text(study.date, format: .dateTime.day().month(.wide).year())
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .multilineTextAlignment(.center)
            .padding(.horizontal, Theme.Spacing.m)
            .accessibilityElement(children: .combine)

            if let name = viewerModel?.tappedStructureName {
                Text(name)
                    .font(.subheadline.weight(.semibold))
                    .padding(.horizontal, Theme.Spacing.m)
                    .padding(.vertical, Theme.Spacing.xs)
                    .background(.ultraThinMaterial, in: Capsule())
                    .accessibilityLabel("Selected structure: \(name)")
                    .transition(.opacity)
            }
        }
        .animation(.default, value: viewerModel?.tappedStructureName)
    }
}

// MARK: - Previews

#Preview("Viewer") {
    NavigationStack {
        StudyViewerView(studyId: "s-001", records: .demo())
    }
    .tint(Theme.doctorAccent)
}

#Preview("Viewer, chest") {
    NavigationStack {
        StudyViewerView(studyId: "s-003", records: .demo())
    }
    .tint(Theme.doctorAccent)
}

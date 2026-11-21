//
//  StudyViewerModel.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation
import Observation

/// Which structures are shown and how opaque each group is, for one study's 3D model.
/// Every change is sent to the web viewer through the bridge.
@Observable
final class StudyViewerModel {
    let studyModel: StudyModel
    /// Body region for the viewer, e.g. "abdomen".
    let region: String
    let bridge: ViewerBridge

    private(set) var visibleIDs: Set<String>
    private(set) var opacity: [StructureGroup: Double]

    /// `showingRelevantOnly` starts with only the relevant structures visible (the patient's guided view).
    init(studyModel: StudyModel, region: String, bridge: ViewerBridge = ViewerBridge(),
         showingRelevantOnly: Bool = false) {
        self.studyModel = studyModel
        self.region = region
        self.bridge = bridge
        visibleIDs = Set(showingRelevantOnly ? studyModel.relevantStructureIDs : studyModel.structures.map(\.id))
        opacity = Dictionary(uniqueKeysWithValues: StructureGroup.allCases.map { ($0, 1.0) })
    }

    /// Groups that have structures, in display order (lesions first).
    var groups: [StructureGroup] {
        StructureGroup.allCases.filter { !studyModel.structures(in: $0).isEmpty }
    }

    /// Name of the structure last tapped in the 3D view, or nil once it's hidden (the viewer clears it too).
    var tappedStructureName: String? {
        tappedStructure?.name
    }

    /// The structure last tapped in the 3D view, or nil once it's hidden.
    var tappedStructure: Structure? {
        guard let id = bridge.tappedStructureID, visibleIDs.contains(id) else { return nil }
        return studyModel.structures.first { $0.id == id }
    }

    func isVisible(_ structure: Structure) -> Bool {
        visibleIDs.contains(structure.id)
    }

    // MARK: Actions

    /// Sends the model to the viewer. Queued by the bridge until the page is ready.
    func loadInViewer() {
        bridge.loadCase(studyModel, region: region, visibleIDs: orderedVisibleIDs)
        for group in StructureGroup.allCases where group.hasAdjustableOpacity && opacity[group] != 1 {
            bridge.setOpacity(group, opacity[group] ?? 1)
        }
    }

    func showAll() {
        setVisible(Set(studyModel.structures.map(\.id)))
    }

    func showRelevant() {
        setVisible(Set(studyModel.relevantStructureIDs))
    }

    /// Show only this structure.
    func isolate(_ structure: Structure) {
        setVisible([structure.id])
    }

    func toggleVisibility(_ structure: Structure) {
        var ids = visibleIDs
        if ids.contains(structure.id) {
            ids.remove(structure.id)
        } else {
            ids.insert(structure.id)
        }
        setVisible(ids)
    }

    func setOpacity(_ value: Double, for group: StructureGroup) {
        guard group.hasAdjustableOpacity else { return }
        opacity[group] = value
        bridge.setOpacity(group, value)
    }

    func resetView() {
        bridge.resetView()
    }

    // MARK: Private

    private func setVisible(_ ids: Set<String>) {
        visibleIDs = ids
        bridge.setVisible(orderedVisibleIDs)
    }

    /// Visible ids in the model's order, so the JS call is stable.
    private var orderedVisibleIDs: [String] {
        studyModel.structures.map(\.id).filter(visibleIDs.contains)
    }
}

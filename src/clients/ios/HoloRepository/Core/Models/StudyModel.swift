//
//  StudyModel.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation

/// The 3D model for a CT study: NV-Segment-CT segmentation. Every study has one.
struct StudyModel: Codable, Identifiable, Hashable, Sendable {
    let studyId: String
    /// Source case, e.g. "KiTS kidney case".
    let caseName: String
    let structures: [Structure]
    /// Structures shown by "Show relevant", e.g. the lesion and the organs and vessels around it.
    let relevantStructureIDs: [String]

    var id: String { studyId }

    func structures(in group: StructureGroup) -> [Structure] {
        structures.filter { $0.group == group }
    }
}

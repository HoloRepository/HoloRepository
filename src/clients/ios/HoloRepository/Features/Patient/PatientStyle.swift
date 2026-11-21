//
//  PatientStyle.swift
//
//
//  Created by Ng Xin Ning on 21/11/2026.
//

import SwiftUI

/// The patient app's look: teal tint, rounded text, and every text style one Dynamic Type size larger
/// than the phone's setting (still following the user's choice, and capped at the largest size).
private struct PatientStyle: ViewModifier {
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    func body(content: Content) -> some View {
        content
            .environment(\.dynamicTypeSize, dynamicTypeSize.oneSizeLarger)
            .tint(Theme.patientAccent)
            .fontDesign(.rounded)
    }
}

extension View {
    /// Apply once on the patient tab view (and on previews of patient screens).
    func patientStyle() -> some View { modifier(PatientStyle()) }
}

private extension DynamicTypeSize {
    var oneSizeLarger: DynamicTypeSize {
        let sizes = DynamicTypeSize.allCases
        guard let index = sizes.firstIndex(of: self) else { return self }
        return sizes[min(index + 1, sizes.count - 1)]
    }
}

/// Places a patient screen can push to. Home and My Scans share them.
enum PatientRoute: Hashable {
    case scan(studyId: String)
    case viewer(studyId: String)
    case report(reportId: String)
    case appointment(appointmentId: String)
}

extension View {
    /// Destinations for `PatientRoute` links inside a patient tab's NavigationStack.
    func patientDestinations(model: PatientContentModel) -> some View {
        navigationDestination(for: PatientRoute.self) { route in
            switch route {
            case .scan(let studyId):
                ScanOverviewView(studyId: studyId, model: model)
            case .viewer(let studyId):
                GuidedViewerView(studyId: studyId, model: model)
            case .report(let reportId):
                PatientReportPlaceholderView(report: model.report(id: reportId))
            case .appointment(let appointmentId):
                AppointmentPlaceholderView(appointment: model.appointment(id: appointmentId))
            }
        }
    }
}

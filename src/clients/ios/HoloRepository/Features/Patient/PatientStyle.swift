//
//  PatientStyle.swift
//
//
//  Created by Ng Xin Ning on 21/11/2026.
//

import SwiftUI

/// The patient app's look: teal tint and rounded text, at the phone's own Dynamic Type size.
private struct PatientStyle: ViewModifier {
    func body(content: Content) -> some View {
        content
            .tint(Theme.patientAccent)
            .fontDesign(.rounded)
    }
}

extension View {
    /// Apply once on the patient tab view (and on previews of patient screens).
    func patientStyle() -> some View { modifier(PatientStyle()) }
}

/// Places a patient screen can push to. Home and My Scans share them.
enum PatientRoute: Hashable {
    case scan(studyId: String)
    case viewer(studyId: String)
    case report(reportId: String)
    case appointment(appointmentId: String)
    case appointments
    case search
    case comingSoon(ComingSoonFeature)
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
            case .appointments:
                AppointmentsView()
            case .search:
                PatientSearchView(model: model)
            case .comingSoon(let feature):
                ComingSoonView(feature: feature)
            }
        }
    }
}

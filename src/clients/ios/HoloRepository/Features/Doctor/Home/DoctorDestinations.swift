//
//  DoctorDestinations.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

extension View {
    /// Destinations for `DoctorRoute` in the Home and Video Meeting stacks. Every screen looks its data up
    /// in the doctor's authorised records, so nothing unauthorised can be opened.
    func doctorDestinations(home: DoctorHomeModel, records: PatientsModel,
                            push: @escaping (DoctorRoute) -> Void) -> some View {
        navigationDestination(for: DoctorRoute.self) { route in
            switch route {
            case .notifications:
                NotificationsView(model: home)
            case .tasks(let filter):
                TaskListView(model: home, filter: filter, push: push)
            case .session(let sessionId):
                MDTSessionDetailView(sessionId: sessionId, model: home, records: records, push: push)
            case .viewer(let studyId):
                StudyViewerView(studyId: studyId, records: records)
            case .report(let reportId):
                ReportDetailView(reportId: reportId, records: records) { studyId in
                    push(.viewer(studyId: studyId))
                }
            }
        }
    }
}

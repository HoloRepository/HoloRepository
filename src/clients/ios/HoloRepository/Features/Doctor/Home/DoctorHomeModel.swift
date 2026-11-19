//
//  DoctorHomeModel.swift
//  
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation
import Observation

/// State for the doctor Home screen, loaded from `MockDataService`.
@Observable
final class DoctorHomeModel {
    private(set) var tasks: [DoctorTask] = []
    private(set) var upcomingSessions: [MDTSession] = []
    private(set) var activeSession: MDTSession?
    private(set) var activity: [ActivityItem] = []
    private(set) var notifications: [AppNotification] = []
    private(set) var hasLoaded = false

    private var patientNames: [String: String] = [:]

    var unreadNotificationCount: Int {
        notifications.filter { !$0.isRead }.count
    }

    func load(from service: MockDataService) {
        tasks = service.doctorTasks
        activeSession = service.mdtSessions.first { $0.status == .active }
        upcomingSessions = service.mdtSessions
            .filter { $0.status == .scheduled }
            .sorted { $0.startTime < $1.startTime }
        activity = service.activityItems.sorted { $0.timestamp > $1.timestamp }
        notifications = service.notifications.sorted { $0.timestamp > $1.timestamp }
        patientNames = Dictionary(uniqueKeysWithValues: service.patients.map { ($0.id, $0.name) })
        hasLoaded = true
    }

    func patientName(for id: String?) -> String? {
        guard let id else { return nil }
        return patientNames[id]
    }

    /// "Good morning, Dr Smith": greeting by time of day, then title and surname.
    static func greeting(for name: String?, at date: Date = .now, calendar: Calendar = .current) -> String {
        let hour = calendar.component(.hour, from: date)
        let partOfDay = switch hour {
        case 5..<12: "morning"
        case 12..<18: "afternoon"
        default: "evening"
        }
        guard let name, !name.isEmpty else { return "Good \(partOfDay)" }

        let words = name.split(separator: " ")
        let shortName = if words.first == "Dr", let surname = words.last, words.count > 1 {
            "Dr \(surname)"
        } else {
            name
        }
        return "Good \(partOfDay), \(shortName)"
    }

    /// A model with no items, for previews of the empty state.
    static func empty() -> DoctorHomeModel {
        let model = DoctorHomeModel()
        model.hasLoaded = true
        return model
    }
}

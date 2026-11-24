//
//  DoctorHomeModel.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import Foundation
import Observation

/// The four task filters, in display order. "Urgent" is a priority (any status except completed);
/// the others are statuses.
enum TaskFilter: String, CaseIterable, Identifiable, Hashable {
    case notStarted, inProgress, urgent, completed

    var id: Self { self }

    var title: String {
        switch self {
        case .notStarted: "Not started"
        case .inProgress: "In progress"
        case .urgent: "Urgent"
        case .completed: "Completed"
        }
    }

    var emptyMessage: String {
        switch self {
        case .notStarted: "No tasks waiting to be started."
        case .inProgress: "No tasks in progress."
        case .urgent: "No urgent tasks. Nice work."
        case .completed: "No completed tasks yet."
        }
    }

    func includes(_ task: DoctorTask) -> Bool {
        switch self {
        case .notStarted: task.status == .notStarted
        case .inProgress: task.status == .inProgress
        case .urgent: task.isUrgent && task.status != .completed
        case .completed: task.status == .completed
        }
    }
}

/// State for the doctor Home and Video Meeting tabs, loaded from `MockDataService`.
/// Holds only tasks, sessions and notifications about patients the doctor is authorised to see.
@Observable
final class DoctorHomeModel {
    /// Urgent first, then by due date.
    private(set) var tasks: [DoctorTask] = []
    /// Not yet finished, soonest first. Use `activeSession(at:)` / `upcomingSessions(at:)`.
    private(set) var sessions: [MDTSession] = []
    /// Newest first.
    private(set) var notifications: [AppNotification] = []
    private(set) var hasLoaded = false

    private var patientNames: [String: String] = [:]
    private var studies: [String: Study] = [:]

    var unreadNotificationCount: Int {
        notifications.filter { !$0.isRead }.count
    }

    func load(from service: MockDataService, doctorId: String?, now: Date = .now) {
        // Authorisation first: nothing about other patients reaches Home.
        let patients = service.patients.filter { patient in
            doctorId.map { patient.authorisedDoctorIds.contains($0) } ?? false
        }
        let allowed = Set(patients.map(\.id))
        patientNames = Dictionary(uniqueKeysWithValues: patients.map { ($0.id, $0.name) })
        studies = Dictionary(uniqueKeysWithValues: service.studies.filter { allowed.contains($0.patientId) }
            .map { ($0.id, $0) })

        tasks = service.doctorTasks
            .filter { allowed.contains($0.patientId) }
            .sorted { lhs, rhs in
                if lhs.isUrgent != rhs.isUrgent { return lhs.isUrgent }
                return (lhs.dueDate ?? .distantFuture) < (rhs.dueDate ?? .distantFuture)
            }
        sessions = service.mdtSessions
            .filter { allowed.contains($0.patientId) && $0.liveStatus(at: now) != .completed }
            .sorted { $0.startTime < $1.startTime }
        notifications = service.notifications
            .filter { $0.patientId.map(allowed.contains) ?? true }
            .sorted { $0.timestamp > $1.timestamp }
        hasLoaded = true
    }

    // MARK: Tasks

    func tasks(for filter: TaskFilter) -> [DoctorTask] {
        tasks.filter(filter.includes)
    }

    // MARK: Sessions

    /// The session that is live now, or else one starting within 30 minutes.
    func activeSession(at now: Date = .now) -> MDTSession? {
        sessions.first { $0.liveStatus(at: now) == .live }
            ?? sessions.first { $0.liveStatus(at: now) == .startingSoon }
    }

    /// Scheduled sessions (not live or starting soon), soonest first.
    func upcomingSessions(at now: Date = .now) -> [MDTSession] {
        let active = activeSession(at: now)?.id
        return sessions.filter { $0.id != active && $0.liveStatus(at: now) != .completed }
    }

    func session(id: String) -> MDTSession? {
        sessions.first { $0.id == id }
    }

    /// The session's cases, or just its main patient's latest study if none are listed.
    func cases(for session: MDTSession) -> [MDTCase] {
        if let cases = session.cases { return cases }
        let study = studies.values.filter { $0.patientId == session.patientId }.max { $0.date < $1.date }
        return study.map { [MDTCase(patientId: session.patientId, studyId: $0.id)] } ?? []
    }

    // MARK: Lookups

    func patientName(for id: String?) -> String? {
        guard let id else { return nil }
        return patientNames[id]
    }

    /// e.g. "CT abdomen".
    func studyName(for id: String?) -> String? {
        guard let id else { return nil }
        return studies[id]?.shortName
    }

    // MARK: Greeting

    /// "Good morning, Dr Sarah": greeting by time of day, then title and first name.
    static func greeting(for name: String?, at date: Date = .now, calendar: Calendar = .current) -> String {
        let hour = calendar.component(.hour, from: date)
        let partOfDay = switch hour {
        case 5..<12: "morning"
        case 12..<18: "afternoon"
        default: "evening"
        }
        guard let name, !name.isEmpty else { return "Good \(partOfDay)" }

        let words = name.split(separator: " ")
        let shortName = if words.first == "Dr", words.count > 1 {
            "Dr \(words[1])"
        } else {
            String(words.first ?? "")
        }
        return "Good \(partOfDay), \(shortName)"
    }

    // MARK: Previews

    /// Loaded with the mock data for the demo doctor.
    static func demo() -> DoctorHomeModel {
        let model = DoctorHomeModel()
        let service = MockDataService()
        model.load(from: service, doctorId: service.doctorUser.id)
        return model
    }

    /// The demo data without the live / starting-soon session.
    static func noActiveSession() -> DoctorHomeModel {
        let model = demo()
        model.sessions.removeAll { [.live, .startingSoon].contains($0.liveStatus()) }
        return model
    }

    /// A model with no items, for previews of empty states.
    static func empty() -> DoctorHomeModel {
        let model = DoctorHomeModel()
        model.hasLoaded = true
        return model
    }
}

// MARK: - Task navigation

/// Screens pushed from the doctor Home, the task list and the Video Meeting tab.
enum DoctorRoute: Hashable {
    case notifications
    case tasks(TaskFilter)
    case session(sessionId: String)
    case viewer(studyId: String)
    case report(reportId: String)
}

extension DoctorTask {
    /// Review scan / annotations → 3D viewer; approve report → report detail; MDT tasks → session detail.
    var route: DoctorRoute? {
        switch kind {
        case .reviewScan, .reviewAnnotations: studyId.map { .viewer(studyId: $0) }
        case .approveReport: reportId.map { .report(reportId: $0) }
        case .acceptMDTInvitation, .prepareMDTCase: sessionId.map { .session(sessionId: $0) }
        }
    }
}

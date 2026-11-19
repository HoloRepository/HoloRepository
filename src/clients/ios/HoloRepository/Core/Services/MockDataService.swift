//
//  MockDataService.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import Foundation

/// Demo data for the December prototype, decoded from the JSON files in Resources/MockData.
/// Those files are copies of src/clients/shared/mock-data (shared with Android); keep them identical.
/// All names and numbers are fake. No network calls. A DaemonService with the same interface replaces this later.
///
/// Notes on the data:
/// - p-001 and p-002 are assigned to the demo doctor; p-003 is a colleague's patient the demo doctor may view.
///   p-004 is not authorised for the demo doctor and must never appear in their app.
/// - The active MDT session, upcoming MDTs, activity and notifications use relative dates
///   so the demo always shows a live session, future meetings and "2 h ago" style times.
struct MockDataService: Sendable {
    let doctorUser: User
    let patientUser: User
    let patients: [Patient]
    let studies: [Study]
    let reports: [Report]
    let appointments: [Appointment]
    let doctorTasks: [DoctorTask]
    let mdtSessions: [MDTSession]
    let activityItems: [ActivityItem]
    let notifications: [AppNotification]
    /// 3D models for the studies that have one.
    let studyModels: [StudyModel]

    /// Decodes every file now; relative dates are resolved against `now`.
    init(bundle: Bundle = .main, now: Date = .now) {
        let decoder = MockDataDecoder(bundle: bundle, now: now)
        let users: [User] = decoder.decode("users")
        doctorUser = decoder.require(users.first { $0.role == .doctor }, "a doctor in users.json")
        patientUser = decoder.require(users.first { $0.role == .patient }, "a patient in users.json")
        patients = decoder.decode("patients")
        studies = decoder.decode("studies")
        reports = decoder.decode("reports")
        appointments = decoder.decode("appointments")
        doctorTasks = decoder.decode("tasks")
        mdtSessions = decoder.decode("mdt_sessions")
        activityItems = decoder.decode("activity")
        notifications = decoder.decode("notifications")
        studyModels = decoder.decode("study_models")
    }

    /// The demo account to sign in as for a role (any credentials are accepted).
    func demoUser(for role: UserRole) -> User {
        switch role {
        case .doctor: doctorUser
        case .patient: patientUser
        }
    }
}

// MARK: - Decoding

/// Loads mock JSON from the bundle. Dates may be written as:
/// - a fixed local date and time: `"2026-11-10T09:00"`
/// - days from today at a local time: `{ "daysFromNow": 2, "time": "14:00" }`
/// - minutes from now: `{ "minutesFromNow": -15 }`
/// Mock data ships with the app, so a missing or malformed file is a programming error and stops the app.
private struct MockDataDecoder {
    let bundle: Bundle
    let now: Date
    private let calendar = Calendar(identifier: .gregorian)

    init(bundle: Bundle, now: Date) {
        self.bundle = bundle
        self.now = now
    }

    func decode<T: Decodable>(_ name: String) -> T {
        let url = bundle.url(forResource: name, withExtension: "json")
            ?? bundle.url(forResource: name, withExtension: "json", subdirectory: "MockData")
        guard let url else {
            fatalError("Mock data file \(name).json is missing from the app bundle.")
        }
        do {
            let decoder = JSONDecoder()
            let now = now
            let calendar = calendar
            decoder.dateDecodingStrategy = .custom { decoder in
                try MockDate.decode(from: decoder, now: now, calendar: calendar)
            }
            return try decoder.decode(T.self, from: Data(contentsOf: url))
        } catch {
            fatalError("Could not decode \(name).json: \(error)")
        }
    }

    func require<T>(_ value: T?, _ description: String) -> T {
        guard let value else { fatalError("Mock data needs \(description).") }
        return value
    }
}

/// The relative date object form.
private struct RelativeMockDate: Decodable {
    var daysFromNow: Int?
    /// "HH:mm", local time. Used with `daysFromNow`.
    var time: String?
    var minutesFromNow: Int?
}

private enum MockDate {
    static func decode(from decoder: Decoder, now: Date, calendar: Calendar) throws -> Date {
        let container = try decoder.singleValueContainer()

        if let text = try? container.decode(String.self) {
            guard let date = fixedDate(text, calendar: calendar) else {
                throw DecodingError.dataCorruptedError(in: container,
                    debugDescription: "Expected a date like \"2026-11-10T09:00\", got \"\(text)\".")
            }
            return date
        }

        let relative = try container.decode(RelativeMockDate.self)
        if let minutes = relative.minutesFromNow {
            return now.addingTimeInterval(TimeInterval(minutes * 60))
        }
        if let days = relative.daysFromNow {
            let (hour, minute) = try time(relative.time ?? "09:00", in: container)
            let day = calendar.date(byAdding: .day, value: days, to: calendar.startOfDay(for: now)) ?? now
            return calendar.date(bySettingHour: hour, minute: minute, second: 0, of: day) ?? day
        }
        throw DecodingError.dataCorruptedError(in: container,
            debugDescription: "A relative date needs \"minutesFromNow\" or \"daysFromNow\".")
    }

    /// "yyyy-MM-ddTHH:mm" in the device's time zone.
    private static func fixedDate(_ text: String, calendar: Calendar) -> Date? {
        let parts = text.split(separator: "T")
        guard parts.count == 2 else { return nil }
        let day = parts[0].split(separator: "-").compactMap { Int($0) }
        let clock = parts[1].split(separator: ":").compactMap { Int($0) }
        guard day.count == 3, clock.count == 2 else { return nil }
        let components = DateComponents(year: day[0], month: day[1], day: day[2], hour: clock[0], minute: clock[1])
        return calendar.date(from: components)
    }

    private static func time(_ text: String, in container: SingleValueDecodingContainer) throws -> (Int, Int) {
        let clock = text.split(separator: ":").compactMap { Int($0) }
        guard clock.count == 2 else {
            throw DecodingError.dataCorruptedError(in: container,
                debugDescription: "Expected a time like \"14:00\", got \"\(text)\".")
        }
        return (clock[0], clock[1])
    }
}

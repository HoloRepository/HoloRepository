//
//  DoctorHomeView.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Doctor Home, top to bottom: greeting and date with bell and avatar, the active MDT session,
/// pending tasks with four filters, and upcoming meetings.
struct DoctorHomeView: View {
    let model: DoctorHomeModel
    /// Authorised clinical records (viewer, reports, MDT cases).
    let records: PatientsModel
    /// Switches the doctor tab bar ("See more" → Video Meeting).
    var onSelectTab: (DoctorTab) -> Void = { _ in }

    @Environment(AppState.self) private var appState
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @State private var path: [DoctorRoute] = []
    @State private var taskFilter: TaskFilter = .notStarted
    @State private var isShowingProfile = false

    /// Tasks and meetings shown on Home before "See all".
    private let previewLimit = 3

    private var isLargeText: Bool { dynamicTypeSize.isAccessibilitySize }

    var body: some View {
        NavigationStack(path: $path) {
            // Re-render every minute so the greeting, date and session status follow the clock.
            TimelineView(.everyMinute) { context in
                ScrollView {
                    VStack(alignment: .leading, spacing: Theme.Spacing.l) {
                        header(now: context.date)
                        activeSession(now: context.date)
                        pendingTasks
                        upcomingMeetings(now: context.date)
                    }
                    .padding(.horizontal, Theme.Spacing.m)
                    .padding(.bottom, Theme.Spacing.l)
                }
            }
            .background(Theme.background)
            .toolbar(.hidden, for: .navigationBar)
            .doctorDestinations(home: model, records: records) { path.append($0) }
            .sheet(isPresented: $isShowingProfile) {
                DoctorProfileView()
            }
        }
    }

    // MARK: 1. Header

    private func header(now: Date) -> some View {
        let greeting = DoctorHomeModel.greeting(for: appState.currentUser?.name, at: now)
        let text = VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            Text("\(greeting) 👋")
                .font(.title2.weight(.semibold))
                .accessibilityLabel(greeting)
                .accessibilityAddTraits(.isHeader)
            Text(now, format: .dateTime.weekday(.wide).day().month(.wide).year())
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .fixedSize(horizontal: false, vertical: true)
        .frame(maxWidth: .infinity, alignment: .leading)

        return Group {
            if isLargeText {
                // Icons above the greeting so the text gets the full width.
                VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                    HStack { Spacer(); headerIcons }
                    text
                }
            } else {
                HStack(alignment: .top, spacing: Theme.Spacing.s) {
                    text
                    headerIcons
                }
            }
        }
        .padding(.top, Theme.Spacing.s)
    }

    private var headerIcons: some View {
        HStack(spacing: Theme.Spacing.xs) {
            Button {
                path.append(.notifications)
            } label: {
                Image(systemName: "bell")
                    .font(.title3)
                    .frame(minWidth: 44, minHeight: 44)
                    .overlay(alignment: .topTrailing) {
                        if model.unreadNotificationCount > 0 {
                            Circle()
                                .fill(Theme.doctorAccent)
                                .frame(width: Theme.Spacing.s + 2, height: Theme.Spacing.s + 2)
                                .overlay(Circle().stroke(Theme.background, lineWidth: 2))
                                .offset(x: -Theme.Spacing.s, y: Theme.Spacing.s)
                        }
                    }
            }
            .accessibilityLabel(model.unreadNotificationCount > 0
                                ? "Notifications, \(model.unreadNotificationCount) unread"
                                : "Notifications")

            Button {
                isShowingProfile = true
            } label: {
                InitialsAvatar(initials)
            }
            .accessibilityLabel("Profile")
        }
    }

    private var initials: String {
        // "Dr Sarah Smith" → "SS".
        let words = (appState.currentUser?.name ?? "").split(separator: " ").filter { $0 != "Dr" }
        return words.prefix(2).compactMap(\.first).map(String.init).joined()
    }

    // MARK: 2. Active MDT session

    private func activeSession(now: Date) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            sectionHeading("Active MDT session")
            if let session = model.activeSession(at: now) {
                activeSessionCard(session, now: now)
            } else {
                Label("No MDT session running or starting soon.", systemImage: "video.slash")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .frame(minHeight: 44)
                    .cardStyle()
            }
        }
    }

    private func activeSessionCard(_ session: MDTSession, now: Date) -> some View {
        let status = session.liveStatus(at: now)
        let caseCount = model.cases(for: session).count
        return VStack(alignment: .leading, spacing: Theme.Spacing.m) {
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                HStack(alignment: .firstTextBaseline) {
                    if let specialty = session.specialty {
                        Text(specialty.uppercased())
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(Theme.doctorAccent)
                    }
                    Spacer(minLength: Theme.Spacing.s)
                    MDTStatusLabel(status: status)
                }
                Text(session.caseName)
                    .font(.title3.weight(.semibold))
                Text(session.startTime, format: .dateTime.weekday(.wide).day().month(.wide))
                    .font(.subheadline)
                Text(session.timeRangeText)
                    .font(.subheadline)
                    .monospacedDigit()
                Label("\(session.participantCount) participants · \(caseCount == 1 ? "1 case" : "\(caseCount) cases")",
                      systemImage: "person.3")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .fixedSize(horizontal: false, vertical: true)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("\(session.accessibilityText(at: now)), \(session.participantCount) participants, \(caseCount) \(caseCount == 1 ? "case" : "cases")")

            Button {
                path.append(.session(sessionId: session.id))
            } label: {
                Text(status == .live ? "Join session" : "View session")
                    .font(.headline)
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.borderedProminent)
            .buttonBorderShape(.roundedRectangle(radius: Theme.Radius.button))
            .tint(Theme.doctorAccent)
            .accessibilityLabel("\(status == .live ? "Join" : "View") session, \(session.caseName)")
        }
        .cardStyle()
        // The most prominent card on Home: an accent outline on top of the usual card border.
        .overlay(RoundedRectangle(cornerRadius: Theme.Radius.card).stroke(Theme.doctorAccent, lineWidth: 1.5))
    }

    // MARK: 3. Pending tasks

    private var pendingTasks: some View {
        let tasks = model.tasks(for: taskFilter)
        return VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            sectionHeading("Pending tasks", link: "See all", linkLabel: "See all tasks") {
                path.append(.tasks(taskFilter))
            }
            TaskFilterBar(selection: $taskFilter) { model.tasks(for: $0).count }

            VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                if tasks.isEmpty {
                    Label(taskFilter.emptyMessage, systemImage: "tray")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .frame(minHeight: 44)
                }
                ForEach(Array(tasks.prefix(previewLimit).enumerated()), id: \.element.id) { index, task in
                    if index > 0 { Divider() }
                    Button {
                        if let route = task.route { path.append(route) }
                    } label: {
                        TaskRow(task: task, patientName: model.patientName(for: task.patientId),
                                studyName: model.studyName(for: task.studyId))
                    }
                    .buttonStyle(.plain)
                }
            }
            .cardStyle()
        }
    }

    // MARK: 4. Upcoming meetings

    private func upcomingMeetings(now: Date) -> some View {
        let sessions = model.upcomingSessions(at: now)
        return VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            sectionHeading("Upcoming meetings", link: "See more", linkLabel: "See more meetings") {
                onSelectTab(.meetings)
            }
            VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                if sessions.isEmpty {
                    Text("No upcoming MDT sessions.")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .frame(minHeight: 44)
                }
                ForEach(Array(sessions.prefix(previewLimit).enumerated()), id: \.element.id) { index, session in
                    if index > 0 { Divider() }
                    Button {
                        path.append(.session(sessionId: session.id))
                    } label: {
                        MeetingRow(session: session, now: now)
                    }
                    .buttonStyle(.plain)
                }
            }
            .cardStyle()
        }
    }

    // MARK: Helpers

    private func sectionHeading(_ title: String) -> some View {
        Text(title)
            .font(.headline)
            .accessibilityAddTraits(.isHeader)
    }

    /// Heading with a "See all →" style link on the right (below it at accessibility sizes).
    private func sectionHeading(_ title: String, link: String, linkLabel: String,
                                action: @escaping () -> Void) -> some View {
        let layout = isLargeText
            ? AnyLayout(VStackLayout(alignment: .leading, spacing: 0))
            : AnyLayout(HStackLayout(alignment: .firstTextBaseline))
        return layout {
            sectionHeading(title)
            if !isLargeText { Spacer() }
            Button(action: action) {
                HStack(spacing: Theme.Spacing.xs) {
                    Text(link)
                    Image(systemName: "arrow.right")
                }
                .font(.subheadline.weight(.semibold))
                .frame(minHeight: 44)
            }
            .accessibilityLabel(linkLabel)
        }
    }
}

// MARK: - Previews

#Preview("Doctor Home") {
    DoctorHomeView(model: .demo(), records: .demo())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

#Preview("Doctor Home, dark") {
    DoctorHomeView(model: .demo(), records: .demo())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
        .preferredColorScheme(.dark)
}

#Preview("No active session") {
    DoctorHomeView(model: .noActiveSession(), records: .demo())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

#Preview("Empty") {
    DoctorHomeView(model: .empty(), records: .demo())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

#Preview("Largest text") {
    DoctorHomeView(model: .demo(), records: .demo())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
        .dynamicTypeSize(.accessibility5)
}

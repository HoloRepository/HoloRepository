//
//  DoctorHomeView.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Doctor Home: greeting, active MDT session and summary cards (count + up to 3 items each).
struct DoctorHomeView: View {
    @Environment(AppState.self) private var appState
    @State private var model: DoctorHomeModel
    @State private var isShowingProfile = false
    @State private var isShowingJoinAlert = false

    /// Called with a patient id when a patient name is tapped (switches to the Patients tab).
    /// When nil, tapping a patient name pushes a placeholder instead.
    private let onOpenPatient: ((String) -> Void)?

    /// Items shown per summary card before "See all".
    private let previewLimit = 3

    init(model: DoctorHomeModel = DoctorHomeModel(), onOpenPatient: ((String) -> Void)? = nil) {
        _model = State(initialValue: model)
        self.onOpenPatient = onOpenPatient
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Theme.Spacing.m) {
                    header

                    if let session = model.activeSession {
                        activeSessionCard(session)
                    }

                    tasksCard
                    upcomingSessionsCard
                    activityCard
                    notificationsCard
                }
                .padding(Theme.Spacing.m)
            }
            .background(Theme.background)
            .navigationTitle("Home")
            .toolbar(.hidden, for: .navigationBar)
            .navigationDestination(for: HomeDestination.self) { destination in
                HomePlaceholderView(title: destination.title(patientName: model.patientName(for:)))
            }
            .sheet(isPresented: $isShowingProfile) {
                DoctorProfileView()
            }
            .alert("Coming in March", isPresented: $isShowingJoinAlert) {
                Button("OK", role: .cancel) {}
            } message: {
                Text("Joining live MDT sessions will be available in a later release.")
            }
            .task {
                if !model.hasLoaded {
                    model.load(from: appState.dataService)
                }
            }
        }
    }

    // MARK: Header

    private var header: some View {
        HStack(alignment: .top, spacing: Theme.Spacing.s) {
            // Re-render every minute so the greeting follows the time of day.
            TimelineView(.everyMinute) { context in
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    Text(DoctorHomeModel.greeting(for: appState.currentUser?.name, at: context.date))
                        .font(.largeTitle.weight(.semibold))
                        .accessibilityAddTraits(.isHeader)
                    Text(context.date, format: .dateTime.weekday(.wide).day().month(.wide).year())
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            }

            Spacer(minLength: Theme.Spacing.s)

            Button {
                isShowingProfile = true
            } label: {
                Image(systemName: "person.circle")
                    .font(.title2)
                    .frame(minWidth: 44, minHeight: 44)
            }
            .accessibilityLabel("Profile")
        }
    }

    // MARK: Active MDT session

    private func activeSessionCard(_ session: MDTSession) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            Label("Active MDT session", systemImage: "person.3")
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(.secondary)

            Text(session.caseName)
                .font(.headline)

            Text("Started \(session.startTime, format: .dateTime.hour().minute()) · \(session.participantCount) participants")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .monospacedDigit()

            Button {
                isShowingJoinAlert = true
            } label: {
                Text("Join session")
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.doctorAccent)
            .accessibilityLabel("Join session, \(session.caseName)")
        }
        .cardStyle()
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Active MDT session, \(session.caseName)")
    }

    // MARK: Summary cards

    private var tasksCard: some View {
        SummaryCard(title: "Pending tasks", systemImage: "checklist", count: model.tasks.count,
                    emptyMessage: "No pending tasks. You're all caught up.", seeAll: .allTasks) {
            ForEach(model.tasks.prefix(previewLimit)) { task in
                HomeItemRow(destination: .task(task), title: task.title,
                            patientId: task.patientId, patientName: model.patientName(for: task.patientId),
                            onOpenPatient: onOpenPatient) {
                    Text(task.kind.label)
                } accessory: {
                    if let state = task.verificationState {
                        StatusBadge(state: state)
                    }
                }
            }
        }
    }

    private var upcomingSessionsCard: some View {
        SummaryCard(title: "Upcoming MDT consultations", systemImage: "calendar",
                    count: model.upcomingSessions.count,
                    emptyMessage: "No upcoming MDT consultations.", seeAll: .allSessions) {
            ForEach(model.upcomingSessions.prefix(previewLimit)) { session in
                HomeItemRow(destination: .session(session), title: session.caseName,
                            patientId: session.patientId, patientName: model.patientName(for: session.patientId),
                            onOpenPatient: onOpenPatient) {
                    Text(session.startTime,
                         format: .dateTime.weekday(.abbreviated).day().month(.abbreviated).hour().minute())
                        .monospacedDigit()
                }
            }
        }
    }

    private var activityCard: some View {
        SummaryCard(title: "Recent activity", systemImage: "clock.arrow.circlepath",
                    count: model.activity.count,
                    emptyMessage: "No recent activity.", seeAll: .allActivity) {
            ForEach(model.activity.prefix(previewLimit)) { item in
                HomeItemRow(destination: .activity(item), title: item.title, systemImage: item.kind.systemImage,
                            patientId: item.patientId, patientName: model.patientName(for: item.patientId),
                            onOpenPatient: onOpenPatient) {
                    Text(item.timestamp, format: .relative(presentation: .numeric, unitsStyle: .abbreviated))
                        .monospacedDigit()
                }
            }
        }
    }

    private var notificationsCard: some View {
        SummaryCard(title: "Notifications", systemImage: "bell", count: model.notifications.count,
                    emptyMessage: "No new notifications.", seeAll: .allNotifications) {
            ForEach(model.notifications.prefix(previewLimit)) { notification in
                HomeItemRow(destination: .notification(notification), title: notification.title,
                            systemImage: notification.kind.systemImage,
                            patientId: notification.patientId,
                            patientName: model.patientName(for: notification.patientId),
                            onOpenPatient: onOpenPatient) {
                    Text(notification.timestamp, format: .relative(presentation: .numeric, unitsStyle: .abbreviated))
                        .monospacedDigit()
                } accessory: {
                    if !notification.isRead {
                        Circle()
                            .fill(Theme.doctorAccent)
                            .frame(width: Theme.Spacing.s, height: Theme.Spacing.s)
                            .accessibilityLabel("Unread")
                    }
                }
            }
        }
    }
}

// MARK: - Navigation

/// Screens pushed from Home. All are placeholders for now.
enum HomeDestination: Hashable {
    case allTasks, allSessions, allActivity, allNotifications
    case task(DoctorTask)
    case session(MDTSession)
    case activity(ActivityItem)
    case notification(AppNotification)
    case patient(String)

    func title(patientName: (String?) -> String?) -> String {
        switch self {
        case .allTasks: "Pending tasks"
        case .allSessions: "MDT consultations"
        case .allActivity: "Recent activity"
        case .allNotifications: "Notifications"
        case .task(let task): task.title
        case .session(let session): session.caseName
        case .activity(let item): item.title
        case .notification(let notification): notification.title
        case .patient(let id): patientName(id) ?? "Patient"
        }
    }
}

// MARK: - Summary card

/// A Home summary card: title with icon and count, up to 3 items, then "See all".
private struct SummaryCard<Content: View>: View {
    let title: String
    let systemImage: String
    let count: Int
    let emptyMessage: String
    let seeAll: HomeDestination
    @ViewBuilder let content: Content

    private var countDescription: String {
        "\(title), \(count) \(count == 1 ? "item" : "items")"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            HStack(spacing: Theme.Spacing.s) {
                Label(title, systemImage: systemImage)
                    .font(.headline)
                Spacer(minLength: Theme.Spacing.s)
                Text(count, format: .number)
                    .font(.caption.weight(.semibold))
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, Theme.Spacing.s)
                    .padding(.vertical, Theme.Spacing.xs)
                    .background(Theme.cardBorder, in: Capsule())
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(countDescription)
            .accessibilityAddTraits(.isHeader)

            if count == 0 {
                // Empty is a normal state, not an error.
                Label(emptyMessage, systemImage: "tray")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, minHeight: 44)
            } else {
                content

                NavigationLink(value: seeAll) {
                    Text("See all")
                        .font(.subheadline.weight(.semibold))
                        .frame(minHeight: 44)
                }
                .accessibilityLabel("See all \(title.lowercased())")
            }
        }
        .cardStyle()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(countDescription)
    }
}

// MARK: - Item row

/// One item in a summary card. The main part pushes a detail screen; the patient name is its own button.
private struct HomeItemRow<Detail: View, Accessory: View>: View {
    let destination: HomeDestination
    let title: String
    var systemImage: String? = nil
    let patientId: String?
    let patientName: String?
    let onOpenPatient: ((String) -> Void)?
    @ViewBuilder let detail: Detail
    @ViewBuilder let accessory: Accessory

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            NavigationLink(value: destination) {
                HStack(alignment: .firstTextBaseline, spacing: Theme.Spacing.s) {
                    if let systemImage {
                        Image(systemName: systemImage)
                            .foregroundStyle(.secondary)
                            .accessibilityHidden(true)
                    }
                    VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                        Text(title)
                            .font(.body)
                            .foregroundStyle(.primary)
                        detail
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                    Spacer(minLength: Theme.Spacing.s)
                    accessory
                }
                .frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            if let patientId, let patientName {
                patientLink(id: patientId, name: patientName)
                    .padding(.leading, systemImage == nil ? 0 : Theme.Spacing.l)
            }
        }
        .padding(.vertical, Theme.Spacing.xs)
    }

    @ViewBuilder
    private func patientLink(id: String, name: String) -> some View {
        let label = Label(name, systemImage: "person")
            .font(.subheadline)
            .frame(minHeight: 44)

        if let onOpenPatient {
            Button { onOpenPatient(id) } label: { label }
                .accessibilityLabel("\(name), open in Patients")
        } else {
            NavigationLink(value: HomeDestination.patient(id)) { label }
                .accessibilityLabel("\(name), open patient")
        }
    }
}

extension HomeItemRow where Accessory == EmptyView {
    init(destination: HomeDestination, title: String, systemImage: String? = nil,
         patientId: String?, patientName: String?, onOpenPatient: ((String) -> Void)?,
         @ViewBuilder detail: () -> Detail) {
        self.init(destination: destination, title: title, systemImage: systemImage,
                  patientId: patientId, patientName: patientName, onOpenPatient: onOpenPatient,
                  detail: detail, accessory: { EmptyView() })
    }
}

// MARK: - Display helpers

private extension DoctorTaskKind {
    var label: String {
        switch self {
        case .reviewReport: "Report review"
        case .checkMarkers: "Marker check"
        }
    }
}

private extension ActivityKind {
    var systemImage: String {
        switch self {
        case .scanUploaded: "square.and.arrow.up"
        case .commentAdded: "text.bubble"
        case .reportApproved: "checkmark.seal"
        }
    }
}

private extension AppNotificationKind {
    var systemImage: String {
        switch self {
        case .newScan: "cube"
        case .comment: "text.bubble"
        case .meetingInvitation: "person.3"
        }
    }
}

// MARK: - Previews

#Preview("Doctor Home") {
    DoctorHomeView(onOpenPatient: { _ in })
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

#Preview("Empty") {
    DoctorHomeView(model: .empty())
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
}

#Preview("Dark mode") {
    DoctorHomeView(onOpenPatient: { _ in })
        .environment(AppState(currentUser: MockDataService().doctorUser))
        .tint(Theme.doctorAccent)
        .preferredColorScheme(.dark)
}

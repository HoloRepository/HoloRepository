//
//  PatientHomeView.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Patient Home, top to bottom: header and greeting, search bar, Quick access, latest results,
/// upcoming appointment. Everything comes from `PatientContentModel`, so only the patient's own
/// approved content shows.
struct PatientHomeView: View {
    let model: PatientContentModel
    /// Switches the patient tab bar (Reports, Scans, Profile).
    var onSelectTab: (PatientTab) -> Void = { _ in }

    @Environment(AppState.self) private var appState
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @State private var path: [PatientRoute] = []

    private var isLargeText: Bool { dynamicTypeSize.isAccessibilitySize }

    var body: some View {
        NavigationStack(path: $path) {
            ScrollView {
                VStack(alignment: .leading, spacing: Theme.Spacing.l) {
                    header
                    searchBar
                    quickAccess
                    latestResults
                    upcomingAppointment
                }
                .padding(.horizontal, Theme.Spacing.m)
                .padding(.bottom, Theme.Spacing.l)
            }
            .background(Theme.background)
            .toolbar(.hidden, for: .navigationBar)
            .patientDestinations(model: model)
        }
    }

    // MARK: 1. Header

    private var patientName: String {
        model.patient?.name ?? appState.currentUser?.name ?? ""
    }

    private var firstName: String {
        patientName.split(separator: " ").first.map(String.init) ?? "there"
    }

    private var initials: String {
        model.patient?.initials
            ?? patientName.split(separator: " ").prefix(2).compactMap(\.first).map(String.init).joined()
    }

    private var greeting: String {
        switch Calendar.current.component(.hour, from: .now) {
        case 5..<12: "Good morning"
        case 12..<18: "Good afternoon"
        default: "Good evening"
        }
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.m) {
            HStack(spacing: Theme.Spacing.s) {
                // No app mark asset yet, so the name stands in for it.
                Text("HoloRepository")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(.tint)
                    .accessibilityAddTraits(.isHeader)
                Spacer(minLength: Theme.Spacing.s)

                // Patient notifications come later (March). No unread dot until there are patient
                // notifications to count; the bell explains what's coming.
                Button {
                    path.append(.comingSoon(.notifications))
                } label: {
                    Image(systemName: "bell")
                        .font(.title3)
                        .frame(minWidth: 44, minHeight: 44)
                }
                .accessibilityLabel("Notifications")

                Button {
                    onSelectTab(.profile)
                } label: {
                    Text(initials)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.tint)
                        .frame(minWidth: 44, minHeight: 44)
                        .background(Theme.patientAccent.opacity(0.15), in: Circle())
                }
                .accessibilityLabel("Your profile")
            }

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text("\(greeting), \(firstName) 👋")
                    .font(.title2.weight(.semibold))
                    .accessibilityLabel("\(greeting), \(firstName)")
                    .accessibilityAddTraits(.isHeader)
                Text("What would you like to find today?")
                    .font(.body)
                    .foregroundStyle(.secondary)
            }
            .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.top, Theme.Spacing.s)
    }

    // MARK: 2. Search bar

    private var searchBar: some View {
        Button {
            path.append(.search)
        } label: {
            HStack(spacing: Theme.Spacing.s) {
                Image(systemName: "magnifyingglass")
                    .foregroundStyle(.secondary)
                Text("Search reports, scans, appointments")
                    .font(.body)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
            }
            .padding(.horizontal, Theme.Spacing.m)
            .padding(.vertical, Theme.Spacing.s)
            .frame(minHeight: 44)
            .background(Theme.card, in: Capsule())
            .overlay(Capsule().stroke(Theme.cardBorder, lineWidth: 1))
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Search")
        .accessibilityHint("Find your reports, scans and appointments.")
    }

    // MARK: 3. Quick access

    private enum Shortcut: CaseIterable, Identifiable {
        case reports, scans, appointments, messages, familyAccess, aiAssistant

        var id: Self { self }

        var title: String {
            switch self {
            case .reports: "Reports"
            case .scans: "Scans"
            case .appointments: "Appointments"
            case .messages: "Messages"
            case .familyAccess: "Family Access"
            case .aiAssistant: "AI Assistant"
            }
        }

        var systemImage: String {
            switch self {
            case .reports: "doc.text"
            case .scans: "cube"
            case .appointments: "calendar"
            case .messages: "message"
            case .familyAccess: "person.2"
            case .aiAssistant: "sparkles"
            }
        }

        var accessibilityLabel: String {
            switch self {
            case .reports: "Your reports"
            case .scans: "Your scans"
            case .appointments: "Your appointments"
            case .messages: "Messages, coming soon"
            case .familyAccess: "Family access, coming soon"
            case .aiAssistant: "AI assistant, coming soon"
            }
        }
    }

    private func open(_ shortcut: Shortcut) {
        switch shortcut {
        case .reports: onSelectTab(.reports)
        case .scans: onSelectTab(.scans)
        case .appointments: path.append(.appointments)
        case .messages: path.append(.comingSoon(.messages))
        case .familyAccess: path.append(.comingSoon(.familyAccess))
        case .aiAssistant: path.append(.comingSoon(.aiAssistant))
        }
    }

    private var quickAccess: some View {
        // Three across normally; one per row at accessibility sizes so labels never truncate.
        let columns = Array(repeating: GridItem(.flexible(), spacing: Theme.Spacing.s, alignment: .top),
                            count: isLargeText ? 1 : 3)
        return VStack(alignment: .leading, spacing: Theme.Spacing.m) {
            sectionHeading("Quick access")
            LazyVGrid(columns: columns, spacing: Theme.Spacing.m) {
                ForEach(Shortcut.allCases) { shortcut in
                    Button {
                        open(shortcut)
                    } label: {
                        shortcutLabel(shortcut)
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(shortcut.accessibilityLabel)
                }
            }
        }
    }

    /// Icon over label, straight on the page background (no card).
    @ViewBuilder
    private func shortcutLabel(_ shortcut: Shortcut) -> some View {
        let icon = Image(systemName: shortcut.systemImage)
            .font(.title2)
            .foregroundStyle(Theme.patientAccent)
            .frame(minWidth: 44, minHeight: 44)
        let title = Text(shortcut.title)
            .font(.subheadline)
            .foregroundStyle(.primary)
            .fixedSize(horizontal: false, vertical: true)

        if isLargeText {
            HStack(spacing: Theme.Spacing.m) {
                icon
                title
                Spacer(minLength: 0)
            }
            .frame(minHeight: 44)
            .contentShape(Rectangle())
        } else {
            VStack(spacing: Theme.Spacing.xs) {
                icon
                title.multilineTextAlignment(.center)
            }
            .frame(maxWidth: .infinity, minHeight: 44)
            .contentShape(Rectangle())
        }
    }

    // MARK: 4. Your latest results

    private var latestResults: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            sectionHeading("Your latest results", link: "See all", linkLabel: "See all reports") {
                onSelectTab(.reports)
            }

            if let scan = model.latestScan {
                VStack(alignment: .leading, spacing: Theme.Spacing.m) {
                    let layout = isLargeText
                        ? AnyLayout(VStackLayout(alignment: .leading, spacing: Theme.Spacing.m))
                        : AnyLayout(HStackLayout(alignment: .top, spacing: Theme.Spacing.m))
                    layout {
                        ScanThumbnail(studyModel: scan.studyModel)
                        resultDetails(scan)
                    }

                    Button {
                        path.append(.report(reportId: scan.report.id))
                    } label: {
                        HStack(spacing: Theme.Spacing.s) {
                            Text("Understand my results")
                            Image(systemName: "arrow.right")
                                .accessibilityHidden(true)
                        }
                        .font(.headline)
                        .frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.borderedProminent)
                    .buttonBorderShape(.roundedRectangle(radius: Theme.Radius.button))
                    .accessibilityLabel("Understand my results")
                    .accessibilityHint("Opens your report, checked by your doctor.")
                }
                .cardStyle()
            } else {
                Text("Your results will show here once your doctor has checked them.")
                    .font(.body)
                    .foregroundStyle(.secondary)
                    .cardStyle()
            }
        }
    }

    private func resultDetails(_ scan: PatientScan) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            Text(scan.study.patientName)
                .font(.headline)
            Text(scan.study.date, format: .dateTime.day().month(.wide).year())
                .font(.subheadline)
                .foregroundStyle(.secondary)
            StatusBadge(kind: .approved, text: "Ready")
                .padding(.vertical, Theme.Spacing.xs)
            if let approver = scan.report.approvedBy {
                Text("Checked and approved by \(approver)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            if !scan.report.patientSummary.isEmpty {
                Label("Easy-read explanation available", systemImage: "text.book.closed")
                    .font(.subheadline)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }

    // MARK: 5. Upcoming appointment

    private var upcomingAppointment: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            sectionHeading("Upcoming appointment", link: "View all", linkLabel: "View all appointments") {
                path.append(.appointments)
            }

            if let appointment = model.nextAppointment {
                Button {
                    path.append(.appointment(appointmentId: appointment.id))
                } label: {
                    AppointmentRow(appointment: appointment, isLargeText: isLargeText)
                }
                .buttonStyle(.plain)
            } else {
                Text("You have no appointments booked.")
                    .font(.body)
                    .foregroundStyle(.secondary)
                    .cardStyle()
            }
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

// MARK: - Pieces

/// A dark tile with a few dots in the study's structure colours, standing in for an image of the 3D model.
/// Lesion pink is left out: it's reserved for the 3D view.
private struct ScanThumbnail: View {
    let studyModel: StudyModel
    @ScaledMetric(relativeTo: .headline) private var size: CGFloat = 72

    private var colours: [String] {
        let relevant = studyModel.structures.filter {
            $0.group != .lesions && studyModel.relevantStructureIDs.contains($0.id)
        }
        let others = studyModel.structures.filter { $0.group != .lesions && !relevant.contains($0) }
        return Array((relevant + others).prefix(4).map(\.colorHex))
    }

    var body: some View {
        let dot = size / 5
        RoundedRectangle(cornerRadius: Theme.Radius.button)
            .fill(Theme.viewerBackground)
            .frame(width: size, height: size)
            .overlay {
                Grid(horizontalSpacing: dot / 2, verticalSpacing: dot / 2) {
                    ForEach(Array(stride(from: 0, to: colours.count, by: 2)), id: \.self) { start in
                        GridRow {
                            ForEach(colours[start..<min(start + 2, colours.count)], id: \.self) { hex in
                                Circle()
                                    .fill(Color(hex: hex))
                                    .frame(width: dot, height: dot)
                            }
                        }
                    }
                }
            }
            .accessibilityHidden(true)
    }
}

/// Date block, then who, when and what, then a chevron.
private struct AppointmentRow: View {
    let appointment: Appointment
    let isLargeText: Bool
    @ScaledMetric(relativeTo: .title2) private var blockWidth: CGFloat = 56

    var body: some View {
        let layout = isLargeText
            ? AnyLayout(VStackLayout(alignment: .leading, spacing: Theme.Spacing.m))
            : AnyLayout(HStackLayout(alignment: .center, spacing: Theme.Spacing.m))
        layout {
            dateBlock

            HStack(spacing: Theme.Spacing.s) {
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    Text("Consultation with \(appointment.clinicianName)")
                        .font(.headline)
                    Text(appointment.date, format: .dateTime.weekday(.wide).hour().minute())
                        .font(.subheadline)
                        .monospacedDigit()
                    Text(appointment.department)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)

                Image(systemName: "chevron.right")
                    .foregroundStyle(.secondary)
            }
        }
        .cardStyle()
        .contentShape(Rectangle())
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
        .accessibilityHint("Opens your appointment.")
        .accessibilityAddTraits(.isButton)
    }

    private var dateBlock: some View {
        VStack(spacing: 0) {
            Text(appointment.date.formatted(.dateTime.month(.abbreviated)).uppercased())
                .font(.caption.weight(.semibold))
            Text(appointment.date, format: .dateTime.day())
                .font(.title2.weight(.bold))
                .monospacedDigit()
        }
        .foregroundStyle(.tint)
        .padding(.vertical, Theme.Spacing.s)
        .padding(.horizontal, Theme.Spacing.s)
        .frame(minWidth: blockWidth)
        .background(Theme.patientAccent.opacity(0.15),
                    in: RoundedRectangle(cornerRadius: Theme.Radius.button))
        .fixedSize()
    }

    private var accessibilityText: String {
        let when = appointment.date.formatted(.dateTime.weekday(.wide).day().month(.wide).hour().minute())
        return "Consultation with \(appointment.clinicianName), \(when), \(appointment.department)"
    }
}

// MARK: - Previews

#Preview("Home") {
    PatientHomeView(model: .demo())
        .environment(AppState(currentUser: MockDataService().patientUser))
        .patientStyle()
}

#Preview("Home, dark") {
    PatientHomeView(model: .demo())
        .environment(AppState(currentUser: MockDataService().patientUser))
        .patientStyle()
        .preferredColorScheme(.dark)
}

#Preview("Home, no upcoming appointment") {
    PatientHomeView(model: .noUpcomingAppointment())
        .environment(AppState(currentUser: MockDataService().patientUser))
        .patientStyle()
}

#Preview("Home, nothing shared yet") {
    PatientHomeView(model: .empty())
        .environment(AppState(currentUser: MockDataService().patientUser))
        .patientStyle()
}

#Preview("Home, largest text") {
    PatientHomeView(model: .demo())
        .environment(AppState(currentUser: MockDataService().patientUser))
        .patientStyle()
        .dynamicTypeSize(.accessibility5)
}

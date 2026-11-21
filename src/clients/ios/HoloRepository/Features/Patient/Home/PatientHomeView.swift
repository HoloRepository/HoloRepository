//
//  PatientHomeView.swift
//
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI

/// Patient Home: a greeting, then cards for the latest scan, latest report, next appointment and
/// recent activity. Everything comes from `PatientContentModel`, so only approved content shows.
struct PatientHomeView: View {
    let model: PatientContentModel

    @Environment(AppState.self) private var appState
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @State private var isShowingProfile = false

    private var firstName: String {
        let name = model.patient?.name ?? appState.currentUser?.name ?? ""
        return name.split(separator: " ").first.map(String.init) ?? "there"
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Theme.Spacing.m) {
                    header
                    latestScanCard
                    latestReportCard
                    nextAppointmentCard
                    recentActivityCard
                }
                .padding(Theme.Spacing.m)
            }
            .background(Theme.background)
            .toolbar(.hidden, for: .navigationBar)
            .patientDestinations(model: model)
            .sheet(isPresented: $isShowingProfile) {
                PatientProfileView()
            }
        }
    }

    // MARK: Header

    private var header: some View {
        // Stack the profile button under the greeting at accessibility sizes so nothing truncates.
        let layout = dynamicTypeSize.isAccessibilitySize
            ? AnyLayout(VStackLayout(alignment: .leading, spacing: Theme.Spacing.s))
            : AnyLayout(HStackLayout(alignment: .center, spacing: Theme.Spacing.m))
        return layout {
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(Date.now, format: .dateTime.weekday(.wide).day().month(.wide))
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Text("Hello, \(firstName)")
                    .font(.largeTitle.weight(.semibold))
                    .accessibilityAddTraits(.isHeader)
            }
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)

            Button {
                isShowingProfile = true
            } label: {
                Image(systemName: "person.crop.circle")
                    .font(.largeTitle)
                    .frame(minWidth: 44, minHeight: 44)
            }
            .accessibilityLabel("Your profile")
        }
        .padding(.top, Theme.Spacing.s)
    }

    // MARK: Cards

    @ViewBuilder
    private var latestScanCard: some View {
        HomeCard(title: "Your latest scan", systemImage: "cube") {
            if let scan = model.latestScan {
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    Text(scan.study.patientName)
                        .font(.body.weight(.semibold))
                    Text(scan.study.date, format: .dateTime.day().month(.wide).year())
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                .accessibilityElement(children: .combine)

                NavigationLink(value: PatientRoute.viewer(studyId: scan.study.id)) {
                    Label("Open 3D view", systemImage: "rotate.3d")
                        .font(.headline)
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.borderedProminent)
                .buttonBorderShape(.roundedRectangle(radius: Theme.Radius.button))
                .accessibilityLabel("Open the 3D view of your \(scan.study.patientName.lowercased())")
            } else {
                Text("You don't have any scans to look at yet.")
                    .font(.body)
                    .foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private var latestReportCard: some View {
        HomeCard(title: "Your latest report", systemImage: "doc.text") {
            if let report = model.latestReport {
                VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                    Text(reportName(report))
                        .font(.body.weight(.semibold))
                    if let approver = report.approvedBy {
                        Text("Checked by \(approver)")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                    StatusBadge(kind: .approved, text: "Approved")
                }
                .accessibilityElement(children: .combine)

                NavigationLink(value: PatientRoute.report(reportId: report.id)) {
                    rowLabel("Read report")
                }
                .accessibilityLabel("Read your report")
            } else {
                Text("When your doctor has checked a report, it will show here.")
                    .font(.body)
                    .foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private var nextAppointmentCard: some View {
        HomeCard(title: "Next appointment", systemImage: "calendar") {
            if let appointment = model.nextAppointment {
                NavigationLink(value: PatientRoute.appointment(appointmentId: appointment.id)) {
                    HStack(spacing: Theme.Spacing.m) {
                        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                            Text(appointment.date, format: .dateTime.weekday(.wide).day().month(.wide))
                                .font(.body.weight(.semibold))
                            Text(appointment.date, format: .dateTime.hour().minute())
                                .font(.subheadline)
                                .monospacedDigit()
                            Text("With \(appointment.clinicianName)")
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                        }
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        chevron
                    }
                    .frame(minHeight: 44)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityElement(children: .combine)
                .accessibilityHint("Opens your appointment.")
            } else {
                Text("You have no appointments booked.")
                    .font(.body)
                    .foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private var recentActivityCard: some View {
        HomeCard(title: "Recent activity", systemImage: "clock") {
            if model.activity.isEmpty {
                Text("Nothing new right now.")
                    .font(.body)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(model.activity.prefix(3)) { item in
                    Label {
                        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                            Text(item.text)
                                .font(.body)
                            Text(item.timestamp, format: .relative(presentation: .named))
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                        }
                        .fixedSize(horizontal: false, vertical: true)
                    } icon: {
                        Image(systemName: item.systemImage)
                            .foregroundStyle(.tint)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
        }
    }

    // MARK: Helpers

    /// "Report on your scan of your tummy" reads oddly, so use the scan's plain name.
    private func reportName(_ report: Report) -> String {
        model.scan(studyId: report.studyId).map { "About your \($0.study.patientName.lowercased())" }
            ?? "Your scan report"
    }

    private func rowLabel(_ title: String) -> some View {
        HStack {
            Text(title)
                .font(.headline)
                .foregroundStyle(.tint)
            Spacer()
            chevron
        }
        .frame(minHeight: 44)
        .contentShape(Rectangle())
    }

    private var chevron: some View {
        Image(systemName: "chevron.right")
            .foregroundStyle(.secondary)
            .accessibilityHidden(true)
    }
}

/// A titled card on patient Home.
private struct HomeCard<Content: View>: View {
    let title: String
    let systemImage: String
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.m) {
            Label(title, systemImage: systemImage)
                .font(.headline)
                .foregroundStyle(.secondary)
                .accessibilityAddTraits(.isHeader)
            content
        }
        .cardStyle()
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

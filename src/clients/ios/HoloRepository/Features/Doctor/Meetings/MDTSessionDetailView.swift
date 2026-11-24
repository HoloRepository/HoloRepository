//
//  MDTSessionDetailView.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

/// One MDT session: name, time, status, place, participants and the cases to discuss.
/// Each case opens the 3D viewer. Video joining (Webex) comes in March.
struct MDTSessionDetailView: View {
    let sessionId: String
    let model: DoctorHomeModel
    let records: PatientsModel
    let push: (DoctorRoute) -> Void

    @State private var isShowingVideoNote = false

    var body: some View {
        Group {
            if let session = model.session(id: sessionId) {
                TimelineView(.everyMinute) { context in
                    content(session, now: context.date)
                }
            } else {
                ContentUnavailableView("Session not available", systemImage: "lock",
                                       description: Text("This session has ended or you aren't invited to it."))
            }
        }
        .background(Theme.background)
        .navigationTitle("MDT session")
        .navigationBarTitleDisplayMode(.inline)
        .alert("Video joining arrives in March", isPresented: $isShowingVideoNote) {
            Button("OK", role: .cancel) {}
        } message: {
            Text("For now, join the call from your usual meeting app.")
        }
    }

    private func content(_ session: MDTSession, now: Date) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Theme.Spacing.m) {
                summary(session, now: now)

                Button {
                    isShowingVideoNote = true
                } label: {
                    Label("Join video call", systemImage: "video")
                        .font(.headline)
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.borderedProminent)
                .buttonBorderShape(.roundedRectangle(radius: Theme.Radius.button))
                .tint(Theme.doctorAccent)
                .accessibilityHint("Video calls aren't available yet.")

                casesCard(session)
                participantsCard(session)
            }
            .padding(Theme.Spacing.m)
        }
    }

    private func summary(_ session: MDTSession, now: Date) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            if let specialty = session.specialty {
                Text(specialty.uppercased())
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(Theme.doctorAccent)
            }
            Text(session.caseName)
                .font(.title2.weight(.semibold))
                .accessibilityAddTraits(.isHeader)
            Label(session.startTime.formatted(.dateTime.weekday(.wide).day().month(.wide).year()),
                  systemImage: "calendar")
                .font(.subheadline)
            Label(session.timeRangeText, systemImage: "clock")
                .font(.subheadline)
                .monospacedDigit()
            Label(session.location ?? "Online", systemImage: session.location == nil ? "video" : "mappin.and.ellipse")
                .font(.subheadline)
            MDTStatusLabel(status: session.liveStatus(at: now))
                .padding(.top, Theme.Spacing.xs)
        }
        .fixedSize(horizontal: false, vertical: true)
        .cardStyle()
        .accessibilityElement(children: .combine)
    }

    private func casesCard(_ session: MDTSession) -> some View {
        // Only cases for patients this doctor may see.
        let cases = model.cases(for: session).filter { records.patient(id: $0.patientId) != nil }
        return VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            Text(cases.count == 1 ? "1 case" : "\(cases.count) cases")
                .font(.headline)
                .accessibilityAddTraits(.isHeader)
            if cases.isEmpty {
                Text("No cases you're authorised to view.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            ForEach(Array(cases.enumerated()), id: \.offset) { index, mdtCase in
                if index > 0 { Divider() }
                Button {
                    push(.viewer(studyId: mdtCase.studyId))
                } label: {
                    caseRow(mdtCase)
                }
                .buttonStyle(.plain)
            }
        }
        .cardStyle()
    }

    private func caseRow(_ mdtCase: MDTCase) -> some View {
        let patientName = records.patient(id: mdtCase.patientId)?.name ?? "Patient"
        let study = records.study(id: mdtCase.studyId)
        return HStack(spacing: Theme.Spacing.m) {
            Image(systemName: "cube")
                .foregroundStyle(Theme.doctorAccent)
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(patientName)
                    .font(.body.weight(.semibold))
                if let study {
                    Text("\(study.description) · \(study.date.formatted(.dateTime.day().month(.abbreviated).year()))")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            }
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
            Image(systemName: "chevron.right")
                .font(.subheadline)
                .foregroundStyle(.tertiary)
        }
        .frame(minHeight: 44)
        .contentShape(Rectangle())
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(patientName), \(study?.description ?? "scan")")
        .accessibilityHint("Opens the 3D viewer.")
        .accessibilityAddTraits(.isButton)
    }

    private func participantsCard(_ session: MDTSession) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.s) {
            Text("\(session.participantCount) participants")
                .font(.headline)
                .accessibilityAddTraits(.isHeader)
            ForEach(session.participants ?? [], id: \.self) { name in
                Label(name, systemImage: "person")
                    .font(.body)
                    .frame(minHeight: 32, alignment: .leading)
            }
        }
        .cardStyle()
    }
}

// MARK: - Previews

#Preview("MDT session detail") {
    NavigationStack {
        MDTSessionDetailView(sessionId: "m-001", model: .demo(), records: .demo()) { _ in }
    }
    .environment(AppState(currentUser: MockDataService().doctorUser))
    .tint(Theme.doctorAccent)
}

#Preview("MDT session detail, two cases") {
    NavigationStack {
        MDTSessionDetailView(sessionId: "m-007", model: .demo(), records: .demo()) { _ in }
    }
    .environment(AppState(currentUser: MockDataService().doctorUser))
    .tint(Theme.doctorAccent)
    .preferredColorScheme(.dark)
}

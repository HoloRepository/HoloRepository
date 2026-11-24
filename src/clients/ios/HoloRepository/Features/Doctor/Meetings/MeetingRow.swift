//
//  MeetingRow.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

/// A compact MDT session row: date block, then title, time, specialty and place, then a chevron.
/// Used on Home and in the Video Meeting tab.
struct MeetingRow: View {
    let session: MDTSession
    var now: Date = .now

    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        let layout = dynamicTypeSize.isAccessibilitySize
            ? AnyLayout(VStackLayout(alignment: .leading, spacing: Theme.Spacing.s))
            : AnyLayout(HStackLayout(alignment: .center, spacing: Theme.Spacing.m))
        layout {
            MeetingDateBlock(date: session.startTime)

            HStack(spacing: Theme.Spacing.s) {
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    Text(session.caseName)
                        .font(.headline)
                    Text(session.timeRangeText)
                        .font(.subheadline)
                        .monospacedDigit()
                    Text(session.detailText)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                    let status = session.liveStatus(at: now)
                    if status == .live || status == .startingSoon {
                        MDTStatusLabel(status: status)
                    }
                }
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)

                Image(systemName: "chevron.right")
                    .font(.subheadline)
                    .foregroundStyle(.tertiary)
            }
        }
        .frame(minHeight: 44)
        .contentShape(Rectangle())
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(session.accessibilityText(at: now))
        .accessibilityAddTraits(.isButton)
    }
}

/// Month small above a larger day number, on a soft doctor-accent tile. Never truncates.
struct MeetingDateBlock: View {
    let date: Date
    @ScaledMetric(relativeTo: .title3) private var minWidth: CGFloat = 52

    var body: some View {
        VStack(spacing: 0) {
            Text(date.formatted(.dateTime.month(.abbreviated)).uppercased())
                .font(.caption.weight(.semibold))
            Text(date, format: .dateTime.day())
                .font(.title3.weight(.bold))
                .monospacedDigit()
        }
        .foregroundStyle(Theme.doctorAccent)
        .padding(Theme.Spacing.s)
        .frame(minWidth: minWidth)
        .background(Theme.doctorAccent.opacity(0.12), in: RoundedRectangle(cornerRadius: Theme.Radius.button))
        .fixedSize()
        .accessibilityHidden(true)
    }
}

/// Live (green), Starting soon (amber), otherwise neutral.
struct MDTStatusLabel: View {
    let status: MDTLiveStatus

    var body: some View {
        switch status {
        case .live: StatusBadge(kind: .approved, text: status.displayName)
        case .startingSoon: StatusBadge(kind: .pending, text: status.displayName)
        case .scheduled, .completed: ColouredBadge(text: status.displayName, colour: .secondary)
        }
    }
}

// MARK: - Display helpers

extension MDTSession {
    /// "14:00 – 15:00".
    var timeRangeText: String {
        let start = startTime.formatted(date: .omitted, time: .shortened)
        let end = effectiveEndTime.formatted(date: .omitted, time: .shortened)
        return "\(start) – \(end)"
    }

    /// "Urology · Room 4B" or "Respiratory · Online".
    var detailText: String {
        [specialty, location ?? "Online"].compactMap { $0 }.joined(separator: " · ")
    }

    func accessibilityText(at now: Date = .now) -> String {
        let day = startTime.formatted(.dateTime.weekday(.wide).day().month(.wide))
        let start = startTime.formatted(date: .omitted, time: .shortened)
        let end = effectiveEndTime.formatted(date: .omitted, time: .shortened)
        var parts = [caseName, "\(day), \(start) to \(end)"]
        if let specialty { parts.append(specialty) }
        parts.append(location ?? "Online")
        parts.append(liveStatus(at: now).displayName)
        return parts.joined(separator: ", ")
    }
}

#Preview("Meeting rows") {
    let model = DoctorHomeModel.demo()
    ScrollView {
        VStack(spacing: Theme.Spacing.m) {
            ForEach(model.sessions) { session in
                MeetingRow(session: session).cardStyle()
            }
        }
        .padding()
    }
    .background(Theme.background)
}

//
//  TaskRow.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

/// One task: title, patient and study, due date, status and urgency labels, chevron.
/// Used on Home and in the full task list.
struct TaskRow: View {
    let task: DoctorTask
    let patientName: String?
    let studyName: String?

    var body: some View {
        HStack(alignment: .center, spacing: Theme.Spacing.s) {
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(task.title)
                    .font(.headline)
                if !subtitle.isEmpty {
                    Text(subtitle)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                if let dueText {
                    Label(dueText, systemImage: "clock")
                        .font(.subheadline)
                        .monospacedDigit()
                        .foregroundStyle(isOverdue ? Theme.danger : .secondary)
                }
                // Side by side when they fit, stacked at large text sizes.
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: Theme.Spacing.s) { labels }
                    VStack(alignment: .leading, spacing: Theme.Spacing.xs) { labels }
                }
                .padding(.top, Theme.Spacing.xs)
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
        .accessibilityLabel(accessibilityText)
        .accessibilityAddTraits(.isButton)
    }

    @ViewBuilder
    private var labels: some View {
        TaskStatusLabel(status: task.status)
        if task.isUrgent {
            ColouredBadge(text: "Urgent", colour: Theme.danger)
        }
        if let state = task.verificationState, task.kind == .approveReport {
            StatusBadge(state: state)
        }
    }

    private var subtitle: String {
        [patientName, studyName].compactMap { $0 }.joined(separator: " · ")
    }

    private var isOverdue: Bool {
        guard let due = task.dueDate, task.status != .completed else { return false }
        return due < .now
    }

    private var dueText: String? {
        guard let due = task.dueDate else { return nil }
        let calendar = Calendar.current
        let time = due.formatted(date: .omitted, time: .shortened)
        let prefix = isOverdue ? "Overdue" : "Due"
        if calendar.isDateInToday(due) { return "\(prefix) today, \(time)" }
        if calendar.isDateInTomorrow(due) { return "\(prefix) tomorrow, \(time)" }
        if calendar.isDateInYesterday(due) { return "\(prefix) yesterday, \(time)" }
        return "\(prefix) \(due.formatted(.dateTime.weekday(.abbreviated).day().month(.abbreviated)))"
    }

    /// e.g. "Approve clinical report, Alex Patel, CT abdomen, urgent, not started, due today, 17:00".
    private var accessibilityText: String {
        var parts = [task.title]
        if let patientName { parts.append(patientName) }
        if let studyName { parts.append(studyName) }
        if task.isUrgent { parts.append("urgent") }
        parts.append(task.status.displayName.lowercased())
        if let state = task.verificationState, task.kind == .approveReport {
            parts.append(state.displayName.lowercased())
        }
        if let dueText { parts.append(dueText.lowercased()) }
        return parts.joined(separator: ", ")
    }
}

/// The task's status: completed in green, in progress in amber, not started neutral.
struct TaskStatusLabel: View {
    let status: DoctorTaskStatus

    var body: some View {
        switch status {
        case .completed: StatusBadge(kind: .approved, text: status.displayName)
        case .inProgress: StatusBadge(kind: .pending, text: status.displayName)
        case .notStarted: ColouredBadge(text: status.displayName, colour: .secondary)
        }
    }
}

/// A small capsule label in any meaningful colour (Theme status colours or neutral grey).
struct ColouredBadge: View {
    let text: String
    let colour: Color

    var body: some View {
        Text(text)
            .font(.caption.weight(.semibold))
            .foregroundStyle(colour)
            .padding(.horizontal, Theme.Spacing.s)
            .padding(.vertical, Theme.Spacing.xs)
            .background(colour.opacity(0.15), in: Capsule())
            .fixedSize()
    }
}

/// The four filter chips in a horizontally scrolling row. Selected chip in the doctor accent.
struct TaskFilterBar: View {
    @Binding var selection: TaskFilter
    /// Task count per filter, for VoiceOver.
    let count: (TaskFilter) -> Int

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: Theme.Spacing.s) {
                ForEach(TaskFilter.allCases) { filter in
                    chip(filter)
                }
            }
            .padding(.vertical, Theme.Spacing.xs)
        }
        // Lets the chips scroll edge to edge while lining up with the content.
        .scrollClipDisabled()
    }

    private func chip(_ filter: TaskFilter) -> some View {
        let isSelected = filter == selection
        return Button {
            selection = filter
        } label: {
            Text(filter.title)
                .font(.subheadline.weight(isSelected ? .semibold : .regular))
                .foregroundStyle(isSelected ? Theme.doctorAccent : .primary)
                .padding(.horizontal, Theme.Spacing.m)
                .frame(minHeight: 44)
                .background(isSelected ? Theme.doctorAccent.opacity(0.15) : Theme.card, in: Capsule())
                .overlay(Capsule().stroke(isSelected ? Theme.doctorAccent : Theme.cardBorder, lineWidth: 1))
                // Never truncate: the row scrolls instead.
                .fixedSize()
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(filter.title), \(count(filter)) \(count(filter) == 1 ? "task" : "tasks")")
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

#Preview("Task rows") {
    let model = DoctorHomeModel.demo()
    ScrollView {
        VStack(spacing: Theme.Spacing.m) {
            ForEach(model.tasks) { task in
                TaskRow(task: task, patientName: model.patientName(for: task.patientId),
                        studyName: model.studyName(for: task.studyId))
                    .cardStyle()
            }
        }
        .padding()
    }
    .background(Theme.background)
    .tint(Theme.doctorAccent)
}

//
//  TaskListView.swift
//
//
//  Created by Ng Xin Ning on 24/11/2026.
//

import SwiftUI

/// Every pending task with the same four filters and rows as Home. Opened from "See all" on Home.
struct TaskListView: View {
    let model: DoctorHomeModel
    let push: (DoctorRoute) -> Void
    @State private var filter: TaskFilter

    init(model: DoctorHomeModel, filter: TaskFilter = .notStarted, push: @escaping (DoctorRoute) -> Void) {
        self.model = model
        self.push = push
        _filter = State(initialValue: filter)
    }

    var body: some View {
        let tasks = model.tasks(for: filter)

        ScrollView {
            VStack(alignment: .leading, spacing: Theme.Spacing.m) {
                TaskFilterBar(selection: $filter) { model.tasks(for: $0).count }

                if tasks.isEmpty {
                    Label(filter.emptyMessage, systemImage: "tray")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .frame(minHeight: 44)
                        .cardStyle()
                } else {
                    VStack(spacing: Theme.Spacing.m) {
                        ForEach(tasks) { task in
                            Button {
                                if let route = task.route { push(route) }
                            } label: {
                                TaskRow(task: task, patientName: model.patientName(for: task.patientId),
                                        studyName: model.studyName(for: task.studyId))
                                    .cardStyle()
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
            .padding(Theme.Spacing.m)
        }
        .background(Theme.background)
        .navigationTitle("Pending tasks")
        .navigationBarTitleDisplayMode(.inline)
    }
}

// MARK: - Previews

#Preview("Task list") {
    NavigationStack {
        TaskListView(model: .demo(), filter: .urgent) { _ in }
    }
    .tint(Theme.doctorAccent)
}

#Preview("Empty: Not started") {
    NavigationStack { TaskListView(model: .empty(), filter: .notStarted) { _ in } }
        .tint(Theme.doctorAccent)
}

#Preview("Empty: In progress") {
    NavigationStack { TaskListView(model: .empty(), filter: .inProgress) { _ in } }
        .tint(Theme.doctorAccent)
}

#Preview("Empty: Urgent") {
    NavigationStack { TaskListView(model: .empty(), filter: .urgent) { _ in } }
        .tint(Theme.doctorAccent)
}

#Preview("Empty: Completed") {
    NavigationStack { TaskListView(model: .empty(), filter: .completed) { _ in } }
        .tint(Theme.doctorAccent)
}

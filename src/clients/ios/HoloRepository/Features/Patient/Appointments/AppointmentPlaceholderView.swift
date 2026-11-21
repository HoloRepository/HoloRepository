//
//  AppointmentPlaceholderView.swift
//
//
//  Created by Ng Xin Ning on 21/11/2026.
//

import SwiftUI

/// Placeholder for one appointment's details. The full screen is built with the Appointments tab.
struct AppointmentPlaceholderView: View {
    let appointment: Appointment?

    var body: some View {
        Group {
            if let appointment {
                ContentUnavailableView {
                    Label(appointment.date.formatted(.dateTime.weekday(.wide).day().month(.wide)),
                          systemImage: "calendar")
                } description: {
                    Text("\(appointment.date.formatted(date: .omitted, time: .shortened)) with \(appointment.clinicianName). More details will show here soon.")
                }
            } else {
                ContentUnavailableView("Appointment not found", systemImage: "calendar",
                                       description: Text("This appointment may have been moved or cancelled."))
            }
        }
        .navigationTitle("Appointment")
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview {
    NavigationStack {
        AppointmentPlaceholderView(appointment: PatientContentModel.demo().nextAppointment)
    }
    .patientStyle()
}

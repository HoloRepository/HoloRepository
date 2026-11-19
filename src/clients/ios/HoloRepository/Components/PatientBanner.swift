//
//  PatientBanner.swift
//
//
//  Created by Ng Xin Ning on 19/11/2026.
//

import SwiftUI

/// Identifies the patient at the top of every clinical screen: name, age, sex and masked hospital number.
struct PatientBanner: View {
    let patient: Patient

    var body: some View {
        HStack(spacing: Theme.Spacing.m) {
            InitialsAvatar(patient.initials)

            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                Text(patient.name)
                    .font(.headline)
                Text("\(patient.age()) · \(patient.sex.displayName) · \(patient.maskedHospitalNumber)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
            }

            Spacer(minLength: 0)
        }
        .padding(.horizontal, Theme.Spacing.m)
        .padding(.vertical, Theme.Spacing.s)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Theme.card)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(Theme.cardBorder)
                .frame(height: 1)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(
            "Patient \(patient.name), \(patient.age()), \(patient.sex.displayName), "
            + "hospital number ending \(patient.hospitalNumber.suffix(4))"
        )
    }
}

#Preview {
    PatientBanner(patient: MockDataService().patients[0])
}

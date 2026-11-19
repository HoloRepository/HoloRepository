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
    /// Smaller, without the avatar, for screens where content needs the space (e.g. the 3D viewer).
    var isCompact = false

    var body: some View {
        HStack(spacing: Theme.Spacing.m) {
            if !isCompact {
                InitialsAvatar(patient.initials)
            }

            VStack(alignment: .leading, spacing: isCompact ? 0 : Theme.Spacing.xs) {
                Text(patient.name)
                    .font(isCompact ? .subheadline.weight(.semibold) : .headline)
                Text("\(patient.age()) · \(patient.sex.displayName) · \(patient.maskedHospitalNumber)")
                    .font(isCompact ? .caption : .subheadline)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
            }

            Spacer(minLength: 0)
        }
        .padding(.horizontal, Theme.Spacing.m)
        .padding(.vertical, isCompact ? Theme.Spacing.xs : Theme.Spacing.s)
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

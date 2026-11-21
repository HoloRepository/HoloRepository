//
//  Theme.swift
//  
//
//  Created by Ng Xin Ning on 18/11/2026.
//

import SwiftUI
import UIKit

// HoloRepository design tokens: minimal, native iOS, one blue accent.
// Light and dark values are built in; SwiftUI switches automatically.
// Keep the same hex values in src/clients/shared/theme.json for Android.

enum Theme {

    // MARK: Colours

    /// Main buttons, links, selected tab. Use sparingly.
    /// Doctor app = blue, patient app = teal. Set once on each role's root view with .tint(...).
    static let doctorAccent = Color(light: 0x2F6FD6, dark: 0x5B93EE)
    static let patientAccent = Color(light: 0x13877F, dark: 0x3CC4B8)
    static let accent = doctorAccent

    /// App backgrounds (soft, not pure white / pure black).
    static let background = Color(light: 0xF7F9FC, dark: 0x0F1520)
    static let card = Color(light: 0xFFFFFF, dark: 0x182131)
    static let cardBorder = Color(light: 0xE3E8EF, dark: 0x263246)

    /// The 3D viewer stays dark in both themes so organs stand out.
    static let viewerBackground = Color(light: 0x0E1420, dark: 0x070A10)

    /// Status colours: only where they carry meaning.
    static let approved = Color(light: 0x2E9E5B, dark: 0x5FCB8A)
    static let pending = Color(light: 0xD99A1E, dark: 0xF0B94A)   // AI draft / waiting for review
    static let danger = Color(light: 0xC8382F, dark: 0xF07468)    // errors only

    /// Reserved for lesions in the 3D view. No UI element may use this colour.
    static let lesion = Color(light: 0xFF2D95, dark: 0xFF2D95)

    // MARK: Layout

    enum Spacing {
        static let xs: CGFloat = 4
        static let s: CGFloat = 8
        static let m: CGFloat = 16
        static let l: CGFloat = 24
        static let xl: CGFloat = 32
    }

    enum Radius {
        static let button: CGFloat = 12
        static let card: CGFloat = 16
    }
}

// MARK: - Reusable styles

/// A quiet card: rounded, thin border, no shadow.
struct CardStyle: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(Theme.Spacing.m)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Theme.card, in: RoundedRectangle(cornerRadius: Theme.Radius.card))
            .overlay(
                RoundedRectangle(cornerRadius: Theme.Radius.card)
                    .stroke(Theme.cardBorder, lineWidth: 1)
            )
    }
}

extension View {
    func cardStyle() -> some View { modifier(CardStyle()) }
}

/// Small status label, e.g. "Approved" or "AI draft".
struct StatusBadge: View {
    enum Kind { case approved, pending }
    let kind: Kind
    /// Overrides the default "Approved" / "AI draft" text.
    var text: String? = nil

    var body: some View {
        let color = kind == .approved ? Theme.approved : Theme.pending
        Text(text ?? (kind == .approved ? "Approved" : "AI draft"))
            .font(.caption.weight(.semibold))
            .foregroundStyle(color)
            .padding(.horizontal, Theme.Spacing.s)
            .padding(.vertical, Theme.Spacing.xs)
            .background(color.opacity(0.15), in: Capsule())
    }
}

extension StatusBadge {
    /// Badge showing a report's real verification state.
    init(state: VerificationState) {
        self.init(kind: state == .verified ? .approved : .pending, text: state.displayName)
    }
}

// MARK: - Colour helper

extension Color {
    /// A colour with separate light and dark mode values, from 0xRRGGBB.
    init(light: UInt32, dark: UInt32) {
        self.init(uiColor: UIColor { traits in
            UIColor(rgb: traits.userInterfaceStyle == .dark ? dark : light)
        })
    }

    /// A colour from data, e.g. a structure's "#RRGGBB" in the 3D model. Use only for data-driven colours;
    /// UI colours come from `Theme`. Invalid strings give grey.
    init(hex: String) {
        let digits = hex.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
        self.init(uiColor: UIColor(rgb: UInt32(digits, radix: 16) ?? 0x888888))
    }
}

private extension UIColor {
    convenience init(rgb: UInt32) {
        self.init(
            red: CGFloat((rgb >> 16) & 0xFF) / 255,
            green: CGFloat((rgb >> 8) & 0xFF) / 255,
            blue: CGFloat(rgb & 0xFF) / 255,
            alpha: 1
        )
    }
}

// MARK: - Preview

#Preview("Theme") {
    ScrollView {
        VStack(alignment: .leading, spacing: Theme.Spacing.m) {
            Text("Hello, Alex").font(.largeTitle.weight(.semibold))

            VStack(alignment: .leading, spacing: Theme.Spacing.s) {
                Text("Your latest scan").font(.headline)
                Text("CT scan of your tummy · 10 Nov")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Button("Open 3D view") {}
                    .buttonStyle(.borderedProminent)
                    .tint(Theme.accent)
                    .frame(maxWidth: .infinity)
            }
            .cardStyle()

            HStack {
                VStack(alignment: .leading) {
                    Text("Your report").font(.headline)
                    Text("Approved 12 Nov").font(.subheadline).foregroundStyle(.secondary)
                }
                Spacer()
                StatusBadge(kind: .approved)
            }
            .cardStyle()
        }
        .padding(Theme.Spacing.m)
    }
    .background(Theme.background)
}
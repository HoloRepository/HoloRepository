# HoloRepository iOS app

Native iOS client for HoloRepository 2027 (UCL IXN). Clinicians review 3D medical scans and AI-drafted reports; patients view their own approved scans and reports. The full feature plan is in `docs/APP_PLAN.md`: read it before building a feature.

## Current goal: December prototype
Build the full app flow and UI with **mock data only**. No real backend, network calls or authentication yet.

**Do not build yet (March or later):**
- Real authentication, MFA, biometric login, lockout, invitations by SMS or email
- Network calls to the daemon (keep everything behind `MockDataService`)
- Live marker sync, Webex, AI transcription, SNOMED-CT or NICE lookups
- Notifications, family/caregiver access
- Sharing or exporting reports outside the app

If a task seems to need one of these, stop and ask.

## Stack
- Swift, SwiftUI, iOS 17+, `@Observable`, `NavigationStack`
- iPhone only for December (no iPad layouts yet)
- No third-party packages unless asked

## Folder structure
```
HoloRepository/
├── App/            HoloRepositoryApp, AppState (signed-in user + role), RootView
├── Core/
│   ├── Theme/      Theme.swift (colours, spacing, CardStyle, StatusBadge)
│   ├── Models/     User, Patient, Study, Structure, Report, Marker, Appointment
│   ├── Services/   MockDataService (December); DaemonService later, same interface
│   └── Viewer/     ViewerBridge, ModelWebView
├── Features/
│   ├── Auth/       role choice, sign-in, invitation, activation
│   ├── Doctor/     DoctorTabView + Home/, Patients/, Imaging/, Reports/, Profile/
│   └── Patient/    PatientTabView + Home/, MyScans/, MyReports/, Appointments/, Profile/, Search/, ComingSoon/
├── Components/     shared UI: Card, StatusBadge, PatientBanner, EmptyState
└── Resources/      Assets.xcassets, MockData/ (JSON), Viewer/ (holo_viewer.html)
```
- New screens go in `Features/<Role>/<Tab>/`. One view per file, named after the screen.
- Shared mock data and the viewer bundle also live in `../shared/` for Android; keep copies in `Resources/` identical.
- After editing shared mock data, run scripts/sync-shared.sh.

## Roles and navigation
- Sign-in → role check → doctor or patient tabs (RootView decides).
- **Doctor tabs:** Home · Patients · Imaging · Reports. Meetings live in Home (upcoming and active MDT sessions) and in a patient's consultation history, not in their own tab.
- **Patient tabs:** Home · Reports · Scans · Messages · Profile. Appointments has no tab: it opens from Home (Quick access, the Upcoming appointment card) and from search.
- Patient Home order: header (bell, avatar), greeting, search bar, Quick access (Reports, Scans, Appointments / Messages, Family Access, AI Assistant), Your latest results, Upcoming appointment.
- Features not built yet (Messages, Family Access, AI Assistant, patient notifications) open the shared `ComingSoonView`.
- Profile/settings open from the avatar on each Home (on the patient app, the avatar switches to the Profile tab).
- Patients only ever see content marked as approved / authorised for patient access. Doctors see AI drafts and approved content.
- Patient identity is linked to a verified clinical record, not just an email.

## 3D viewer
The 3D model is the team's shared web viewer inside a `WKWebView`. Never rebuild 3D natively. Talk to it only through `ViewerBridge`:
- Swift → JS: `HoloViewer.loadCase(case)`, `HoloViewer.setVisible(ids)`, `HoloViewer.resetView()`, `HoloViewer.setOpacity(group, value)`
- JS → Swift: `{type: "ready"}`, `{type: "tap", id}`
- **CT only.** The daemon segments CT with NVIDIA NV-Segment-CT. Every study is 3D-ready: no modality filter, no modality icons, no "no 3D model" state. Keep the `Modality` type (just `.ct`) so other modalities can be added later.
- Structure IDs are NV-Segment-CT label names exactly as the model outputs them (e.g. `"kidney mass"`, `"right kidney"`, `"left rib 9"`); `name` is the display name. Don't invent IDs for structures the model doesn't segment.
- Structure groups: lesions, organs, vessels, bones, muscles. Lesions are always #FF2D95 (`Theme.lesion`) and nothing else uses that colour.
- On a phone the model fills the screen; structures, markers and notes open in a bottom sheet (`.presentationDetents([.medium, .large])`).
- Doctor viewer has a "Patient view" button that switches to the simple patient view on the same phone.
- Markers ("tap model → marker → comment") are saved on the device in December, with a "Share with patient" toggle.

## Design
- Minimal native iOS: standard SwiftUI components, white space, no gradients or heavy shadows.
- Use colours from `Theme.swift` only. Never hard-code colours in views.
  - Doctor accent: `Theme.doctorAccent` (blue). Patient accent: `Theme.patientAccent` (teal). Set with `.tint(...)` on each role's tab view.
  - Status colours only where they carry meaning: `Theme.approved`, `Theme.pending`, `Theme.danger`.
  - `Theme.lesion` is reserved for lesions in the 3D view. Never use it in UI.
  - The 3D viewer background is always dark (`Theme.viewerBackground`).
- Follow the phone's light/dark setting.
- Text: Dynamic Type text styles only (`.largeTitle`, `.headline`, `.body`, `.subheadline`, `.caption`), regular and semibold. Patient app uses `.fontDesign(.rounded)` at the phone's normal text size (no extra size boost). Numbers use `.monospacedDigit()`.
- Components: `TabView` with icons and labels; `List(.insetGrouped)`; cards via `.cardStyle()`; one `.borderedProminent` button per screen; `.searchable`; `ContentUnavailableView` for empty states; SF Symbols for icons.
- Doctor Home uses summary cards (count + 2–3 items each).
- Spacing in steps of 8 (`Theme.Spacing`); radius 12 for buttons, 16 for cards; tap targets at least 44 pt.

## AI content labels
Reports and findings show their real verification state, never invented confidence scores:
- **Verified reference:** linked to an identifiable clinical source
- **Requires review:** AI-generated, needs clinician verification
- **AI-generated:** not yet approved

## Accessibility
Every interactive element has an accessibility label. Support Dynamic Type without truncation, VoiceOver and WCAG AA contrast. Plain language on patient screens.

## Rules
- Keep model names and JSON fields identical to the Android app and `../shared/mock-data`.
- Use realistic but fake mock data. Never real patient data.
- Never commit scan files (.nii.gz, DICOM), signing files or credentials.
- Explain the plan before large changes; prefer small, focused edits that build.
- After changes, build the project and fix compile errors before finishing.

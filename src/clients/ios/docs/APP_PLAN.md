# HoloRepository Mobile App: Feature Plan

## Overview

- **Purpose:** check that the planned app matches the project vision before prompting Claude to build the foundation.
- **Platforms:** native iOS (Swift / SwiftUI) and Android (Kotlin / Jetpack Compose).
- **Two deadlines:**
  - **December:** the full app flow and UI components in place for the prototype demo (mock data).
  - **March:** the finished app with advanced features connected to the real backend.
- **Two roles:** doctor / clinician and patient. Each has its own dashboard and bottom navigation.
- **Imaging scope: CT only.** The daemon segments CT scans (DICOM) with NVIDIA NV-Segment-CT, so every study in the app is a CT with a 3D model. The brief only requires DICOM in general; other modalities can be added later (the `Modality` type is kept for that).

---

## Design and UI

### Style
- **Minimal, native iOS:** standard SwiftUI components, plenty of white space, no gradients or heavy shadows.
- **Theme:** follows the phone's light / dark setting. The 3D viewer background is always dark.
- **Devices for December:** iPhone. Android follows the same design; tablet layouts come later.

### Colour
| Use | Light | Dark |
| --- | --- | --- |
| Doctor accent (blue) | #2F6FD6 | #5B93EE |
| Patient accent (teal) | #13877F | #3CC4B8 |
| Background | #F7F9FC | #0F1520 |
| Cards | #FFFFFF | #182131 |
| Card border | #E3E8EF | #263246 |
| 3D viewer background | #0E1420 | #070A10 |
| Approved / verified | #2E9E5B | #5FCB8A |
| Requires review / AI draft | #D99A1E | #F0B94A |
| Errors | #C8382F | #F07468 |
| Lesion (3D view only, never UI) | #FF2D95 | #FF2D95 |

- The accent is used only for the main action, links and the selected tab. Everything else is neutral.
- Status colours are used only where they carry meaning.

### Text
- **Doctor app:** SF Pro (system font), Dynamic Type text styles, regular and semibold only.
- **Patient app:** SF Pro Rounded at the phone's normal text size, plain language.
- Numbers (volumes, dates) use monospaced digits.

### Components and layout
- **Bottom tabs:** icons with text labels, 4 tabs per role.
- **Doctor Home:** summary cards (pending tasks, MDT sessions, recent activity), each with a count and 2–3 items.
- **3D viewer (phone):** the model fills the screen; structures, markers and notes open in a bottom sheet.
- **Patient view button:** on the doctor's 3D viewer, switches the same phone to the simple patient view to show a patient their scan in clinic.
- **Lists:** inset grouped lists. **Cards:** radius 16, thin border. **Buttons:** radius 12, one main button per screen.
- **Spacing:** steps of 8 (8, 16, 24, 32).
- **Icons:** SF Symbols.
- **Tap targets:** at least 44 pt.

---

## 1. Sign-in and role access

### Flow
1. **Login page:** choose "Doctor / Clinician" or "Patient".
2. **Sign in:**
   - Doctor: email and password.
   - Patient: registered credentials.
3. **Authentication and role check:** the backend verifies the account's identity, status and permissions.
4. **Dashboard:** opens the doctor or patient dashboard.

### Patient account creation
1. A hospital or clinical administrator creates the patient account.
2. The patient receives an invitation (SMS or email).
3. The patient activates their account.
4. The patient signs in.

> Patient access must be linked to a **verified patient identity and the correct clinical record**, not just an email address. This matters when two patients share a name, or when family members share an email account.

### December
- Login page with role choice
- Sign-in screens (doctor: email and password; patient: registered credentials)
- Role-based routing to the correct dashboard
- Patient invitation and activation screens

### March
- Biometric login (Face ID / Android BiometricPrompt)
- Multi-factor authentication for doctors
- Password reset through verified contact details
- Account lockout after repeated failed attempts
- Invitation expiry and resend
- Audit logs for account creation and access

---

## 2. Doctor / clinician app

**Bottom navigation:** Home · Patients · Scans · Video Meeting · Reports

- **Scans** is the Imaging feature (section 2.3), renamed in the tab bar and title.
- **Video Meeting** lists the active and upcoming MDT sessions. Each opens the MDT session detail: name, time, status, place, participants and cases (each case opens the 3D viewer). "Join video call" says video joining arrives in March (Webex).
- Meetings also appear on Home and in a patient's consultation history (Patients).

### 2.1 Home
In this order, in one scrolling page:
1. Header: "Good morning, Dr Sarah" (by time of day) and today's date; notification bell with unread dot (opens the notifications list) and initials avatar (opens Profile)
2. Active MDT session: the live or starting-soon session, with status (Live / Starting soon / Scheduled, from the times), participants and number of cases; "Join session" when live, otherwise "View session"
3. Pending tasks: four filter chips (Not started · In progress · Urgent · Completed), up to 3 tasks, "See all" opens the full task list
4. Upcoming meetings: up to 3 sessions with date blocks, "See more" opens Video Meeting

**Tasks** have a status (not started, in progress, completed) and a separate priority (normal, urgent). "Urgent" shows urgent tasks of any status except completed. Kinds: review scan, review annotations, approve clinical report, accept MDT invitation, prepare MDT case. Each opens an existing screen (3D viewer, report detail or MDT session detail). Notifications stay separate from tasks.

### 2.2 Patients
- Search patients
- View assigned patients
- Access clinical records
- Browse each patient's scan history
- Patient-specific consultation history

### 2.3 Imaging
- CT study list with search (patient or exam); no modality filter. Every study is 3D-ready.
- 3D viewer (WebView): rotate, zoom with limits, reset
- Structures: show/hide, isolate, volumes, opacity. Structures are NV-Segment-CT labels, grouped as lesions, organs, vessels, bones and muscles.
- Tap the model → add a marker → add a comment (saved on the device)
- "Share with patient" on a marker
- "Patient view" button: switches the viewer to the simple patient view on the same phone
- Sync markers to the MDT session

### 2.4 Reports
- AI-generated MDT summaries
- Clinical reports
- Patient-friendly explanations
- Report editing and approval

### March
- **Live annotations:** view annotations from other clinicians in real time.
- **AI clinical assistant and reporting:**
  - Consultation transcription
  - AI clinical summary
  - Report viewing, editing and approval
  - SNOMED-CT references
  - NICE guideline references
  - Patient summary
  - Evidence traceability
  - AI verification interface, with three labels:
    - **Verified reference:** linked to an identifiable clinical source.
    - **Requires review:** an AI-generated finding that needs clinician verification.
    - **AI-generated:** automatically generated content that hasn't been approved yet.

    These labels show real verification states, not invented AI confidence scores.
- **Doctor profile and settings:**
  - Professional role and department
  - Account security and biometric authentication
  - Notification preferences
  - Device management
  - Connected hospital / edge server status
  - Log out

---

## 3. Patient app

**Bottom navigation:** Home · Reports · Scans · Messages · Profile

Appointments has no tab: it opens from Home (Quick access and the Upcoming appointment card) and from search. Messages, Family Access, AI Assistant and patient notifications open a shared "Coming soon" screen until they're built.

> Patients only see scans, annotations and reports that have been authorised for patient access.

### 3.1 Home
In this order, in one scrolling page:
1. Header: app name, notification bell, initials avatar (opens Profile); greeting by time of day ("Good morning, Alex") and "What would you like to find today?"
2. Search bar: searches the patient's own approved reports, scans and appointments, grouped by type (messages and medical history later)
3. Quick access (3 × 2 icons, no cards): Reports, Scans, Appointments / Messages, Family Access, AI Assistant
4. Your latest results: the newest scan with an approved report, "Ready" badge, easy-read explanation, "Understand my results"
5. Upcoming appointment: date block, clinician, time and department

### 3.2 My Scans
1. Scans library (CT scans; each one has a 3D model)
2. Select an available scan
3. Scan overview: date, body part, short description
4. Embedded 3D viewer (WebView)
5. Guided exploration
6. View the approved report

### 3.3 My Reports
1. Patient-friendly explanations
2. Report history
3. View the linked scan
4. Share or export as PDF (WhatsApp or email)

### 3.4 Appointments
1. Upcoming and previous appointments
2. Appointment date, time and department
3. Appointment details and reminders
4. Relevant consultation information

### March
- **Notifications** for new scans, approved reports and upcoming appointments, without any sensitive medical information.
- **Family and caregiver access:**
  - Invite trusted family members or carers
  - Accept invitations
  - Choose what is shared: selected scans, approved reports, appointment information
  - Set access levels and expiry dates
  - Revoke access
- **Patient profile and settings:**
  - Personal details and verified contact information
  - Biometric login
  - Notification preferences
  - Accessibility and language settings
- **Accessibility:** adjustable text size, clear language, screen-reader support, simple navigation.

---

## 4. Open questions

- Who builds the authentication backend: invitations, MFA, lockout and audit logs?
- How are SMS and email invitations sent, given the on-premise, no-cloud design?
- Is sharing reports by PDF through WhatsApp or email allowed? It moves patient data outside hospital systems, so check with Dr Rafiq.
- Marker sync to MDT sessions needs the daemon's WebSocket. Is it ready by December, or does it move to March?
- Where does appointment data come from? Hospital booking systems aren't part of the brief, so it may stay as mock data.
- Family and caregiver access: what consent and safeguarding rules apply?
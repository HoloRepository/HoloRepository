<img src="docs/branding/app-icon-source.png" alt="HoloRepository app icon" width="128">

# HoloRepository for macOS

The macOS app for HoloRepository 2027. It opens CT and MR scans saved as NIfTI and shows them as 2D slices and a 3D view side by side.

## Features

- Open NIfTI files (`.nii`, `.nii.gz`).
- See axial, coronal and sagittal slices and a 3D view together.

## Run it

1. Open `HoloRepository2027.xcodeproj` in Xcode (version 26 or later, on macOS 15 or later).
2. Press **⌘R**.

## How to use it

- **Open a scan:** click **Open…** (⌘O) and choose a NIfTI file.

## App icon

The icon artwork is `docs/branding/app-icon-source.png`. To change it, replace that file and run `python3 scripts/make_app_icon.py`, which needs Pillow (`pip install pillow`). The script makes every icon size Xcode needs.

## How it works

The window and buttons are written in Swift (SwiftUI). The scan itself is drawn by [NiiVue](https://niivue.com), a medical image viewer running inside the app. The Windows app can reuse the same viewer, in the `WebViewer/` folder.

## Privacy

Scans are always read from where they already are, such as a hospital drive or a USB stick. The app never copies them and never sends them anywhere.

## Credits

- [NiiVue](https://github.com/niivue/niivue) draws the scans (BSD 2-Clause licence). [dcm2niix](https://github.com/rordenlab/dcm2niix) converts DICOM for it (BSD licence). Their licences, and those of the libraries bundled inside them, are in `WebViewer/vendor/`.

## Coming next

- Open DICOM files and folders.
- Viewing tools: layouts, contrast presets, measuring and 3D options.
- Sign in to the HoloRepository server.
- Send scans to the server for segmentation, then show the organs it finds in 2D and 3D.
- Unit tests.

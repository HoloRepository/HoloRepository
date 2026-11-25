<img src="docs/branding/app-icon-source.png" alt="HoloRepository app icon" width="128">

# HoloRepository for macOS

The macOS app for HoloRepository 2027. This first version is the empty app with its icon; the scan viewer comes next.

## Run it

1. Open `HoloRepository2027.xcodeproj` in Xcode (version 26 or later, on macOS 15 or later).
2. Press **⌘R**.

## App icon

The icon artwork is `docs/branding/app-icon-source.png`. To change it, replace that file and run `python3 scripts/make_app_icon.py`, which needs Pillow (`pip install pillow`). The script makes every icon size Xcode needs.

## Coming next

- A scan viewer built on [NiiVue](https://niivue.com).
- Open DICOM and NIfTI scans.

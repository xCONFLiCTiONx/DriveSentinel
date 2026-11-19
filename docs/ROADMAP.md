# DriveSentinel roadmap

## Implemented foundation
- WPF dashboard for volumes exposed by Windows DriveInfo.
- Shows volume name, label, reported drive type, capacity, free space, and filesystem.
- Per-volume Watch setting stored under the current user's LocalAppData.
- One-minute refresh of basic drive readiness and capacity only. This is inventory polling, not a disk scan.
- System tray icon with open, refresh, and exit actions.
- Explicit confirmation before a manual directory-entry accessibility check.
- No automatic file scans, sector scans, repairs, formatting, or file modifications.

## Next milestones
1. Physical disk inventory and stable device identity, including mapping logical volumes to physical devices.
2. SMART / NVMe health and temperature readings through supported Windows storage APIs and vendor/controller paths. Unknown or unsupported data must remain Unavailable, never be inferred as healthy.
3. Persistent health history, threshold-based notifications, and per-device watch schedules.
4. Manual filesystem diagnostics with clear privilege and filesystem requirements.
5. Optional manual read-only surface scan, with estimated duration, cancellation, and warnings about workload.
6. Windows Service for background telemetry only, separated from the interactive WPF tray process.
7. Installer, service lifecycle controls, signed release workflow, and automated tests.

## Important distinction
DriveInfo.DriveType describes the volume as Windows exposes it. USB-attached HDDs may appear as Fixed; physical bus type cannot be reliably inferred from this property alone. Physical-device discovery is a separate planned milestone.

The current Manual file-system access check walks directory entries and counts enumeration errors. It is not a SMART test, filesystem repair, file-content checksum, or physical-sector surface scan. Access errors can be normal permission/locking behavior and must not be treated as definitive evidence of drive failure.

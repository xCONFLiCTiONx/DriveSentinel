# DriveSentinel

DriveSentinel is a native Windows drive health, SMART telemetry, and failure monitoring application for NVMe SSDs, SATA SSDs, internal/external HDDs, and USB storage devices.

Built with **C# / .NET 10 / WPF** with 100% native Windows storage IOCTLs and COM reflection. **Zero third-party NuGet dependencies.**

---

## Architecture & Hardware Interop

DriveSentinel communicates directly with Windows kernel storage drivers to retrieve low-level disk diagnostics:

1. **NVMe Controller Health Information:**
   * Queries `IOCTL_STORAGE_QUERY_PROPERTY` using `StorageAdapterProtocolSpecificProperty` and `StorageDeviceProtocolSpecificProperty` with `ProtocolTypeNvme`.
   * Directly extracts the 512-byte `NVME_HEALTH_INFO_LOG` directly from the NVMe controller.
   * Telemetry points: Available Spare, Available Spare Threshold, Percentage Used (Endurance Wear), Composite Temperature, Media/Integrity Errors, Critical Warnings, Error Information Log Entries, Data Units Read/Written (TBW), Power Cycles, Power-On Hours, and Unsafe Shutdowns.

2. **ATA / SATA SMART Telemetry:**
   * Reads raw, normalized, worst-case, and manufacturer failure threshold values via in-process COM reflection (`WbemScripting.SWbemLocator`) querying `MSStorageDriver_FailurePredictData` and `MSStorageDriver_FailurePredictThresholds`.
   * For USB-attached bridges (Realtek RTL9210, ASMedia, JMicron), executes SAT-16 / SAT-12 (SCSI-to-ATA Translation) pass-through commands (`IOCTL_SCSI_PASS_THROUGH_DIRECT`) with ATA opcode `0x85`/`0xA1` to extract raw ATA SMART data directly from drives behind bridge controllers.

3. **Drive & Volume Mapping:**
   * Automatically correlates physical hardware drives (`\\.\PhysicalDrive0`, `1`, etc.) to logical drive letters (`C:`, `D:`, etc.) via `IOCTL_STORAGE_GET_DEVICE_NUMBER`.
   * Detects underlying media type (NVMe SSD, SATA SSD, HDD) via seek penalty inspection (`StorageDeviceSeekPenaltyProperty`).

4. **Correlated Windows Storage Event Logs:**
   * Queries the Windows System Event Log for kernel storage driver events:
     * **Event ID 7 (`disk`):** Physical bad block detected.
     * **Event ID 51 (`disk`):** Paging error during write/read.
     * **Event ID 129 (`stornvme` / `storahci`):** Controller reset issued due to unresponsive hardware.
     * **Event ID 153 (`disk`):** I/O request timed out and was retried.
     * **Event ID 55 / 98 (`ntfs`):** File system metadata corruption or repair needed.

---

## Diagnostic Guide & Technical Notes

### Critical SMART Attributes Reference

| ID | Attribute Name | Type | Technical Impact & Diagnostic Significance |
|---|---|---|---|
| **05** | Reallocated Sectors Count | Critical | Bad sectors retired and remapped to spare sectors. Any non-zero or increasing count indicates active surface degradation. |
| **10** | Spin Retry Count | Critical | Count of failed spindle motor spin-up attempts. Indicates mechanical motor failure on HDDs. |
| **171** | SSD Program Fail Count | Critical | Count of failed flash write operations. Indicates dying NAND blocks. |
| **172** | SSD Erase Fail Count | Critical | Count of failed flash block erase operations. Dying NAND blocks. |
| **180** | Unused Reserved Block Count | Critical | Remaining factory reserve flash blocks. When this hits zero, the SSD locks into read-only mode. |
| **184** | End-to-End Error Count | Critical | Data parity errors detected between host cache and internal drive controller memory. |
| **187** | Reported Uncorrectable Errors | Critical | Read errors that could not be recovered using hardware ECC. High statistical indicator of imminent drive failure. |
| **188** | Command Timeout | Warning | Number of aborted commands due to drive communication timeouts. Indicates controller freezes or cable faults. |
| **190 / 194** | Operating Temperature | Thermal | Internal physical sensor temperature. Sustained temperatures > 65°C accelerate NAND degradation and magnetic surface decay. |
| **197** | Current Pending Sector Count | Critical | Unstable sectors awaiting reallocation after failing reads. Data in these sectors cannot be safely read until rewritten. |
| **198** | Offline Uncorrectable Sectors | Critical | Uncorrectable errors encountered during offline background surface scans. Data loss has already occurred. |
| **199** | UltraDMA CRC Error Count | Interface | Interface cable communication errors. Typically caused by loose, damaged, or poor-quality SATA cables or port contacts. |
| **202 / 231** | Percentage Lifetime Used / Life Left | Endurance | Percentage of manufacturer-rated write endurance consumed or remaining. Reaching 100% wear means write warranty specification is exhausted. |
| **241 / 242** | Total LBAs Written (TBW) / Read | Workload | Total cumulative 512-byte blocks written and read over the lifespan of the storage device. |

---

### NVMe Health Log Reference

* **Available Spare:** Remaining spare memory blocks. When Available Spare falls to or below the manufacturer threshold, the SSD controller will enter read-only mode to prevent data corruption.
* **Percentage Used:** Estimate of endurance consumed. Reaching 100% indicates that the total rated terabytes written (TBW) has been exhausted.
* **Media and Data Integrity Errors:** Occurrences where unrecoverable data integrity errors occurred. Any non-zero count means uncorrectable NAND degradation.
* **Error Information Log Entries:** Cumulative count of controller error log events (timeouts, protocol errors, internal hardware anomalies).

---

## Configuration & Settings

Settings are stored locally in `%LOCALAPPDATA%\DriveSentinel\watch-settings.json`.

* **Hardware Telemetry Check Interval:** Polling frequency for background telemetry evaluation (5, 10, 15, 30, or 60 minutes).
* **Enable Failure Alerts:** Toggles evaluation and logging of failure risk indicators across all physical drives.
* **Show Desktop Notifications:** Toggles system tray notification popups when a drive enters Warning or Critical status.
* **Temperature Warning Threshold:** Temperature above which a drive is flagged as overheating (default 65°C).
* **Low Free Space Warning:** Free space percentage below which volume space warnings are triggered (default 10%).
* **Start DriveSentinel with Windows:** Registers the application in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and synchronizes elevated logon execution without UAC interruption.

---

## Building and Running

### Prerequisites
* Windows 10 / 11 x64
* .NET 10 SDK
* Administrator privileges (requested via `app.manifest` for raw storage device handle access)

### Commands
```powershell
# Build Debug
dotnet build .\src\DriveSentinel.App\DriveSentinel.App.csproj -c Debug

# Build Release
dotnet build .\src\DriveSentinel.App\DriveSentinel.App.csproj -c Release

# Run DriveSentinel
dotnet run --project .\src\DriveSentinel.App\DriveSentinel.App.csproj -c Release
```

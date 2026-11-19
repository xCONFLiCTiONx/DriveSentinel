using System;
using System.Collections.Generic;

namespace DriveSentinel;

public enum FailureSeverity
{
    None = 0,
    Notice = 1,
    AttentionNeeded = 2,
    Warning30Days = 3,
    CriticalImmediate = 4
}

public sealed class DriveFailurePrediction
{
    public bool IsFailurePredicted => Severity >= FailureSeverity.Warning30Days;
    public FailureSeverity Severity { get; set; } = FailureSeverity.None;
    public string Title { get; set; } = "Healthy";
    public string Summary { get; set; } = "No early failure indicators detected. Drive is operating within safe parameters.";
    public int? EstimatedDaysToFailure { get; set; }
    public string RecommendedAction { get; set; } = "Continue regular monitoring and periodic backups.";
    public List<string> RiskFactors { get; set; } = new();

    public string BadgeText => Severity switch
    {
        FailureSeverity.CriticalImmediate => "CRITICAL",
        FailureSeverity.Warning30Days => "FAILING",
        FailureSeverity.AttentionNeeded => "ATTENTION NEEDED",
        FailureSeverity.Notice => "NOTICE",
        _ => "HEALTHY"
    };

    public string BadgeBrush => Severity switch
    {
        FailureSeverity.CriticalImmediate => "#EF4444", // Bright red
        FailureSeverity.Warning30Days => "#F97316",     // Orange
        FailureSeverity.AttentionNeeded => "#FBBF24",   // Amber / Yellow
        FailureSeverity.Notice => "#60A5FA",          // Blue
        _ => "#22C55E"                                // Green
    };

    public string BadgeForeground => Severity switch
    {
        FailureSeverity.CriticalImmediate => "#FFFFFF",
        FailureSeverity.Warning30Days => "#FFFFFF",
        FailureSeverity.AttentionNeeded => "#0F172A",
        FailureSeverity.Notice => "#FFFFFF",
        _ => "#0A1E14"
    };
}

public sealed class PhysicalDriveInfo
{
    public int DiskIndex { get; set; }
    public string DevicePath => @"\\.\PhysicalDrive" + DiskIndex;
    public string Model { get; set; } = "Unknown Drive";
    public string Vendor { get; set; } = "";
    public string SerialNumber { get; set; } = "Unavailable";
    public string BusType { get; set; } = "Unknown";
    public string MediaType { get; set; } = "Unknown";
    public bool IsVirtualDisk { get; set; }
    public bool HasSatPassThrough { get; set; }

    // Windows storage events are risk signals, not a complete health measurement.
    public bool HasHardwareHealthTelemetry =>
        !IsVirtualDisk &&
        (SmartAttributes.Count > 0 ||
         TemperatureC.HasValue ||
         LifeRemainingPercent.HasValue ||
         AvailableSparePercent.HasValue ||
         ReallocatedSectors.HasValue ||
         PendingSectors.HasValue ||
         UncorrectableErrors.HasValue ||
         MediaErrors.HasValue);

    public long SizeBytes { get; set; }
    public string SizeLabel { get; set; } = "—";
    public List<string> MountedVolumes { get; set; } = new();
    public string VolumesSummary { get; set; } = "None";

    public string HealthStatus { get; set; } = "Unknown";
    public int HealthScore { get; set; } = 0;
    public string HealthScoreLabel => !HasHardwareHealthTelemetry ? "N/A" : $"{HealthScore}%";

    public string HealthScoreBrush => !HasHardwareHealthTelemetry ? "#AAB5C5" : HealthScore switch
    {
        >= 90 => "#22C55E", // Green
        >= 70 => "#FBBF24", // Yellow/Amber
        >= 40 => "#F97316", // Orange
        _ => "#EF4444"      // Red
    };

    public DriveFailurePrediction Prediction { get; set; } = new();

    public int? TemperatureC { get; set; }
    public string TemperatureLabel => TemperatureC.HasValue ? $"{TemperatureC.Value}°C" : "—";
    public string TemperatureBrush => TemperatureC switch
    {
        null => "#AAB5C5",
        >= 70 => "#EF4444",
        >= 55 => "#FBBF24",
        _ => "#22C55E"
    };

    public int? LifeRemainingPercent { get; set; }
    public string LifeRemainingLabel => LifeRemainingPercent.HasValue ? $"{LifeRemainingPercent.Value}% life remaining" : "Unavailable";
    public string LifeRemainingBrush => LifeRemainingPercent switch
    {
        null => "#AAB5C5",
        <= 10 => "#EF4444",
        <= 30 => "#F97316",
        <= 60 => "#FBBF24",
        _ => "#22C55E"
    };

    public int? AvailableSparePercent { get; set; }
    public string AvailableSpareLabel => AvailableSparePercent.HasValue ? $"{AvailableSparePercent.Value}% available spare" : "—";

    public long? PowerOnHours { get; set; }
    public string PowerOnHoursLabel
    {
        get
        {
            if (!PowerOnHours.HasValue) return "—";
            var hours = PowerOnHours.Value;
            var years = hours / (24.0 * 365.25);
            return years >= 0.1 ? $"{hours:N0} hrs (~{years:0.#} yrs)" : $"{hours:N0} hrs";
        }
    }

    public long? PowerCycles { get; set; }
    public string PowerCyclesLabel => PowerCycles.HasValue ? $"{PowerCycles.Value:N0} cycles" : "—";

    public ulong? TotalBytesWritten { get; set; }
    public string TotalBytesWrittenLabel => TotalBytesWritten.HasValue ? FormatBytes((long)TotalBytesWritten.Value) : "—";

    public ulong? TotalBytesRead { get; set; }
    public string TotalBytesReadLabel => TotalBytesRead.HasValue ? FormatBytes((long)TotalBytesRead.Value) : "—";

    public ulong? UnsafeShutdowns { get; set; }
    public ulong? MediaErrors { get; set; }
    public long? ReallocatedSectors { get; set; }
    public long? PendingSectors { get; set; }
    public long? UncorrectableErrors { get; set; }
    public long? CrcErrors { get; set; }

    public List<SmartAttributeItem> SmartAttributes { get; set; } = new();
    public List<StorageEvent> StorageEvents { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    public string KeyRiskSummary
    {
        get
        {
            if (IsVirtualDisk)
            {
                return "None (Virtual Disk)";
            }

            var parts = new List<string>();
            if (ReallocatedSectors is > 0) parts.Add($"Reallocated sectors: {ReallocatedSectors}");
            if (PendingSectors is > 0) parts.Add($"Pending sectors: {PendingSectors}");
            if (UncorrectableErrors is > 0) parts.Add($"Uncorrectable errors: {UncorrectableErrors}");
            if (MediaErrors is > 0) parts.Add($"Media errors: {MediaErrors}");
            if (AvailableSparePercent is <= 20) parts.Add($"Available spare: {AvailableSparePercent}%");
            if (CrcErrors is > 0) parts.Add($"CRC errors: {CrcErrors}");

            var faults = StorageEvents.Count(e => e.Id == "129" || e.Id == "7" || e.Id == "11" || e.Id == "51");
            if (faults > 0) parts.Add($"Storage hardware faults: {faults}");

            var retries = StorageEvents.Count(e => e.Id == "153");
            if (retries >= 5) parts.Add($"I/O retry events: {retries}");

            return parts.Count > 0 ? string.Join(" · ", parts) : "No bad sectors or media errors reported";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}

public sealed class SmartAttributeItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Current { get; set; }
    public int Worst { get; set; }
    public int Threshold { get; set; }
    public long RawValue { get; set; }
    public string RawDisplay { get; set; } = "";
    public string Status { get; set; } = "OK";
    public string StatusBrush => Status switch
    {
        "Critical" => "#EF4444",
        "Warning" => "#F97316",
        _ => "#22C55E"
    };
    public bool IsCritical { get; set; }
    public string Description { get; set; } = "";
    public string Impact { get; set; } = "";
}

public sealed class StorageEvent
{
    public string Time { get; set; } = "";
    public string Source { get; set; } = "";
    public string Level { get; set; } = "";
    public string Id { get; set; } = "";
    public string Message { get; set; } = "";
    public string LevelBrush => Level switch
    {
        "Critical" => "#EF4444",
        "Error" => "#F97316",
        "Warning" => "#FBBF24",
        _ => "#AAB5C5"
    };
}

public sealed class DriveHealthReport
{
    public string Title { get; set; } = "Drive health report";
    public string OverallStatus { get; set; } = "Unknown";
    public string Summary { get; set; } = "";
    public DateTime CollectedAt { get; set; } = DateTime.Now;
    public List<HealthSection> Sections { get; set; } = new();
    public List<StorageEvent> Events { get; set; } = new();
    public string Limitations { get; set; } = "";
}

public sealed class HealthSection
{
    public string Title { get; set; } = "";
    public List<HealthMetric> Metrics { get; set; } = new();
}

public sealed class HealthMetric
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Note { get; set; } = "";
}

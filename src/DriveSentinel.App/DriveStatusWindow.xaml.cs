using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Clipboard = System.Windows.Clipboard;

namespace DriveSentinel;

public partial class DriveStatusWindow : Window
{
    private readonly int _diskIndex;
    private readonly WatchSettings _settings = WatchSettings.Load();
    private PhysicalDriveInfo? _drive;

    public DriveStatusWindow(PhysicalDriveInfo drive)
    {
        InitializeComponent();
        _drive = drive;
        _diskIndex = drive.DiskIndex;
        WatchSettings.ApplyPlacement(this, _settings.StatusWindowBounds, 980, 630);
        Loaded += async (_, _) => await LoadReportAsync();
    }

    public DriveStatusWindow(DriveRow volumeDrive)
    {
        InitializeComponent();
        var mapping = StorageInterop.GetVolumeDiskNumber(volumeDrive.RootPath);
        _diskIndex = mapping?.DiskNumber ?? 0;
        WatchSettings.ApplyPlacement(this, _settings.StatusWindowBounds, 980, 630);
        Loaded += async (_, _) => await LoadReportAsync();
    }

    public DriveStatusWindow(int diskIndex)
    {
        InitializeComponent();
        _diskIndex = diskIndex;
        WatchSettings.ApplyPlacement(this, _settings.StatusWindowBounds, 980, 630);
        Loaded += async (_, _) => await LoadReportAsync();
    }

    private async void RefreshStatus_Click(object sender, RoutedEventArgs e) => await LoadReportAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task LoadReportAsync()
    {
        RefreshStatusButton.IsEnabled = false;
        try
        {
            var drive = await Task.Run(() => DriveTelemetryService.QuerySingleDrive(_diskIndex, null));
            if (drive != null)
            {
                _drive = drive;
                PopulateUi(drive);
            }
            else
            {
                DriveTitle.Text = $"Physical Drive {_diskIndex}";
                DriveSubtitle.Text = "Telemetry unavailable for this drive.";
            }
        }
        catch (Exception ex)
        {
            DriveSubtitle.Text = $"Telemetry unavailable: {ex.Message}";
        }
        finally
        {
            RefreshStatusButton.IsEnabled = true;
        }
    }

    private void PopulateUi(PhysicalDriveInfo drive)
    {
        // Title & Header
        DriveTitle.Text = $"{drive.Model} (Drive {drive.DiskIndex})";
        BusTypeText.Text = $"{drive.BusType} · {drive.MediaType}";
        DriveSubtitle.Text = $"Serial: {drive.SerialNumber} · Capacity: {drive.SizeLabel} · Volumes: {drive.VolumesSummary}";
        CollectedText.Text = $"Updated {drive.LastUpdated:yyyy-MM-dd HH:mm:ss} · Direct Windows IOCTL telemetry";
        Title = $"Drive Health — {drive.Model} — {drive.HealthStatus} ({drive.HealthScoreLabel})";

        // Health Score
        HealthScoreNumber.Text = drive.HealthScoreLabel;
        HealthScoreStatus.Text = drive.HealthStatus.ToUpperInvariant();
        var scoreBrush = new BrushConverter().ConvertFromString(drive.HealthScoreBrush) as Brush ?? Brushes.Green;
        HealthScoreNumber.Foreground = scoreBrush;
        HealthScoreStatus.Foreground = scoreBrush;

        // Prediction Banner
        var pred = drive.Prediction;
        var badgeBrush = new BrushConverter().ConvertFromString(pred.BadgeBrush) as Brush ?? Brushes.Green;
        PredictionBadgeBorder.Background = badgeBrush;
        PredictionBadgeText.Text = pred.BadgeText;
        PredictionBadgeText.Foreground = new BrushConverter().ConvertFromString(pred.BadgeForeground) as Brush ?? Brushes.White;

        PredictionSummaryText.Text = pred.Summary;
        PredictionActionText.Text = $"Action: {pred.RecommendedAction}";
        RiskFactorsList.ItemsSource = pred.RiskFactors;

        if (drive.IsVirtualDisk || (!drive.HasHardwareHealthTelemetry && pred.Severity < FailureSeverity.AttentionNeeded))
        {
            PredictionBanner.Background = (Brush)new BrushConverter().ConvertFromString("#202B3A")!;
            PredictionBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#64748B")!;
            PredictionNoticeTime.Text = drive.IsVirtualDisk
                ? "Host physical-disk health not verified"
                : "Health telemetry unavailable — status unverified";
            PredictionNoticeTime.Foreground = (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!;
        }
        else if (pred.Severity >= FailureSeverity.Warning30Days)
        {
            PredictionBanner.Background = (Brush)new BrushConverter().ConvertFromString("#2A1215")!;
            PredictionBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#EF4444")!;
            PredictionNoticeTime.Text = pred.EstimatedDaysToFailure.HasValue
                ? $"⚠️ Projected ~{pred.EstimatedDaysToFailure.Value} days remaining"
                : "⚠️ Active drive degradation detected";
            PredictionNoticeTime.Foreground = (Brush)new BrushConverter().ConvertFromString("#F87171")!;
        }
        else if (pred.Severity == FailureSeverity.AttentionNeeded)
        {
            PredictionBanner.Background = (Brush)new BrushConverter().ConvertFromString("#292110")!;
            PredictionBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#FBBF24")!;
            PredictionNoticeTime.Text = "Early degradation warning";
            PredictionNoticeTime.Foreground = (Brush)new BrushConverter().ConvertFromString("#FCD34D")!;
        }
        else
        {
            PredictionBanner.Background = (Brush)new BrushConverter().ConvertFromString("#13271E")!;
            PredictionBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#22C55E")!;
            PredictionNoticeTime.Text = "Safe operating condition";
            PredictionNoticeTime.Foreground = (Brush)new BrushConverter().ConvertFromString("#86EFAC")!;
        }

        // Key Metric Cards
        MetricTemp.Text = drive.TemperatureLabel;
        MetricTemp.Foreground = new BrushConverter().ConvertFromString(drive.TemperatureBrush) as Brush ?? Brushes.White;

        MetricWear.Text = drive.LifeRemainingPercent.HasValue ? $"{drive.LifeRemainingPercent.Value}%" : "—";
        MetricWear.Foreground = new BrushConverter().ConvertFromString(drive.LifeRemainingBrush) as Brush ?? Brushes.White;

        MetricSpare.Text = drive.AvailableSparePercent.HasValue ? $"{drive.AvailableSparePercent.Value}%" : "—";
        MetricSpare.Foreground = drive.AvailableSparePercent.HasValue && drive.AvailableSparePercent.Value <= 20
            ? Brushes.Red : (Brush)new BrushConverter().ConvertFromString("#4ADE80")!;

        MetricHours.Text = drive.PowerOnHours.HasValue ? $"{drive.PowerOnHours.Value:N0} hrs" : "—";

        var badCount = (drive.ReallocatedSectors ?? 0) + (drive.PendingSectors ?? 0) + (long)(drive.MediaErrors ?? 0);
        MetricBadSectors.Text = badCount.ToString("N0");
        MetricBadSectors.Foreground = badCount > 0 ? (Brush)new BrushConverter().ConvertFromString("#EF4444")! : (Brush)new BrushConverter().ConvertFromString("#4ADE80")!;

        // Grids
        AttributesGrid.ItemsSource = drive.SmartAttributes;
        EventsGrid.ItemsSource = drive.StorageEvents;

        // Specs
        SpecModel.Text = drive.Model;
        SpecSerial.Text = drive.SerialNumber;
        SpecBus.Text = drive.BusType;
        SpecMedia.Text = drive.MediaType;
        SpecCapacity.Text = drive.SizeLabel;
        SpecVolumes.Text = string.IsNullOrWhiteSpace(drive.VolumesSummary) ? "None" : drive.VolumesSummary;
        SpecTbw.Text = drive.TotalBytesWrittenLabel;
        SpecCycles.Text = drive.PowerCyclesLabel;
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (_drive == null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"# DriveSentinel Health Report — {_drive.Model}");
        sb.AppendLine($"Collected: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"## Health Summary");
        var scoreStr = _drive.HasHardwareHealthTelemetry ? $" ({_drive.HealthScore}%)" : "";
        sb.AppendLine($"- **Status**: {_drive.HealthStatus}{scoreStr}");
        sb.AppendLine($"- **Prediction**: {_drive.Prediction.Title}");
        sb.AppendLine($"- **Summary**: {_drive.Prediction.Summary}");
        sb.AppendLine($"- **Recommended Action**: {_drive.Prediction.RecommendedAction}");
        if (_drive.Prediction.RiskFactors.Count > 0)
        {
            sb.AppendLine("- **Detected Risk Factors**:");
            foreach (var risk in _drive.Prediction.RiskFactors)
                sb.AppendLine($"  - ⚠️ {risk}");
        }
        sb.AppendLine();
        sb.AppendLine($"## Drive Specifications");
        sb.AppendLine($"- **Model**: {_drive.Model}");
        sb.AppendLine($"- **Serial Number**: {_drive.SerialNumber}");
        sb.AppendLine($"- **Bus Type**: {_drive.BusType}");
        sb.AppendLine($"- **Media Type**: {_drive.MediaType}");
        sb.AppendLine($"- **Capacity**: {_drive.SizeLabel}");
        sb.AppendLine($"- **Attached Volumes**: {_drive.VolumesSummary}");
        sb.AppendLine($"- **Temperature**: {_drive.TemperatureLabel}");
        sb.AppendLine($"- **Endurance Life Remaining**: {_drive.LifeRemainingLabel}");
        sb.AppendLine($"- **Power-On Hours**: {_drive.PowerOnHoursLabel}");
        sb.AppendLine($"- **Total Bytes Written**: {_drive.TotalBytesWrittenLabel}");
        sb.AppendLine();
        sb.AppendLine($"## SMART / NVMe Attributes ({_drive.SmartAttributes.Count} attributes)");
        sb.AppendLine("| ID | Name | Current | Worst | Thresh | Raw Value | Status | Meaning |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var attr in _drive.SmartAttributes)
        {
            sb.AppendLine($"| {attr.Id} | {attr.Name} | {attr.Current} | {attr.Worst} | {attr.Threshold} | {attr.RawDisplay} | {attr.Status} | {attr.Description} |");
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            CopyReportButton.Content = "✓ Report Copied";
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                Dispatcher.Invoke(() => CopyReportButton.Content = "Copy report");
            });
        }
        catch
        {
            // Clipboard access error
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        WatchSettings.SavePlacement(this, _settings.StatusWindowBounds);
        _settings.Save();
        base.OnClosing(e);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Clipboard = System.Windows.Clipboard;

namespace DriveSentinel;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PhysicalDriveInfo> _physicalDrives = new();
    private readonly ObservableCollection<DriveRow> _volumes = new();
    private readonly WatchSettings _settings = WatchSettings.Load();
    private readonly DispatcherTimer _watchTimer = new();
    private Forms.NotifyIcon? _trayIcon;
    private bool _closingToTray;
    private readonly Dictionary<string, (bool Ready, long FreeBytes)> _lastVolumeState = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, FailureSeverity> _lastDriveSeverities = new();
    private Forms.ToolStripMenuItem? _startupTrayMenuItem;
    private bool _initialRefreshDone;
    private bool _isInitialized;
    private WindowState _storedWindowState = WindowState.Normal;
    private DispatcherTimer? _boundsSaveDebounceTimer;

    public MainWindow() : this(false)
    {
    }

    public MainWindow(bool isSilent)
    {
        InitializeComponent();
        PhysicalDrivesGrid.ItemsSource = _physicalDrives;
        VolumesGrid.ItemsSource = _volumes;

        ConfigureTrayIcon();

        _watchTimer.Interval = TimeSpan.FromMinutes(Math.Max(5, _settings.CheckIntervalMinutes));
        _watchTimer.Tick += async (_, _) => await RunPeriodicMonitoringAsync();
        _watchTimer.Start();

        WatchSettings.ApplyPlacement(this, _settings.MainWindowBounds, 1100, 680);
        _storedWindowState = _settings.MainWindowBounds.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        if (_settings.MainWindowBounds.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        UpdateStartupState();

        LocationChanged += (_, _) => PersistWindowBounds(immediate: false);
        SizeChanged += (_, _) => PersistWindowBounds(immediate: false);

        if (isSilent)
        {
            WindowState = WindowState.Minimized;
            Visibility = Visibility.Hidden;
            _initialRefreshDone = true;
            _isInitialized = true;
            _ = RefreshAllAsync(quiet: true);
        }
        else
        {
            Loaded += async (_, _) =>
            {
                if (_settings.MainWindowBounds.IsMaximized)
                {
                    WindowState = WindowState.Maximized;
                    _storedWindowState = WindowState.Maximized;
                }

                // Delay enabling bounds persistence until window layout and display have completely stabilized
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    _isInitialized = true;
                });

                if (!_initialRefreshDone)
                {
                    _initialRefreshDone = true;
                    await RefreshAllAsync();
                }
            };
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_settings.MainWindowBounds.IsMaximized)
        {
            WindowState = WindowState.Maximized;
            _storedWindowState = WindowState.Maximized;
        }
    }

    private void ConfigureTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "DriveSentinel — Drive Health & Predictive Failure Alerting",
            Icon = File.Exists(Path.Combine(AppContext.BaseDirectory, "icon.ico"))
                ? new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "icon.ico"))
                : System.Drawing.SystemIcons.Shield,
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Dashboard", null, (_, _) => Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add("Refresh Drive Telemetry", null, (_, _) => Dispatcher.Invoke(async () => await RefreshAllAsync()));

        _startupTrayMenuItem = new Forms.ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupManager.IsStartupEnabled()
        };
        _startupTrayMenuItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            var enable = !_startupTrayMenuItem.Checked;
            StartupManager.SetStartup(enable);
            _startupTrayMenuItem.Checked = StartupManager.IsStartupEnabled();
            UpdateStartupState();
            AddLog(StartupManager.IsStartupEnabled()
                ? "Registered elevated scheduled task to start with Windows."
                : "Unregistered elevated scheduled task from Windows startup.");
        });
        menu.Items.Add(_startupTrayMenuItem);

        menu.Items.Add("Settings...", null, (_, _) => Dispatcher.Invoke(() => Settings_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit DriveSentinel", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
    }

    public void ShowDashboard()
    {
        if (!IsVisible)
        {
            Show();
        }

        var targetState = _settings.MainWindowBounds.IsMaximized || _storedWindowState == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;

        _storedWindowState = targetState;
        WindowState = targetState;

        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            var cmd = targetState == WindowState.Maximized ? 3 /*SW_MAXIMIZE*/ : 9 /*SW_RESTORE*/;
            StorageInterop.ShowWindow(helper.Handle, cmd);
            StorageInterop.SetForegroundWindow(helper.Handle);
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        if (!_initialRefreshDone)
        {
            _initialRefreshDone = true;
            _ = RefreshAllAsync();
        }
    }

    private void ExitApplication()
    {
        _closingToTray = true;
        _boundsSaveDebounceTimer?.Stop();
        _watchTimer.Stop();
        WatchSettings.SavePlacement(this, _settings.MainWindowBounds, _storedWindowState);
        _settings.Save();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            _storedWindowState = WindowState;
            PersistWindowBounds(immediate: true);
        }
    }

    private void PersistWindowBounds(bool immediate = false)
    {
        if (!_isInitialized) return;
        if (WindowState != WindowState.Minimized)
        {
            _storedWindowState = WindowState;
            WatchSettings.SavePlacement(this, _settings.MainWindowBounds, _storedWindowState);

            if (immediate)
            {
                _boundsSaveDebounceTimer?.Stop();
                _settings.Save();
            }
            else
            {
                if (_boundsSaveDebounceTimer == null)
                {
                    _boundsSaveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    _boundsSaveDebounceTimer.Tick += (_, _) =>
                    {
                        _boundsSaveDebounceTimer.Stop();
                        _settings.Save();
                    };
                }
                _boundsSaveDebounceTimer.Stop();
                _boundsSaveDebounceTimer.Start();
            }
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _boundsSaveDebounceTimer?.Stop();
        WatchSettings.SavePlacement(this, _settings.MainWindowBounds, _storedWindowState);
        _settings.Save();

        if (!_closingToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    public void UpdateStartupState()
    {
        if (_startupTrayMenuItem != null)
        {
            _startupTrayMenuItem.Checked = StartupManager.IsStartupEnabled();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new SettingsWindow(_settings);
            if (IsVisible)
            {
                window.Owner = this;
            }
            window.ShowDialog();
            UpdateStartupState();
            if (window.SettingsChanged)
            {
                _watchTimer.Interval = TimeSpan.FromMinutes(Math.Max(5, _settings.CheckIntervalMinutes));
                AddLog($"Settings updated: check interval={_settings.CheckIntervalMinutes}m, predictive alerts={_settings.EnablePredictiveAlerts}, tray notifications={_settings.ShowTrayNotifications}.");
            }
        }
        catch (Exception ex)
        {
            AddLog($"Failed to open Settings: {ex.Message}");
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async Task RunPeriodicMonitoringAsync()
    {
        await RefreshAllAsync(quiet: true);
    }

    private async Task RefreshAllAsync(bool quiet = false)
    {
        if (!quiet) AddLog("Querying hardware storage providers, NVMe health logs, and ATA SMART attributes…");

        List<PhysicalDriveInfo> drives;
        try
        {
            drives = await Task.Run(DriveTelemetryService.GetAllPhysicalDrives);
        }
        catch (Exception ex)
        {
            AddLog($"Error reading drive telemetry: {ex.Message}");
            return;
        }

        var selectedDiskIndex = (PhysicalDrivesGrid.SelectedItem as PhysicalDriveInfo)?.DiskIndex;

        _physicalDrives.Clear();
        foreach (var d in drives)
        {
            _physicalDrives.Add(d);
        }

        if (selectedDiskIndex.HasValue)
        {
            PhysicalDrivesGrid.SelectedItem = _physicalDrives.FirstOrDefault(d => d.DiskIndex == selectedDiskIndex.Value);
        }

        // Refresh Logical Volumes
        RefreshVolumes(quiet);

        // Evaluate overall system health
        EvaluateOverallSystemHealth(drives, quiet);

        DriveCountText.Text = $"{_physicalDrives.Count} physical drive(s) · {_volumes.Count} volume(s)";
        LastRefreshText.Text = $"Telemetry updated {DateTime.Now:T}";
    }

    private void EvaluateOverallSystemHealth(List<PhysicalDriveInfo> drives, bool quiet)
    {
        var criticalDrives = drives.Where(d => d.Prediction.Severity == FailureSeverity.CriticalImmediate).ToList();
        var failingDrives = drives.Where(d => d.Prediction.Severity == FailureSeverity.Warning30Days).ToList();
        var attentionDrives = drives.Where(d => d.Prediction.Severity == FailureSeverity.AttentionNeeded).ToList();

        if (criticalDrives.Count > 0)
        {
            var target = criticalDrives[0];
            SystemStatusBadge.Background = (Brush)new BrushConverter().ConvertFromString("#3B1215")!;
            SystemStatusBadge.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#EF4444")!;
            SystemStatusIcon.Text = "🔴";
            SystemStatusText.Text = $"CRITICAL FAILURE IMMINENT: {target.Model} (Drive {target.DiskIndex})";
            SystemStatusText.Foreground = (Brush)new BrushConverter().ConvertFromString("#F87171")!;

            AddLog($"CRITICAL ALERT: Drive {target.DiskIndex} ({target.Model}) has reached critical failure threshold! Immediate replacement required!");

            NotifyFailure(target);
        }
        else if (failingDrives.Count > 0)
        {
            var target = failingDrives[0];
            SystemStatusBadge.Background = (Brush)new BrushConverter().ConvertFromString("#2E1A11")!;
            SystemStatusBadge.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#F97316")!;
            SystemStatusIcon.Text = "🟠";
            SystemStatusText.Text = $"⚠️ DRIVE FAILURE PREDICTED: {target.Model} (Drive {target.DiskIndex})";
            SystemStatusText.Foreground = (Brush)new BrushConverter().ConvertFromString("#FB923C")!;

            AddLog($"WARNING ALERT: Drive {target.DiskIndex} ({target.Model}) is showing degradation patterns: {target.Prediction.Summary}");

            NotifyFailure(target);
        }
        else if (attentionDrives.Count > 0)
        {
            var target = attentionDrives[0];
            SystemStatusBadge.Background = (Brush)new BrushConverter().ConvertFromString("#292110")!;
            SystemStatusBadge.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#FBBF24")!;
            SystemStatusIcon.Text = "🟡";
            SystemStatusText.Text = $"ATTENTION NEEDED: {target.Model} (Drive {target.DiskIndex})";
            SystemStatusText.Foreground = (Brush)new BrushConverter().ConvertFromString("#FCD34D")!;

            if (!quiet) AddLog($"Notice: Drive {target.DiskIndex} ({target.Model}) has warning signs: {target.Prediction.Summary}");
        }
        else
        {
            SystemStatusBadge.Background = (Brush)new BrushConverter().ConvertFromString("#142C21")!;
            SystemStatusBadge.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#22C55E")!;
            SystemStatusIcon.Text = "🟢";
            SystemStatusText.Text = $"ALL DRIVES HEALTHY ({drives.Count} MONITORED)";
            SystemStatusText.Foreground = (Brush)new BrushConverter().ConvertFromString("#4ADE80")!;

            if (!quiet) AddLog($"SMART check complete: All {drives.Count} drive(s) healthy. No failure predicted.");
        }

        // Log stats per drive
        if (!quiet)
        {
            foreach (var d in drives)
            {
                AddLog($"DRIVE {d.DiskIndex} [{d.BusType}] {d.Model} (Serial: {d.SerialNumber}): Health={d.HealthScoreLabel}, Status={d.HealthStatus}, Temp={d.TemperatureLabel}, Life={d.LifeRemainingLabel}, POH={d.PowerOnHoursLabel}, Risks={d.KeyRiskSummary}");
            }
        }
    }

    private void NotifyFailure(PhysicalDriveInfo drive)
    {
        var prevSeverity = _lastDriveSeverities.TryGetValue(drive.DiskIndex, out var s) ? s : FailureSeverity.None;
        if (drive.Prediction.Severity != prevSeverity)
        {
            _lastDriveSeverities[drive.DiskIndex] = drive.Prediction.Severity;
            _trayIcon?.ShowBalloonTip(5000, "⚠️ Drive Failure Alert — DriveSentinel",
                $"Drive {drive.DiskIndex} ({drive.Model}): {drive.Prediction.Title}\n{drive.Prediction.Summary}\nBack up your data now!", Forms.ToolTipIcon.Warning);
        }
    }

    private void RefreshVolumes(bool quiet = false)
    {
        var found = new List<DriveRow>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var ready = drive.IsReady;
                var total = ready ? drive.TotalSize : 0;
                var free = ready ? drive.AvailableFreeSpace : 0;
                var label = ready && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.VolumeLabel : (ready ? "Local volume" : "Not ready");
                var typeLabel = drive.DriveType switch
                {
                    DriveType.Fixed => "Fixed",
                    DriveType.Removable => "Removable",
                    DriveType.Network => "Network",
                    DriveType.CDRom => "Optical",
                    _ => "Other"
                };
                var root = NormalizeRoot(drive.Name);
                found.Add(new DriveRow
                {
                    Name = drive.Name,
                    Label = label,
                    TypeLabel = typeLabel,
                    CapacityLabel = ready ? FormatBytes(total) : "—",
                    FreeLabel = ready ? FormatBytes(free) : "—",
                    FileSystem = ready ? SafeFileSystem(drive) : "—",
                    RootPath = root,
                    DriveType = drive.DriveType,
                    IsReady = ready,
                    IsWatched = _settings.WatchedRoots.Contains(root),
                    TotalBytes = total,
                    FreeBytes = free
                });

                // Check volume free space alerts
                if (_lastVolumeState.TryGetValue(root, out var prev))
                {
                    if (prev.Ready && !ready)
                        AddLog($"ALERT: Volume {root} ({label}) is no longer accessible.");
                    if (ready && total > 0)
                    {
                        var freeRatio = (double)free / total;
                        var prevRatio = prev.Ready && total > 0 ? (double)prev.FreeBytes / total : 1.0;
                        if (freeRatio < 0.05 && prevRatio >= 0.05)
                            AddLog($"WARNING: {root} has less than 5% free space ({FormatBytes(free)}).");
                    }
                }
                _lastVolumeState[root] = (ready, free);
            }
            catch { }
        }

        _volumes.Clear();
        foreach (var row in found) _volumes.Add(row);
    }

    private static string SafeFileSystem(DriveInfo drive)
    {
        try { return drive.DriveFormat; }
        catch { return "Unavailable"; }
    }

    private static string NormalizeRoot(string root) => root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private PhysicalDriveInfo? SelectedPhysicalDrive => PhysicalDrivesGrid.SelectedItem as PhysicalDriveInfo;

    private void PhysicalDrivesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedPhysicalDrive is { } drive)
        {
            AddLog($"SELECTED: Drive {drive.DiskIndex} · {drive.Model} ({drive.MediaType}) · Health: {drive.HealthScoreLabel} ({drive.HealthStatus}) · Prediction: {drive.Prediction.Title}");
        }
    }

    private void PhysicalDrivesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedPhysicalDrive is { } drive)
        {
            OpenDriveStatus(drive);
        }
    }

    private void ViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPhysicalDrive is { } drive)
        {
            OpenDriveStatus(drive);
            return;
        }

        if (VolumesGrid.SelectedItem is DriveRow volume)
        {
            var window = new DriveStatusWindow(volume) { Owner = this };
            window.Show();
            return;
        }

        if (_physicalDrives.Count > 0)
        {
            OpenDriveStatus(_physicalDrives[0]);
            return;
        }

        AddLog("Select a physical drive from the list above to inspect telemetry details.");
    }

    private void OpenDriveStatus(PhysicalDriveInfo drive)
    {
        AddLog($"Opening full health and SMART telemetry diagnostics for {drive.Model} (Drive {drive.DiskIndex})…");
        var window = new DriveStatusWindow(drive) { Owner = this };
        window.Show();
    }

    private void WatchVolume_Click(object sender, RoutedEventArgs e)
    {
        if (VolumesGrid.SelectedItem is DriveRow vol)
        {
            _settings.WatchedRoots.Add(vol.RootPath);
            _settings.Save();
            RefreshVolumes();
            AddLog($"Watch enabled for volume {vol.RootPath}.");
        }
        else
        {
            AddLog("Select a volume in the Logical Volumes tab first.");
        }
    }

    private void UnwatchVolume_Click(object sender, RoutedEventArgs e)
    {
        if (VolumesGrid.SelectedItem is DriveRow vol)
        {
            _settings.WatchedRoots.Remove(vol.RootPath);
            _settings.Save();
            RefreshVolumes();
            AddLog($"Watch disabled for volume {vol.RootPath}.");
        }
        else
        {
            AddLog("Select a volume in the Logical Volumes tab first.");
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        var vol = VolumesGrid.SelectedItem as DriveRow;
        if (vol == null)
        {
            AddLog("Select a volume from the Logical Volumes tab to check.");
            return;
        }

        var confirmed = DarkDialog.Confirm(
            this,
            $"Run a manual, read-only file-system accessibility check on {vol.RootPath} ({vol.Label})?\n\nThis enumerates directory entries to test filesystem responsiveness. It does not alter files.",
            "Confirm Read-Only Check",
            "Run Check",
            "Cancel");

        if (!confirmed) return;

        var button = sender as Button;
        if (button != null) button.IsEnabled = false;
        AddLog($"Starting file-system accessibility check on {vol.RootPath}…");

        try
        {
            var res = await Task.Run(() => CheckDirectoryEntries(vol.RootPath));
            AddLog($"Check complete for {vol.RootPath}: {res.Directories:N0} folders, {res.Files:N0} files scanned, {res.Errors:N0} access errors.");
        }
        catch (Exception ex)
        {
            AddLog($"File system check error: {ex.Message}");
        }
        finally
        {
            if (button != null) button.IsEnabled = true;
        }
    }

    private static (long Directories, long Files, long Errors) CheckDirectoryEntries(string root)
    {
        long directories = 0, files = 0, errors = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(current).ToArray(); }
            catch
            {
                Interlocked.Increment(ref errors);
                continue;
            }

            Interlocked.Increment(ref directories);
            foreach (var entry in entries)
            {
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                            pending.Push(entry);
                    }
                    else Interlocked.Increment(ref files);
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
            }
        }
        return (directories, files, errors);
    }

    private void AddLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AddLog(message));
            return;
        }
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}

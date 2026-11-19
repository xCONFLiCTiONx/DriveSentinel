using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace DriveSentinel;

public sealed class WindowBounds
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool IsMaximized { get; set; }
}

public sealed class WatchSettings
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriveSentinel");
    private static readonly string FilePath = Path.Combine(Folder, "watch-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public HashSet<string> WatchedRoots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<int> WatchedDisks { get; set; } = new();
    public int CheckIntervalMinutes { get; set; } = 15;
    public bool EnablePredictiveAlerts { get; set; } = true;
    public int TemperatureWarningThresholdC { get; set; } = 65;
    public int LowSpaceThresholdPercent { get; set; } = 10;
    public bool ShowTrayNotifications { get; set; } = true;

    // Window position, size, and state persistence
    public WindowBounds MainWindowBounds { get; set; } = new();
    public WindowBounds StatusWindowBounds { get; set; } = new();
    public WindowBounds SettingsWindowBounds { get; set; } = new();

    public static WatchSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new WatchSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<WatchSettings>(json, JsonOptions) ?? new WatchSettings();
        }
        catch
        {
            return new WatchSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            // Direct write fallback in case atomic move is blocked by antivirus or file locks
            try
            {
                var json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }

    /// <summary>
    /// Applies saved bounds to a window, verifying they fit on screen and respect the Windows taskbar.
    /// </summary>
    public static void ApplyPlacement(Window window, WindowBounds bounds, double defaultWidth, double defaultHeight)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        var workArea = SystemParameters.WorkArea;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        var hasSavedDimensions = bounds.Width.HasValue && bounds.Width.Value >= window.MinWidth &&
                                 bounds.Height.HasValue && bounds.Height.Value >= window.MinHeight;

        var targetWidth = hasSavedDimensions ? bounds.Width!.Value : Math.Min(defaultWidth, workArea.Width - 80);
        var targetHeight = hasSavedDimensions ? bounds.Height!.Value : Math.Min(defaultHeight, workArea.Height - 70);

        // Ensure clamped to work area
        targetWidth = Math.Min(targetWidth, workArea.Width);
        targetHeight = Math.Min(targetHeight, workArea.Height);

        window.Width = Math.Max(window.MinWidth, targetWidth);
        window.Height = Math.Max(window.MinHeight, targetHeight);

        var hasSavedPosition = bounds.Left.HasValue && bounds.Top.HasValue;
        var isVisibleOnScreen = hasSavedPosition &&
                                bounds.Left!.Value + 80 >= virtualLeft &&
                                bounds.Left!.Value + window.Width - 80 <= virtualLeft + virtualWidth &&
                                bounds.Top!.Value + 40 >= virtualTop &&
                                bounds.Top!.Value + window.Height - 40 <= virtualTop + virtualHeight;

        if (isVisibleOnScreen)
        {
            window.Left = bounds.Left!.Value;
            window.Top = bounds.Top!.Value;
        }
        else
        {
            // Center within primary screen work area (clearing taskbar)
            window.Left = workArea.Left + Math.Max(0, (workArea.Width - window.Width) / 2);
            window.Top = workArea.Top + Math.Max(0, (workArea.Height - window.Height) / 2);
        }

        if (bounds.IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
        else
        {
            window.WindowState = WindowState.Normal;
        }
    }

    /// <summary>
    /// Saves current window bounds (using RestoreBounds if currently maximized or minimized).
    /// </summary>
    public static void SavePlacement(Window window, WindowBounds bounds, WindowState? explicitState = null)
    {
        var state = explicitState ?? window.WindowState;

        if (state == WindowState.Maximized)
        {
            bounds.IsMaximized = true;
            if (!window.RestoreBounds.IsEmpty)
            {
                bounds.Left = window.RestoreBounds.Left;
                bounds.Top = window.RestoreBounds.Top;
                bounds.Width = window.RestoreBounds.Width;
                bounds.Height = window.RestoreBounds.Height;
            }
        }
        else if (state == WindowState.Normal)
        {
            bounds.IsMaximized = false;
            bounds.Left = window.Left;
            bounds.Top = window.Top;
            bounds.Width = window.Width;
            bounds.Height = window.Height;
        }
        else if (state == WindowState.Minimized)
        {
            // When minimized, preserve bounds.IsMaximized and save RestoreBounds if available
            if (!window.RestoreBounds.IsEmpty)
            {
                bounds.Left = window.RestoreBounds.Left;
                bounds.Top = window.RestoreBounds.Top;
                bounds.Width = window.RestoreBounds.Width;
                bounds.Height = window.RestoreBounds.Height;
            }
        }
    }
}

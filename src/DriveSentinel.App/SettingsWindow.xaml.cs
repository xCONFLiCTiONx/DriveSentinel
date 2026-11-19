using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace DriveSentinel;

public partial class SettingsWindow : Window
{
    private readonly WatchSettings _settings;
    public bool SettingsChanged { get; private set; }

    public SettingsWindow(WatchSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        WatchSettings.ApplyPlacement(this, _settings.SettingsWindowBounds, 640, 580);
        Loaded += (_, _) => LoadSettingsIntoUi();
    }

    private void LoadSettingsIntoUi()
    {
        // Check Interval
        SelectComboByTag(IntervalCombo, _settings.CheckIntervalMinutes.ToString());
        // Predictive Alerts
        PredictiveAlertsCheck.IsChecked = _settings.EnablePredictiveAlerts;
        // Tray Notifications
        TrayNotificationsCheck.IsChecked = _settings.ShowTrayNotifications;
        // Temp Threshold
        SelectComboByTag(TempCombo, _settings.TemperatureWarningThresholdC.ToString());
        // Space Threshold
        SelectComboByTag(SpaceCombo, _settings.LowSpaceThresholdPercent.ToString());

        // Startup UI
        UpdateStartupUi();
    }

    private void UpdateStartupUi()
    {
        var isRegistered = StartupManager.IsStartupEnabled();
        var converter = new BrushConverter();

        if (isRegistered)
        {
            StartupStatusBox.Background = (Brush)converter.ConvertFromString("#142C21")!;
            StartupStatusBox.BorderBrush = (Brush)converter.ConvertFromString("#22C55E")!;
            StartupStatusIcon.Text = "🟢";
            StartupStatusHeading.Text = "Registered to start with Windows";
            StartupStatusHeading.Foreground = (Brush)converter.ConvertFromString("#4ADE80")!;
            StartupStatusDetails.Text = "DriveSentinel will launch elevated in the background when Windows starts.";
            StartWithWindowsCheck.IsChecked = true;
        }
        else
        {
            StartupStatusBox.Background = (Brush)converter.ConvertFromString("#101826")!;
            StartupStatusBox.BorderBrush = (Brush)converter.ConvertFromString("#2B374A")!;
            StartupStatusIcon.Text = "⚪";
            StartupStatusHeading.Text = "Not registered to start with Windows";
            StartupStatusHeading.Foreground = (Brush)converter.ConvertFromString("#CBD5E1")!;
            StartupStatusDetails.Text = "DriveSentinel will not start automatically when you log into Windows.";
            StartWithWindowsCheck.IsChecked = false;
        }
    }

    private void StartWithWindowsCheck_Click(object sender, RoutedEventArgs e)
    {
        bool enable = StartWithWindowsCheck.IsChecked == true;
        StartupManager.SetStartup(enable);
        UpdateStartupUi();
        SettingsChanged = true;
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static int GetSelectedTagInt(ComboBox combo, int fallback)
    {
        if (combo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var val))
            return val;
        return fallback;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.CheckIntervalMinutes = GetSelectedTagInt(IntervalCombo, 15);
        _settings.EnablePredictiveAlerts = PredictiveAlertsCheck.IsChecked == true;
        _settings.ShowTrayNotifications = TrayNotificationsCheck.IsChecked == true;
        _settings.TemperatureWarningThresholdC = GetSelectedTagInt(TempCombo, 65);
        _settings.LowSpaceThresholdPercent = GetSelectedTagInt(SpaceCombo, 10);

        _settings.Save();
        SettingsChanged = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        WatchSettings.SavePlacement(this, _settings.SettingsWindowBounds);
        _settings.Save();
        base.OnClosing(e);
    }
}

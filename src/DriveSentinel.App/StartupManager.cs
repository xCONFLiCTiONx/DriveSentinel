using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DriveSentinel;

[Flags]
public enum StartupLocation
{
    None = 0,
    TaskScheduler = 1
}

public static class StartupManager
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string AppName = "DriveSentinel";
    public const string SilentArgument = "--silent";

    public static string GetExecutablePath()
    {
        var exeInDir = Path.Combine(AppContext.BaseDirectory, "DriveSentinel.exe");
        if (File.Exists(exeInDir))
        {
            return exeInDir;
        }

        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path) &&
            !path.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(path))
        {
            return path;
        }

        return exeInDir;
    }

    /// <summary>
    /// Checks whether DriveSentinel is registered to start elevated via Windows Task Scheduler.
    /// Uses highest available privileges so it starts at logon without a UAC prompt.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        return IsScheduledTaskEnabled();
    }

    /// <summary>
    /// Backwards compatibility alias for IsStartupEnabled.
    /// </summary>
    public static bool IsRegistryStartupEnabled()
    {
        return IsStartupEnabled();
    }

    /// <summary>
    /// Checks if the DriveSentinel elevated logon scheduled task exists.
    /// </summary>
    public static bool IsScheduledTaskEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/query /tn \"{AppName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return proc.ExitCode == 0 && output.Contains(AppName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates or removes an elevated Windows Task Scheduler task that runs at logon with highest privileges.
    /// Cleans up any legacy registry Run key entries to avoid duplicate non-elevated prompts.
    /// </summary>
    public static bool SetStartup(bool enable)
    {
        try
        {
            // Remove any legacy registry Run entry and startup shortcut
            RemoveRegistryRunEntry();
            RemoveStartupFolderShortcut();

            if (enable)
            {
                var exePath = GetExecutablePath();
                var cmd = $"/create /tn \"{AppName}\" /tr \"\\\"{exePath}\\\" {SilentArgument}\" /sc onlogon /rl highest /f";
                var psi = new ProcessStartInfo("schtasks.exe", cmd)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
                return IsScheduledTaskEnabled();
            }
            else
            {
                var psi = new ProcessStartInfo("schtasks.exe", $"/delete /tn \"{AppName}\" /f")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
                return !IsScheduledTaskEnabled();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Backwards compatibility alias for SetStartup.
    /// </summary>
    public static bool SetRegistryStartup(bool enable)
    {
        return SetStartup(enable);
    }

    public static StartupLocation GetStartupLocation()
    {
        return IsScheduledTaskEnabled() ? StartupLocation.TaskScheduler : StartupLocation.None;
    }

    public static bool RemoveAllStartup()
    {
        return SetStartup(false);
    }

    private static void RemoveRegistryRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.DeleteValue(AppName, false);
        }
        catch
        {
            // Ignore registry cleanup errors
        }
    }

    private static void RemoveStartupFolderShortcut()
    {
        try
        {
            var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupDir, "DriveSentinel.lnk");
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch
        {
            // Ignore legacy cleanup errors
        }
    }
}

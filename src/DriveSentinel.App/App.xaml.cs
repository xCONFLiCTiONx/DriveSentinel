using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace DriveSentinel;

public partial class App : System.Windows.Application
{
    private const string MutexName = "DriveSentinel_SingleInstance_Mutex";
    private const string ShowDashboardEventName = "DriveSentinel_ShowDashboard_Event";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _showDashboardEvent;
    private static RegisteredWaitHandle? _registeredWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check if --silent, -silent, or /silent was passed in arguments
        bool isSilent = false;
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i].Trim();
            if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-silent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase))
            {
                isSilent = true;
                break;
            }
        }

        // Single instance check
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already active
            if (!isSilent)
            {
                // Signal the primary instance to unhide / focus its dashboard
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(ShowDashboardEventName);
                    signal.Set();
                }
                catch
                {
                    // Existing instance might be closing or inaccessible
                }
            }

            // Terminate duplicate process
            Shutdown();
            return;
        }

        // Set up the event so future secondary launches without --silent bring our dashboard to foreground
        try
        {
            _showDashboardEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowDashboardEventName);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(_showDashboardEvent, (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow is MainWindow mw)
                    {
                        mw.ShowDashboard();
                    }
                });
            }, null, -1, false);
        }
        catch
        {
            // Event handle creation failure should not prevent app from running
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainWindow = new MainWindow(isSilent);
        MainWindow = mainWindow;

        if (!isSilent)
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _registeredWait?.Unregister(null);
        _showDashboardEvent?.Dispose();

        if (_singleInstanceMutex != null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // Mutex may have already been released
            }
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}

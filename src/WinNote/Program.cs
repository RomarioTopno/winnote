using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinNote.Services;
using static WinNote.Interop.NativeMethods;

namespace WinNote;

public static class Program
{
    private const string MutexName = @"Local\" + AppPaths.InstanceName + ".SingleInstance.3F2B6C1E";
    private static Mutex? s_mutex;

    [STAThread]
    private static int Main(string[] args)
    {
        bool HasArg(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

        // Used by the installer: close a running copy (so files can be replaced) and, on
        // uninstall, also remove the "Start with Windows" entry. Notes are never touched.
        if (HasArg("--close") || HasArg("--uninstall"))
        {
            CloseRunningInstance();
            if (HasArg("--uninstall"))
            {
                // Optional "--user-sid <SID>" from the installer (see StartupService.RemoveForUser).
                int i = Array.FindIndex(args, a => a.Equals("--user-sid", StringComparison.OrdinalIgnoreCase));
                string? sid = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
                try { StartupService.RemoveForUser(sid); }
                catch (Exception ex) { ErrorLog.Write("Uninstall cleanup", ex); }
            }
            return 0;
        }

        bool startHidden = HasArg("--hidden");

        s_mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Already running: ask that instance to show its window, then quit.
            if (!startHidden) SignalRunningInstance();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(startHidden);
        });

        GC.KeepAlive(s_mutex);
        return 0;
    }

    /// <summary>Asks a running WinNote to save and exit (WM_CLOSE to its host window) and waits up to 5 s.</summary>
    private static void CloseRunningInstance()
    {
        nint host = FindWindow(HostWindow.ClassName, null);
        if (host == 0) return;
        GetWindowThreadProcessId(host, out uint pid);
        PostMessage(host, WM_CLOSE, 0, 0);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            process.WaitForExit(5000);
        }
        catch (ArgumentException) { } // already gone
    }

    private static void SignalRunningInstance()
    {
        // The first instance may still be starting up; give it up to ~2 s to create its host window.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            nint host = FindWindow(HostWindow.ClassName, null);
            if (host != 0)
            {
                // We are the foreground process (the user just launched us), so we can hand
                // foreground rights to the running instance.
                GetWindowThreadProcessId(host, out uint pid);
                AllowSetForegroundWindow(pid);
                PostMessage(host, HostWindow.WM_APP_SHOW, 0, 0);
                return;
            }
            Thread.Sleep(100);
        }
    }
}

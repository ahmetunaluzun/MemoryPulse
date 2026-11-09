using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal static class Program
    {
        public const string RepoUrl = "https://github.com/ahmetunaluzun/MemoryPulse";
        public const string ReleasesUrl = RepoUrl + "/releases/latest";
        public static readonly string Version =
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        [STAThread]
        private static int Main(string[] args)
        {
            // Elevated helper modes (started by this exe through UAC).
            if (args.Length > 0 && args[0] == Elevated.PurgeArg)
                return Elevated.PurgeStandby() == ActionResult.Ok ? 0 : 1;
            if (args.Length >= 3 && args[0] == Elevated.StartupArg)
                return Startup.Apply(args[1] == "on", args[2]) ? 0 : 1;

            bool startHidden = Array.IndexOf(args, "--tray") >= 0;
            Mutex mutex;
            bool first;
            try { mutex = new Mutex(true, @"Local\MemoryPulse.Instance", out first); }
            catch (UnauthorizedAccessException) { SignalRunningInstance(); return 0; } // an elevated instance owns it
            using (mutex)
            {
                if (!first)
                {
                    SignalRunningInstance();
                    return 0;
                }
                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += (s, e) => Fail(e.Exception);
                    AppDomain.CurrentDomain.UnhandledException += (s, e) => Fail(e.ExceptionObject as Exception);
                    Application.Run(new TrayApp(!startHidden, show));
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private const string ShowEventName = @"Local\MemoryPulse.Show";

        private static void SignalRunningInstance()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(ShowEventName, System.Security.AccessControl.EventWaitHandleRights.Modify))
                    ev.Set();
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException || ex is UnauthorizedAccessException)
            {
                // The running instance is elevated (started with Windows) and cannot be signalled from here.
                Log.Write("INFO", "Second start while an elevated instance runs: " + ex.Message);
                L.Use(Settings.Load().Language);
                MessageBox.Show(L.T("app.alreadyRunning"), "MemoryPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public static void OpenTaskManager()
        {
            try { Process.Start("taskmgr.exe"); }
            catch (System.ComponentModel.Win32Exception ex) { Log.Write("ERROR", "Task Manager could not start: " + ex.Message); }
        }

        private static void Fail(Exception ex)
        {
            Log.Write("ERROR", "Unhandled: " + ex);
            MessageBox.Show(L.T("error.unexpected", ex?.Message), "MemoryPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace MemoryPulse
{
    internal enum ActionResult { Ok, Cancelled, Failed }

    /// <summary>Operations that need administrator rights. When not elevated they re-run this exe via UAC.</summary>
    internal static class Elevated
    {
        public const string PurgeArg = "--purge-standby";
        public const string StartupArg = "--startup";

        public static ActionResult PurgeStandby()
        {
            if (Native.IsElevated)
            {
                try { Native.PurgeStandbyList(); return ActionResult.Ok; }
                catch (Win32Exception ex) { Log.Write("ERROR", "Standby purge failed: " + ex.Message); return ActionResult.Failed; }
            }
            return RunSelfElevated(PurgeArg);
        }

        public static ActionResult RunSelfElevated(string args)
        {
            try
            {
                var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, args)
                {
                    Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null || !p.WaitForExit(60000)) return ActionResult.Failed;
                    return p.ExitCode == 0 ? ActionResult.Ok : ActionResult.Failed;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // user cancelled the UAC prompt
            {
                return ActionResult.Cancelled;
            }
            catch (Win32Exception ex)
            {
                Log.Write("ERROR", $"Elevated run '{args}' failed: {ex.Message}");
                return ActionResult.Failed;
            }
        }
    }

    /// <summary>
    /// "Start with Windows". Full edition: a per-user scheduled task that runs at logon with the highest
    /// available rights, so automatic standby cleaning works without a UAC prompt every time.
    /// Store edition: the package's startup task (see <see cref="StoreStartup"/>).
    /// </summary>
    internal static class Startup
    {
        private const string TaskName = "MemoryPulse";

        public static bool IsEnabled() =>
            Edition.IsStore ? StoreStartup.IsEnabled() : Schtasks($"/Query /TN \"{TaskName}\"") == 0;

        public static ActionResult Set(bool enable)
        {
            if (Edition.IsStore) return StoreStartup.Set(enable);
            string user = WindowsIdentity.GetCurrent().Name;
            if (Native.IsElevated) return Apply(enable, user) ? ActionResult.Ok : ActionResult.Failed;
            return Elevated.RunSelfElevated($"{Elevated.StartupArg} {(enable ? "on" : "off")} \"{user}\"");
        }

        /// <summary>Runs inside the elevated helper process.</summary>
        public static bool Apply(bool enable, string user)
        {
            if (!enable) return Schtasks($"/Delete /TN \"{TaskName}\" /F") == 0;
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string xmlPath = Path.Combine(Path.GetTempPath(), "MemoryPulse-task.xml");
            File.WriteAllText(xmlPath, TaskXml(exe, user), Encoding.Unicode);
            try { return Schtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F") == 0; }
            finally { File.Delete(xmlPath); }
        }

        private static string TaskXml(string exe, string user)
        {
            string u = SecurityElement.Escape(user), e = SecurityElement.Escape(exe);
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>MemoryPulse - RAM monitor in the notification area.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{u}</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{u}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{e}</Command><Arguments>--tray</Arguments></Exec></Actions>
</Task>";
        }

        private static int Schtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 && !args.StartsWith("/Query", StringComparison.Ordinal))
                    Log.Write("ERROR", $"schtasks {args.Split(' ')[0]} failed ({p.ExitCode}): {err.Trim()}");
                return p.ExitCode;
            }
        }
    }
}

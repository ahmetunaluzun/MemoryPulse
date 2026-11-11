using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;

namespace MemoryPulse
{
    internal enum EventKind
    {
        AppCrash, AppHang, DotNetCrash, BlueScreen, PowerLoss, UnexpectedShutdown, // problems
        ShutdownRequested, WindowsStopped, WindowsStarted                          // ordinary restarts
    }

    /// <summary>A crash, hang or restart that Windows wrote to its own event log.</summary>
    internal sealed class SystemEvent
    {
        public DateTime Time;
        public EventKind Kind;
        public string Program; // crashed program or the one that asked for the shutdown; null for system events
        public string Detail;  // faulting module, exception, stop code or shutdown type

        public bool IsProblem => Kind <= EventKind.UnexpectedShutdown;
        public string KindText => L.T("event.kind." + Kind);
        public string Description => L.T("event.text." + Kind, Program ?? "", Detail ?? "");
    }

    /// <summary>
    /// Reads crashes, hangs and restarts for a time range from the Windows event log, on demand.
    /// MemoryPulse does not record these itself; ordinary users can read both logs.
    /// </summary>
    internal static class SystemEvents
    {
        private const string ApplicationQuery =
            "(Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002)" +
            " or (Provider[@Name='.NET Runtime'] and EventID=1026)";
        private const string SystemQuery =
            "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41)" +
            " or (Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001)" +
            " or (Provider[@Name='EventLog'] and EventID=6008) or (Provider[@Name='User32'] and EventID=1074)" +
            " or (Provider[@Name='Microsoft-Windows-Kernel-General'] and (EventID=12 or EventID=13))";
        private const string DotNetAppPrefix = "Application: ";
        private const string DotNetExceptionPrefix = "Exception Info: ";
        private const int MaxDetailLength = 120;

        /// <param name="program">When set: only this program's crashes and hangs (name with or without .exe).</param>
        public static List<SystemEvent> Read(DateTime from, DateTime to, string program = null)
        {
            var list = new List<SystemEvent>();
            ReadLog("Application", ApplicationQuery, from, to, list);
            if (program == null) ReadLog("System", SystemQuery, from, to, list);
            else list.RemoveAll(e => !SameProgram(e.Program, program));
            list.Sort((a, b) => b.Time.CompareTo(a.Time));
            return list;
        }

        public static bool SameProgram(string a, string b) =>
            a != null && b != null && string.Equals(WithoutExe(a), WithoutExe(b), StringComparison.OrdinalIgnoreCase);

        private static string WithoutExe(string name) =>
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;

        private static void ReadLog(string log, string what, DateTime from, DateTime to, List<SystemEvent> list)
        {
            string xpath = string.Format(CultureInfo.InvariantCulture,
                "*[System[({0}) and TimeCreated[@SystemTime>='{1:o}' and @SystemTime<='{2:o}']]]",
                what, from.ToUniversalTime(), to.ToUniversalTime());
            try
            {
                using (var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath)))
                {
                    EventRecord record;
                    while ((record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            var e = Convert(record);
                            if (e != null) list.Add(e);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is EventLogException || ex is UnauthorizedAccessException)
            {
                Log.Write("WARN", $"Event log '{log}' could not be read: {ex.Message}");
            }
        }

        private static SystemEvent Convert(EventRecord r)
        {
            if (r.TimeCreated == null) return null;
            var e = new SystemEvent { Time = r.TimeCreated.Value };
            string P(int i) => i < r.Properties.Count ? r.Properties[i].Value?.ToString() : null;
            switch (r.ProviderName + "/" + r.Id.ToString(CultureInfo.InvariantCulture))
            {
                case "Application Error/1000": e.Kind = EventKind.AppCrash; e.Program = P(0); e.Detail = P(3); break;
                case "Application Hang/1002": e.Kind = EventKind.AppHang; e.Program = P(0); break;
                case ".NET Runtime/1026": e.Kind = EventKind.DotNetCrash; ReadDotNet(P(0), e); break;
                case "Microsoft-Windows-Kernel-Power/41": e.Kind = EventKind.PowerLoss; break;
                case "Microsoft-Windows-WER-SystemErrorReporting/1001": e.Kind = EventKind.BlueScreen; e.Detail = P(0); break;
                case "EventLog/6008": e.Kind = EventKind.UnexpectedShutdown; break;
                case "User32/1074": e.Kind = EventKind.ShutdownRequested; e.Program = ProcessOf(P(0)); e.Detail = P(4); break;
                case "Microsoft-Windows-Kernel-General/12": e.Kind = EventKind.WindowsStarted; break;
                case "Microsoft-Windows-Kernel-General/13": e.Kind = EventKind.WindowsStopped; break;
                default: return null;
            }
            if (e.Detail != null && e.Detail.Length > MaxDetailLength) e.Detail = e.Detail.Substring(0, MaxDetailLength) + "…";
            return e;
        }

        // The .NET runtime writes one text block: "Application: x.exe ... Exception Info: System.Y ...".
        private static void ReadDotNet(string text, SystemEvent e)
        {
            if (text == null) return;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith(DotNetAppPrefix, StringComparison.Ordinal)) e.Program = line.Substring(DotNetAppPrefix.Length);
                else if (line.StartsWith(DotNetExceptionPrefix, StringComparison.Ordinal)) e.Detail = line.Substring(DotNetExceptionPrefix.Length);
            }
        }

        // "C:\WINDOWS\system32\winlogon.exe (COMPUTER)" -> "winlogon.exe"
        private static string ProcessOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int cut = text.LastIndexOf(" (", StringComparison.Ordinal);
            if (cut > 0) text = text.Substring(0, cut);
            try { return Path.GetFileName(text); }
            catch (ArgumentException) { return text; }
        }
    }
}

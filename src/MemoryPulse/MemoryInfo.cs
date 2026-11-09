using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MemoryPulse
{
    internal sealed class MemorySnapshot
    {
        public ulong Total, Available, Standby, Free, Modified, CommitUsed, CommitLimit;
        public bool HasDetail;

        /// <summary>Standby + modified: what Task Manager shows as "Cached".</summary>
        public ulong Cached => Standby + Modified;

        /// <summary>Memory actively used by processes, drivers and the system.</summary>
        public ulong InUse => Total > Available + Modified ? Total - Available - Modified : 0;

        public static double Gb(ulong bytes) => bytes / 1073741824.0;
    }

    internal sealed class ProcessUsage
    {
        public string Name;
        public int Count;
        public ulong WorkingSet;
    }

    internal static class MemoryInfo
    {
        private static PerformanceCounter[] _counters;
        private static bool _countersFailed;

        public static MemorySnapshot Read()
        {
            var s = new MemorySnapshot();
            Native.GetMemoryStatus(out s.Total, out s.Available, out s.CommitUsed, out s.CommitLimit);
            if (Native.TryGetMemoryLists(out s.Standby, out s.Free, out s.Modified) || TryReadCounters(s))
                s.HasDetail = true;
            return s;
        }

        // Fallback for non-elevated runs: the same values from Windows performance counters.
        private static bool TryReadCounters(MemorySnapshot s)
        {
            if (_countersFailed) return false;
            try
            {
                if (_counters == null)
                {
                    string[] names =
                    {
                        "Standby Cache Normal Priority Bytes", "Standby Cache Core Bytes", "Standby Cache Reserve Bytes",
                        "Free & Zero Page List Bytes", "Modified Page List Bytes"
                    };
                    _counters = names.Select(n => new PerformanceCounter("Memory", n, true)).ToArray();
                }
                s.Standby = (ulong)(_counters[0].NextValue() + _counters[1].NextValue() + _counters[2].NextValue());
                s.Free = (ulong)_counters[3].NextValue();
                s.Modified = (ulong)_counters[4].NextValue();
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UnauthorizedAccessException
                                       || ex is System.ComponentModel.Win32Exception)
            {
                _countersFailed = true;
                Log.Write("WARN", "Performance counters unavailable: " + ex.Message);
                return false;
            }
        }

        /// <summary>Processes grouped by name, largest working set first.</summary>
        public static List<ProcessUsage> TopProcesses(int count)
        {
            var groups = new Dictionary<string, ProcessUsage>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (!groups.TryGetValue(p.ProcessName, out var u))
                            groups[p.ProcessName] = u = new ProcessUsage { Name = p.ProcessName };
                        u.Count++;
                        u.WorkingSet += (ulong)p.WorkingSet64;
                    }
                    catch (InvalidOperationException) { /* process exited while enumerating */ }
                }
            }
            return groups.Values.OrderByDescending(g => g.WorkingSet).Take(count).ToList();
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MemoryPulse
{
    /// <summary>Per-program RAM and read/write bytes since the previous sample, grouped by process name.</summary>
    internal sealed class ProcessSampler
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern bool GetProcessIoCounters(IntPtr process, out IO_COUNTERS counters);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private Dictionary<int, IO_COUNTERS> _previous; // null until the first (baseline) pass

        /// <summary>PID → program name at the last sample and the one before (for processes that just exited).</summary>
        public Dictionary<int, string> Names = new Dictionary<int, string>(), OlderNames = new Dictionary<int, string>();

        /// <summary>All programs right now. The first call only sets the baseline for read/write.</summary>
        public Dictionary<string, ProgramSample> Sample()
        {
            var current = new Dictionary<int, IO_COUNTERS>();
            var names = new Dictionary<int, string>();
            var groups = new Dictionary<string, ProgramSample>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        string name = p.ProcessName;
                        names[p.Id] = name;
                        if (!groups.TryGetValue(name, out var g)) groups[name] = g = new ProgramSample { Name = name };
                        g.Count++;
                        g.WorkingSet += p.WorkingSet64;
                        if (p.Id != 0 && TryReadIo(p.Id, out IO_COUNTERS io))
                        {
                            current[p.Id] = io;
                            AddDelta(g, p.Id, io);
                        }
                    }
                    catch (InvalidOperationException) { /* process exited while enumerating */ }
                }
            }
            _previous = current;
            OlderNames = Names;
            Names = names;
            return groups;
        }

        // A PID not seen before started during the interval, so everything it read/wrote counts.
        // Counters lower than before mean the PID was reused by a new process: same rule.
        private void AddDelta(ProgramSample g, int pid, IO_COUNTERS now)
        {
            if (_previous == null) return;
            bool known = _previous.TryGetValue(pid, out IO_COUNTERS old) && now.ReadBytes >= old.ReadBytes && now.WriteBytes >= old.WriteBytes;
            g.Read += (long)(known ? now.ReadBytes - old.ReadBytes : now.ReadBytes);
            g.Write += (long)(known ? now.WriteBytes - old.WriteBytes : now.WriteBytes);
        }

        private static bool TryReadIo(int pid, out IO_COUNTERS io)
        {
            io = default(IO_COUNTERS);
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false; // protected or another user's process
            try { return GetProcessIoCounters(h, out io); }
            finally { CloseHandle(h); }
        }

        /// <summary>The rows worth keeping: biggest by RAM, busiest by read/write, and the always-wanted names.</summary>
        public static List<ProgramSample> Pick(Dictionary<string, ProgramSample> all, ICollection<string> always)
        {
            var picked = new HashSet<ProgramSample>(all.Values.OrderByDescending(g => g.WorkingSet).Take(HistoryConfig.TopByRam));
            picked.UnionWith(all.Values.Where(g => g.Read + g.Write > 0).OrderByDescending(g => g.Read + g.Write).Take(HistoryConfig.TopByIo));
            foreach (string name in always)
                if (all.TryGetValue(name, out var g)) picked.Add(g);
            return picked.OrderByDescending(g => g.WorkingSet).ToList();
        }
    }

    /// <summary>System-wide disk and network activity and free disk space.</summary>
    internal sealed class SystemSampler : IDisposable
    {
        private PerformanceCounter _diskRead, _diskWrite;
        private bool _diskFailed;
        private bool _netFailed;
        private Dictionary<long, long[]> _net = new Dictionary<long, long[]>(); // adapter LUID -> { received, sent }

        public SystemSampler()
        {
            ReadDisk(1, out _, out _);  // rate counters and network totals need a first reading to compare with
            ReadNetwork(out _, out _);
        }

        /// <summary>Bytes read/written on all physical disks during the interval; -1 when Windows counters are unavailable.</summary>
        public void ReadDisk(double seconds, out long read, out long write)
        {
            read = write = -1;
            if (_diskFailed) return;
            try
            {
                if (_diskRead == null)
                {
                    _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
                    _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
                }
                read = (long)(_diskRead.NextValue() * seconds);
                write = (long)(_diskWrite.NextValue() * seconds);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is Win32Exception)
            {
                _diskFailed = true;
                read = write = -1;
                Log.Write("WARN", "History: disk counters unavailable, disk activity will not be recorded: " + ex.Message);
            }
        }

        private PerformanceCounter _diskIdle;
        private bool _idleFailed;

        /// <summary>
        /// Share of the time since the previous call in which the physical disks were busy (0..100);
        /// -1 when Windows counters are unavailable. The first call only sets the starting point.
        /// </summary>
        public double ReadDiskActive()
        {
            if (_idleFailed) return -1;
            try
            {
                if (_diskIdle == null) _diskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", true);
                return Math.Max(0, Math.Min(100, 100 - _diskIdle.NextValue()));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is Win32Exception)
            {
                _idleFailed = true;
                Log.Write("WARN", "Disk active time counter unavailable, it will not be shown: " + ex.Message);
                return -1;
            }
        }

        // MIB_IF_TABLE2 / MIB_IF_ROW2 (netioapi.h): the row is read by offset, it is the same on x86 and x64.
        private const int IfRowSize = 1352, IfFirstRow = 8, IfLuid = 0, IfFlags = 1152, IfInOctets = 1208, IfOutOctets = 1280;
        private const byte IfHardware = 0x01; // InterfaceAndOperStatusFlags.HardwareInterface

        [DllImport("iphlpapi.dll")]
        private static extern int GetIfTable2(out IntPtr table);

        [DllImport("iphlpapi.dll")]
        private static extern void FreeMibTable(IntPtr table);

        /// <summary>
        /// Bytes received/sent since the previous call, on hardware adapters only. Virtual adapters (VPN,
        /// virtual machine switches) carry the same traffic a second time and would double the total.
        /// </summary>
        public void ReadNetwork(out long rx, out long tx)
        {
            rx = tx = 0;
            int err = GetIfTable2(out IntPtr table);
            if (err != 0)
            {
                if (!_netFailed) Log.Write("WARN", $"History: network totals could not be read (GetIfTable2 error {err})");
                _netFailed = true;
                rx = tx = -1;
                return;
            }
            var now = new Dictionary<long, long[]>();
            try
            {
                int count = Marshal.ReadInt32(table);
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = table + IfFirstRow + i * IfRowSize;
                    if ((Marshal.ReadByte(row, IfFlags) & IfHardware) == 0) continue;
                    var totals = new[] { Marshal.ReadInt64(row, IfInOctets), Marshal.ReadInt64(row, IfOutOctets) };
                    now[Marshal.ReadInt64(row, IfLuid)] = totals;
                    // A new adapter has no earlier reading; a smaller total means its counter was reset.
                    if (!_net.TryGetValue(Marshal.ReadInt64(row, IfLuid), out long[] old) || totals[0] < old[0] || totals[1] < old[1]) continue;
                    rx += totals[0] - old[0];
                    tx += totals[1] - old[1];
                }
            }
            finally { FreeMibTable(table); }
            _net = now;
        }

        public static void AppendDrives(StringBuilder sb, string time)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    sb.Append("D,").Append(time).Append(',').Append(drive.Name.TrimEnd('\\')).Append(',')
                      .Append(drive.AvailableFreeSpace.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(drive.TotalSize.ToString(CultureInfo.InvariantCulture)).AppendLine();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // A drive that went away between the two calls: skip it this time.
                }
            }
        }

        public void Dispose()
        {
            _diskRead?.Dispose();
            _diskWrite?.Dispose();
            _diskIdle?.Dispose();
        }
    }
}

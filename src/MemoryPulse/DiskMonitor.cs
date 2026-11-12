using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MemoryPulse
{
    internal sealed class DriveUsage
    {
        public string Name, Label; // "C:", "Windows"
        public long Free, Total;
        public double UsedFraction => Total <= 0 ? 0 : 1 - (double)Free / Total;

        /// <summary>Warning when little free space is left, critical when almost none.</summary>
        public Level Level
        {
            get
            {
                double free = Total <= 0 ? 1 : (double)Free / Total;
                return free < DiskMonitor.CriticalFreeFraction ? Level.Critical : free < DiskMonitor.LowFreeFraction ? Level.Warning : Level.Normal;
            }
        }

        /// <summary>"C: (Windows)", or just "C:" when the drive has no label.</summary>
        public string Title => string.IsNullOrEmpty(Label) ? Name : $"{Name} ({Label})";
    }

    /// <summary>What the main window shows about disks at one moment.</summary>
    internal sealed class DiskReading
    {
        public List<DriveUsage> Drives;
        public bool HasRates;                          // false until there is an earlier reading to compare with
        public double ReadRate, WriteRate, ActivePercent; // bytes per second, 0..100; -1 = Windows counters unavailable
        public Dictionary<string, double> ProgramRates; // program → read+write bytes per second
    }

    /// <summary>Drive fullness, live disk activity and per-program read/write speed for the main window.</summary>
    internal sealed class DiskMonitor : IDisposable
    {
        public const double LowFreeFraction = 0.10;      // a drive with less free space than this is drawn as a warning
        public const double CriticalFreeFraction = 0.05; // and in red below this
        private const double MinSeconds = 1;             // rates over a shorter time are noise
        private const double MaxSeconds = 30;            // the window was hidden: an average over that long is not "now"

        private readonly object _gate = new object();
        private readonly ProcessSampler _processes = new ProcessSampler();
        private readonly Stopwatch _since = new Stopwatch();
        private SystemSampler _system;
        private DiskReading _last;
        private bool _disposed;

        /// <summary>Fixed drives that are ready, in letter order. A drive that goes away while being read is left out.</summary>
        public static List<DriveUsage> ReadDrives()
        {
            var list = new List<DriveUsage>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    list.Add(new DriveUsage { Name = drive.Name.TrimEnd('\\'), Label = drive.VolumeLabel,
                                              Free = drive.AvailableFreeSpace, Total = drive.TotalSize });
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // The drive went away between two calls: skip it this time.
                }
            }
            return list;
        }

        /// <summary>Reads everything. Slow the first time (Windows loads its counters): call it off the UI thread.</summary>
        public DiskReading Sample()
        {
            lock (_gate)
            {
                if (_disposed) return null;
                if (_system != null && _since.Elapsed.TotalSeconds < MinSeconds && _last != null)
                {
                    _last.Drives = ReadDrives();
                    return _last;
                }
                var r = new DiskReading { Drives = ReadDrives() };
                double seconds = _since.Elapsed.TotalSeconds;
                bool first = _system == null;
                if (first) _system = new SystemSampler(); // its constructor takes the first disk reading
                else
                {
                    _system.ReadDisk(1, out long read, out long write); // over one second = the rate
                    r.ReadRate = read;
                    r.WriteRate = write;
                }
                r.ActivePercent = _system.ReadDiskActive();
                var programs = _processes.Sample();
                _since.Restart();
                if (!first && seconds <= MaxSeconds)
                {
                    r.HasRates = true;
                    r.ProgramRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in programs.Values) r.ProgramRates[p.Name] = (p.Read + p.Write) / seconds;
                }
                return _last = r;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _system?.Dispose();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace MemoryPulse
{
    /// <summary>Fixed values of the usage history: how often, how long, how much.</summary>
    internal static class HistoryConfig
    {
        public const int SampleSeconds = 60;      // one record per minute
        public const int KeepDays = 7;            // older day files are deleted
        public const int TopByRam = 15;           // programs recorded per sample, by RAM
        public const int TopByIo = 5;             // plus the busiest by read/write, even when small in RAM
        public const int DriveEverySamples = 10;  // free disk space changes slowly: every 10th sample
        public const string FileDateFormat = "yyyy-MM-dd";
        public const string TimeFormat = "HH:mm:ss";
        public const string Header = "#MemoryPulse history v1";
        public static string Dir => Path.Combine(AppPaths.DataDir, "history");
    }

    /// <summary>One program (all its processes) in one sample.</summary>
    internal sealed class ProgramSample
    {
        public string Name;
        public int Count;
        public long WorkingSet, Read, Write; // read/write: bytes during the interval
    }

    /// <summary>One point of the system charts (an average over one bucket).</summary>
    internal struct SysPoint
    {
        public DateTime Time;
        public double Available, Cached;                 // bytes; Cached is NaN when it was not readable
        public double DiskRead, DiskWrite, NetRx, NetTx; // bytes per second
    }

    /// <summary>What one program did in the selected range.</summary>
    internal sealed class ProgramStat
    {
        public string Name;
        public int Samples;
        public double SumWorkingSet;
        public long PeakWorkingSet, Read, Write, NetRx, NetTx;
        public DateTime PeakTime;
        public bool HasNet; // true only for programs whose network use was watched
        public double AverageWorkingSet => Samples == 0 ? 0 : SumWorkingSet / Samples;
    }

    internal sealed class DriveStat
    {
        public string Name;
        public long FirstFree, LastFree, Total;
    }

    /// <summary>Everything the history window shows for one range.</summary>
    internal sealed class HistoryData
    {
        public readonly List<SysPoint> System = new List<SysPoint>();
        public readonly Dictionary<string, ProgramStat> Programs = new Dictionary<string, ProgramStat>(StringComparer.OrdinalIgnoreCase);
        public readonly SortedDictionary<string, DriveStat> Drives = new SortedDictionary<string, DriveStat>(StringComparer.OrdinalIgnoreCase);
        public readonly List<DateTime> ProgramTimes = new List<DateTime>(); // filled only when one program was asked for
        public readonly List<double> ProgramRam = new List<double>();
        public int BucketSeconds;
        public long DiskRead, DiskWrite, NetRx, NetTx;
        public long MinAvailable = long.MaxValue;
        public DateTime MinAvailableTime;
        public bool Empty => System.Count == 0;
    }

    /// <summary>Byte counts and rates as short text in the UI language.</summary>
    internal static class Fmt
    {
        private const double Step = 1024;
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

        public static string Bytes(double bytes, string format = null)
        {
            if (double.IsNaN(bytes) || bytes < 0) return L.T("value.na");
            int unit = 0;
            while (bytes >= Step && unit < Units.Length - 1) { bytes /= Step; unit++; }
            return bytes.ToString(format ?? (unit == 0 || bytes >= 100 ? "0" : "0.0"), L.Culture) + " " + Units[unit];
        }

        public static string Rate(double bytesPerSecond, string format = null) =>
            Bytes(bytesPerSecond, format) + L.T("unit.perSec");
    }
}

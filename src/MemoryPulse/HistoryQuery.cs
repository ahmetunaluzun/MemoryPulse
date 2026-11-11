using System;

namespace MemoryPulse
{
    /// <summary>Reads the day files for a range and adds them up for the charts and tables.</summary>
    internal static class HistoryQuery
    {
        private const int MaxChartPoints = 720; // longer ranges are averaged into this many buckets

        /// <param name="program">When set, only this program's rows are read and its RAM series is kept.</param>
        public static HistoryData Load(DateTime from, DateTime to, string program)
        {
            var d = new HistoryData();
            d.BucketSeconds = Math.Max(HistoryConfig.SampleSeconds, (int)((to - from).TotalSeconds / MaxChartPoints));
            var bucket = new Bucket();
            foreach (var line in HistoryStore.Read(from, to))
            {
                switch (line.Kind)
                {
                    case 'S': AddSystem(d, ref bucket, line); break;
                    case 'D': AddDrive(d, line); break;
                    case 'P': AddProgram(d, line, program); break;
                    case 'N': AddNetwork(d, line, program); break;
                }
            }
            bucket.Flush(d);
            return d;
        }

        private struct Bucket
        {
            public long Slot;
            public int Count, CachedCount;
            public double Available, Cached, Seconds, DiskRead, DiskWrite, NetRx, NetTx;
            public DateTime Last;

            public void Flush(HistoryData d)
            {
                if (Count == 0) return;
                double sec = Math.Max(1, Seconds);
                d.System.Add(new SysPoint
                {
                    Time = Last,
                    Available = Available / Count,
                    Cached = CachedCount == 0 ? double.NaN : Cached / CachedCount,
                    DiskRead = DiskRead / sec, DiskWrite = DiskWrite / sec, NetRx = NetRx / sec, NetTx = NetTx / sec
                });
                this = default(Bucket);
            }
        }

        // S values: seconds, available, cached, commit, diskRead, diskWrite, netRx, netTx
        private static void AddSystem(HistoryData d, ref Bucket b, HistoryLine line)
        {
            long slot = line.Time.Ticks / (TimeSpan.TicksPerSecond * d.BucketSeconds);
            if (slot != b.Slot) { b.Flush(d); b.Slot = slot; }
            long[] v = line.V;
            b.Count++;
            b.Last = line.Time;
            b.Seconds += v[0];
            b.Available += v[1];
            if (v[2] >= 0) { b.Cached += v[2]; b.CachedCount++; }
            b.DiskRead += Positive(v[4]); b.DiskWrite += Positive(v[5]);
            b.NetRx += Positive(v[6]); b.NetTx += Positive(v[7]);
            d.DiskRead += Positive(v[4]); d.DiskWrite += Positive(v[5]);
            d.NetRx += Positive(v[6]); d.NetTx += Positive(v[7]);
            if (v[1] < d.MinAvailable) { d.MinAvailable = v[1]; d.MinAvailableTime = line.Time; }
        }

        private static long Positive(long v) => v < 0 ? 0 : v;

        private static void AddDrive(HistoryData d, HistoryLine line)
        {
            if (!d.Drives.TryGetValue(line.Name, out var s))
                d.Drives[line.Name] = s = new DriveStat { Name = line.Name, FirstFree = line.V[0] };
            s.LastFree = line.V[0];
            s.Total = line.V[1];
        }

        private static ProgramStat StatFor(HistoryData d, string name)
        {
            if (!d.Programs.TryGetValue(name, out var s)) d.Programs[name] = s = new ProgramStat { Name = name };
            return s;
        }

        private static bool Wanted(string name, string program) =>
            program == null || string.Equals(name, program, StringComparison.OrdinalIgnoreCase);

        // P values: process count, working set, read, write
        private static void AddProgram(HistoryData d, HistoryLine line, string program)
        {
            if (!Wanted(line.Name, program)) return;
            var s = StatFor(d, line.Name);
            long ws = line.V[1];
            s.Samples++;
            s.SumWorkingSet += ws;
            if (ws > s.PeakWorkingSet) { s.PeakWorkingSet = ws; s.PeakTime = line.Time; }
            s.Read += Positive(line.V[2]);
            s.Write += Positive(line.V[3]);
            if (program != null) { d.ProgramTimes.Add(line.Time); d.ProgramRam.Add(ws); }
        }

        private static void AddNetwork(HistoryData d, HistoryLine line, string program)
        {
            if (!Wanted(line.Name, program)) return;
            var s = StatFor(d, line.Name);
            s.HasNet = true;
            s.NetRx += Positive(line.V[0]);
            s.NetTx += Positive(line.V[1]);
        }
    }
}

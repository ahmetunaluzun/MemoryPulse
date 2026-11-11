using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MemoryPulse
{
    /// <summary>
    /// Writes one usage record per minute on a background thread: system memory, disk, network,
    /// the biggest programs, and network bytes of the programs the user chose to watch.
    /// </summary>
    internal sealed class HistoryRecorder : IDisposable
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly object _gate = new object(); // one sample or reconfiguration at a time
        private readonly Timer _timer;
        private volatile bool _enabled;
        private volatile HashSet<string> _watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private SystemSampler _system;
        private ProcessSampler _processes;
        private NetworkTrace _trace;
        private bool _traceFailed, _staleChecked;
        private DateTime _lastUtc, _cleanedDay;
        private int _sampleNo;

        /// <summary>Per-program network measurement needs administrator rights, which the Store edition cannot have.</summary>
        public static bool CanTraceNetwork => !Edition.IsStore && Native.IsElevated;

        public HistoryRecorder()
        {
            int period = HistoryConfig.SampleSeconds * 1000;
            _timer = new Timer(_ => Run(true), null, period, period);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => _trace?.Dispose(); // never leave the session behind
        }

        /// <summary>Called at start and whenever settings or the watch list change. Returns at once.</summary>
        public void Apply(Settings s)
        {
            _enabled = s.HistoryEnabled;
            _watched = new HashSet<string>(s.WatchedNetwork, StringComparer.OrdinalIgnoreCase);
            ThreadPool.QueueUserWorkItem(_ => Run(false));
        }

        private void Run(bool sample)
        {
            // A sample that is still running when the next one is due is not queued behind it.
            if (sample) { if (!Monitor.TryEnter(_gate)) return; }
            else Monitor.Enter(_gate);
            try
            {
                if (!_enabled) { Release(); return; }
                bool ready = _system != null;
                if (!ready) Prepare();
                UpdateTrace();
                if (sample && ready) Sample();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception
                                       || ex is InvalidOperationException)
            {
                Log.Write("ERROR", "History sample failed: " + ex.Message);
            }
            catch (Exception ex) // thread boundary: an escaped exception here would end the app
            {
                Log.Write("ERROR", "History sample failed unexpectedly: " + ex);
            }
            finally { Monitor.Exit(_gate); }
        }

        // First readings to compare the next sample with.
        private void Prepare()
        {
            // Fields are set only after the first readings succeed; a half-done start would make the next sample
            // compare with nothing (seconds since year 1, missing I/O baseline).
            var system = new SystemSampler();
            var processes = new ProcessSampler();
            processes.Sample();
            _processes = processes;
            _lastUtc = DateTime.UtcNow;
            _system = system;
            _sampleNo = 0;
            Log.Write("INFO", $"History recording on: every {HistoryConfig.SampleSeconds} s, kept {HistoryConfig.KeepDays} days, in {HistoryConfig.Dir}");
        }

        private void Release()
        {
            if (_system == null && _trace == null) return;
            _system?.Dispose();
            _system = null;
            _processes = null;
            _trace?.Dispose();
            _trace = null;
            Log.Write("INFO", "History recording off");
        }

        private void UpdateTrace()
        {
            if (!CanTraceNetwork) return;
            if (!_staleChecked) { _staleChecked = true; NetworkTrace.StopStale(); }
            bool wanted = _watched.Count > 0;
            if (wanted && _trace == null && !_traceFailed)
            {
                _trace = NetworkTrace.Start();
                _traceFailed = _trace == null; // do not retry every minute; a restart tries again
            }
            else if (!wanted && _trace != null)
            {
                _trace.Dispose();
                _trace = null;
            }
        }

        private void Sample()
        {
            DateTime now = DateTime.Now, utc = DateTime.UtcNow;
            double seconds = (utc - _lastUtc).TotalSeconds;
            if (seconds < 1) return;
            _lastUtc = utc;
            string time = now.ToString(HistoryConfig.TimeFormat, Inv);
            var sb = new StringBuilder(2048);

            var mem = MemoryInfo.Read();
            _system.ReadDisk(seconds, out long diskRead, out long diskWrite);
            _system.ReadNetwork(out long rx, out long tx);
            sb.Append("S,").Append(time);
            foreach (long v in new[] { (long)Math.Round(seconds), (long)mem.Available, mem.HasDetail ? (long)mem.Cached : -1,
                                       (long)mem.CommitUsed, diskRead, diskWrite, rx, tx })
                sb.Append(',').Append(v.ToString(Inv));
            sb.AppendLine();
            if (_sampleNo++ % HistoryConfig.DriveEverySamples == 0) SystemSampler.AppendDrives(sb, time);

            var watched = _watched;
            var all = _processes.Sample();
            foreach (var p in ProcessSampler.Pick(all, watched))
                sb.Append("P,").Append(time).Append(',').Append(HistoryStore.Clean(p.Name)).Append(',').Append(p.Count.ToString(Inv)).Append(',')
                  .Append(p.WorkingSet.ToString(Inv)).Append(',').Append(p.Read.ToString(Inv)).Append(',').Append(p.Write.ToString(Inv)).AppendLine();
            if (_trace != null) AppendNetwork(sb, time, watched, all);

            HistoryStore.Append(now, sb.ToString());
            if (_cleanedDay != now.Date) { _cleanedDay = now.Date; HistoryStore.DeleteOld(now); }
        }

        // Bytes arrive per PID; the watched programs' PIDs are added up by program name.
        private void AppendNetwork(StringBuilder sb, string time, HashSet<string> watched, Dictionary<string, ProgramSample> running)
        {
            var totals = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _trace.Take())
            {
                int pid = (int)kv.Key;
                if (!_processes.Names.TryGetValue(pid, out string name) && !_processes.OlderNames.TryGetValue(pid, out name)) continue;
                if (!watched.Contains(name)) continue;
                if (!totals.TryGetValue(name, out long[] t)) totals[name] = t = new long[2];
                t[0] += kv.Value[0];
                t[1] += kv.Value[1];
            }
            foreach (string name in watched)
            {
                bool used = totals.TryGetValue(name, out long[] t);
                if (!used && !running.ContainsKey(name)) continue; // not running: no row, not a zero
                sb.Append("N,").Append(time).Append(',').Append(HistoryStore.Clean(name)).Append(',')
                  .Append((used ? t[0] : 0).ToString(Inv)).Append(',').Append((used ? t[1] : 0).ToString(Inv)).AppendLine();
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
            _enabled = false;
            lock (_gate) Release();
        }
    }
}

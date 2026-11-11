using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MemoryPulse
{
    /// <summary>
    /// Bytes sent/received per process, from the Windows kernel's own network events (ETW provider
    /// Microsoft-Windows-Kernel-Network, read in real time). Nothing is captured but the size of each
    /// packet and the PID it belongs to. Needs administrator rights; the session is system-wide and
    /// outlives a crashed process, so a leftover session with our name is stopped before starting.
    /// </summary>
    internal sealed class NetworkTrace : IDisposable
    {
        private const string SessionName = "MemoryPulse-Net";
        private static readonly Guid Provider = new Guid("7DD42A49-5329-4832-8DFD-43D979153A88");
        private const ulong KeywordIpv4AndIpv6 = 0x10 | 0x20;
        private const byte LevelInformation = 4;
        private const int ThreadJoinMs = 3000;
        private const int MinPayload = 8;        // PID (uint32) + size (uint32) lead every event we use
        // Offsets inside EVENT_RECORD (fixed layout, see evntcons.h).
        private const int HeaderPidOffset = 12, EventIdOffset = 40, UserDataLengthOffset = 86, ExtendedDataOffset = 88;
        private static readonly int UserDataOffset = ExtendedDataOffset + IntPtr.Size;

        private readonly object _gate = new object();
        private Dictionary<uint, long[]> _bytes = new Dictionary<uint, long[]>(); // pid -> { received, sent }
        private readonly Etw.EventRecordCallback _callback; // kept alive for the native side
        private ulong _session, _trace;
        private Thread _thread;
        private bool _firstLogged;

        private NetworkTrace() { _callback = OnEvent; }

        /// <summary>Starts the session, or returns null (and logs why) when Windows refuses.</summary>
        public static NetworkTrace Start()
        {
            StopStale();
            var t = new NetworkTrace();
            IntPtr props = Etw.NewProperties(SessionName);
            try
            {
                int err = Etw.StartTraceW(out t._session, SessionName, props);
                if (err != 0) { Log.Write("WARN", $"Network trace could not start (StartTrace error {err})"); return null; }
                Guid provider = Provider;
                err = Etw.EnableTraceEx2(t._session, ref provider, Etw.EnableProvider, LevelInformation, KeywordIpv4AndIpv6, 0, 0, IntPtr.Zero);
                if (err != 0) Log.Write("WARN", $"Network trace could not start (EnableTraceEx2 error {err})");
                else if (t.Open()) { Log.Write("INFO", "Network trace started for watched programs"); return t; }
                t.Dispose();
                return null;
            }
            finally { Marshal.FreeHGlobal(props); }
        }

        private bool Open()
        {
            var log = new Etw.EVENT_TRACE_LOGFILE
            {
                LoggerName = SessionName,
                ProcessTraceMode = Etw.RealTime | Etw.EventRecord,
                EventCallback = Marshal.GetFunctionPointerForDelegate(_callback)
            };
            _trace = Etw.OpenTraceW(ref log);
            if (Etw.IsInvalid(_trace))
            {
                Log.Write("WARN", $"Network trace could not be opened (OpenTrace error {Marshal.GetLastWin32Error()})");
                _trace = 0;
                return false;
            }
            _thread = new Thread(Pump) { IsBackground = true, Name = "net-trace" };
            _thread.Start();
            return true;
        }

        // ProcessTrace blocks until the session stops; the callback runs on this thread.
        private void Pump()
        {
            try
            {
                int err = Etw.ProcessTrace(new[] { _trace }, 1, IntPtr.Zero, IntPtr.Zero);
                if (err != 0) Log.Write("WARN", $"Network trace ended with error {err}");
            }
            catch (Exception ex) // thread boundary: nothing above us would report it
            {
                Log.Write("ERROR", "Network trace thread failed: " + ex);
            }
        }

        private static int Direction(ushort id)
        {
            switch (id)
            {
                case 11: case 27: case 43: case 59: return 0; // TCP/UDP received, IPv4/IPv6
                case 10: case 26: case 42: case 58: return 1; // TCP/UDP sent, IPv4/IPv6
                default: return -1;
            }
        }

        private void OnEvent(IntPtr record)
        {
            // Called from native code: an exception escaping here would end the process.
            try { Count(record); }
            catch (Exception ex) when (ex is OutOfMemoryException || ex is AccessViolationException || ex is ArgumentException)
            {
                if (_errorLogged) return;
                _errorLogged = true;
                Log.Write("WARN", "Network trace: an event could not be read and was skipped: " + ex.Message);
            }
        }

        private bool _errorLogged;

        private void Count(IntPtr record)
        {
            ushort id = (ushort)Marshal.ReadInt16(record, EventIdOffset);
            int direction = Direction(id);
            if (direction < 0 || (ushort)Marshal.ReadInt16(record, UserDataLengthOffset) < MinPayload) return;
            IntPtr data = Marshal.ReadIntPtr(record, UserDataOffset);
            uint pid = (uint)Marshal.ReadInt32(data, 0);
            uint size = (uint)Marshal.ReadInt32(data, sizeof(uint));
            lock (_gate)
            {
                if (!_bytes.TryGetValue(pid, out long[] totals)) _bytes[pid] = totals = new long[2];
                totals[direction] += size;
            }
            if (_firstLogged) return;
            _firstLogged = true;
            Log.Write("INFO", $"Network trace: first event received (id {id}, pid {pid}, {size} bytes, header pid {Marshal.ReadInt32(record, HeaderPidOffset)})");
        }

        /// <summary>Bytes per PID since the previous call: { received, sent }.</summary>
        public Dictionary<uint, long[]> Take()
        {
            lock (_gate)
            {
                var taken = _bytes;
                _bytes = new Dictionary<uint, long[]>();
                return taken;
            }
        }

        /// <summary>Stops a session left behind by an earlier run that did not exit cleanly.</summary>
        public static void StopStale()
        {
            IntPtr props = Etw.NewProperties(SessionName);
            try
            {
                if (Etw.ControlTraceW(0, SessionName, props, Etw.ControlStop) == 0)
                    Log.Write("INFO", "Network trace: stopped a session left over from an earlier run");
            }
            finally { Marshal.FreeHGlobal(props); }
        }

        public void Dispose()
        {
            if (_session != 0)
            {
                IntPtr props = Etw.NewProperties(SessionName);
                try { Etw.ControlTraceW(_session, null, props, Etw.ControlStop); }
                finally { Marshal.FreeHGlobal(props); }
                _session = 0;
                Log.Write("INFO", "Network trace stopped");
            }
            if (_trace != 0) { Etw.CloseTrace(_trace); _trace = 0; }
            if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(ThreadJoinMs);
            _thread = null;
        }
    }
}

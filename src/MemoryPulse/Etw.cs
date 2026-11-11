using System;
using System.Runtime.InteropServices;

namespace MemoryPulse
{
    /// <summary>Event Tracing for Windows: the few advapi32 calls and structures a real-time session needs.</summary>
    internal static class Etw
    {
        public const uint RealTime = 0x100;            // EVENT_TRACE_REAL_TIME_MODE / PROCESS_TRACE_MODE_REAL_TIME
        public const uint EventRecord = 0x10000000;    // PROCESS_TRACE_MODE_EVENT_RECORD
        public const uint EnableProvider = 1;          // EVENT_CONTROL_CODE_ENABLE_PROVIDER
        public const uint ControlStop = 1;             // EVENT_TRACE_CONTROL_STOP
        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const uint ClockQpc = 1;
        private const int NameRoomBytes = 2 * 1024;    // room for the session name Windows writes back (1024 UTF-16 chars)
        private const int TimeZoneBytes = 172;         // sizeof(TIME_ZONE_INFORMATION)

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void EventRecordCallback(IntPtr eventRecord);

        [StructLayout(LayoutKind.Sequential)]
        private struct WNODE_HEADER
        {
            public uint BufferSize, ProviderId;
            public ulong HistoricalContext;
            public long TimeStamp;
            public Guid Guid;
            public uint ClientContext, Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset, LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE_HEADER
        {
            public ushort Size, FieldTypeFlags;
            public byte Type, Level;
            public ushort Version;
            public uint ThreadId, ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public uint KernelTime, UserTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId, ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength, ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize, Version, ProviderVersion, NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution, MaximumFileSize, LogFileMode, BuffersWritten;
            public Guid LogInstanceGuid;
            public IntPtr LoggerName, LogFileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = TimeZoneBytes)] public byte[] TimeZone;
            public long BootTime, PerfFreq, StartTime;
            public uint ReservedFlags, BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct EVENT_TRACE_LOGFILE
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string LogFileName;
            [MarshalAs(UnmanagedType.LPWStr)] public string LoggerName;
            public long CurrentTime;
            public uint BuffersRead, ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize, Filled, EventsLost;
            public IntPtr EventCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int StartTraceW(out ulong session, string name, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int ControlTraceW(ulong session, string name, IntPtr properties, uint control);

        [DllImport("advapi32.dll")]
        public static extern int EnableTraceEx2(ulong session, ref Guid provider, uint controlCode, byte level,
            ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr parameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ulong OpenTraceW(ref EVENT_TRACE_LOGFILE logfile);

        [DllImport("advapi32.dll")]
        public static extern int ProcessTrace(ulong[] handles, uint count, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        public static extern int CloseTrace(ulong handle);

        /// <summary>INVALID_PROCESSTRACE_HANDLE is all ones in the handle's native width (64 or 32 bits).</summary>
        public static bool IsInvalid(ulong traceHandle) => traceHandle == ulong.MaxValue || traceHandle == uint.MaxValue;

        /// <summary>A zeroed EVENT_TRACE_PROPERTIES for a real-time session, followed by room for the names. Free with FreeHGlobal.</summary>
        public static IntPtr NewProperties(string sessionName)
        {
            int size = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
            int total = size + NameRoomBytes;
            IntPtr buffer = Marshal.AllocHGlobal(total);
            Marshal.Copy(new byte[total], 0, buffer, total); // zero it
            var p = new EVENT_TRACE_PROPERTIES();
            p.Wnode.BufferSize = (uint)total;
            p.Wnode.Flags = WNODE_FLAG_TRACED_GUID;
            p.Wnode.ClientContext = ClockQpc;
            p.BufferSize = 64;   // KB
            p.FlushTimer = 1;    // seconds: events reach us within a second
            p.LogFileMode = RealTime;
            p.LoggerNameOffset = (uint)size;
            p.LogFileNameOffset = 0; // real time only: no log file
            Marshal.StructureToPtr(p, buffer, false);
            return buffer;
        }
    }
}

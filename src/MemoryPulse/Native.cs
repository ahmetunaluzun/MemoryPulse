using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MemoryPulse
{
    /// <summary>Windows memory APIs: overall status, page list sizes and standby purge.</summary>
    internal static class Native
    {
        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
        private const uint TOKEN_QUERY = 0x8;
        private const uint SE_PRIVILEGE_ENABLED = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int infoClass, ref int command, int length);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string system, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state,
            int length, IntPtr previous, IntPtr returnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr process);

        /// <summary>Returns this process's unused pages to Windows (they come back on demand).</summary>
        public static void TrimWorkingSet() => EmptyWorkingSet(GetCurrentProcess());

        public static bool IsElevated
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>Total / available RAM and commit charge (bytes).</summary>
        public static void GetMemoryStatus(out ulong total, out ulong available, out ulong commitUsed, out ulong commitLimit)
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (!GlobalMemoryStatusEx(ref m)) throw new Win32Exception();
            total = m.ullTotalPhys;
            available = m.ullAvailPhys;
            commitLimit = m.ullTotalPageFile;
            commitUsed = m.ullTotalPageFile - m.ullAvailPageFile;
        }

        /// <summary>
        /// Reads SYSTEM_MEMORY_LIST_INFORMATION (bytes). Usually needs administrator rights;
        /// returns false when the call is not allowed so the caller can fall back.
        /// </summary>
        public static bool TryGetMemoryLists(out ulong standby, out ulong free, out ulong modified)
        {
            standby = free = modified = 0;
            int p = IntPtr.Size;
            int size = p * 22; // 5 counters + 8 standby priorities + 8 repurposed + 1
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (NtQuerySystemInformation(SystemMemoryListInformation, buf, size, out _) != 0) return false;
                ulong page = (ulong)Environment.SystemPageSize;
                ulong Read(int index) => (ulong)Marshal.ReadIntPtr(buf, index * p).ToInt64();
                free = (Read(0) + Read(1)) * page;          // zero + free
                modified = (Read(2) + Read(3)) * page;      // modified + modified-no-write
                for (int i = 0; i < 8; i++) standby += Read(5 + i);
                standby *= page;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>Empties the standby list. Requires administrator rights.</summary>
        public static void PurgeStandbyList()
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int command = MemoryPurgeStandbyList;
            int status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
            if (status != 0) throw new Win32Exception($"NtSetSystemInformation failed: 0x{status:X8}");
        }

        private static void EnablePrivilege(string name)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                throw new Win32Exception();
            try
            {
                if (!LookupPrivilegeValue(null, name, out LUID luid)) throw new Win32Exception();
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                int err = Marshal.GetLastWin32Error();
                if (err != 0) throw new Win32Exception(err); // ERROR_NOT_ALL_ASSIGNED: not elevated
            }
            finally
            {
                CloseHandle(token);
            }
        }
    }
}

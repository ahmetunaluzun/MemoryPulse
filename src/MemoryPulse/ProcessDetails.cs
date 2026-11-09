using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MemoryPulse
{
    /// <summary>One running instance of a program, read on demand for the details window.</summary>
    internal sealed class ProcessInstance
    {
        public int Pid;
        public ulong WorkingSet;
        public string Path;          // null: not readable (protected or system process)
        public string Description;
        public string Company;
        public string WorkingFolder; // null: not readable
        public string CommandLine;   // only for script hosts, secrets masked; null otherwise
    }

    /// <summary>
    /// Reads the executable path, file description, working folder and (for script hosts)
    /// the command line of each process with a given name. Nothing here is logged or stored.
    /// </summary>
    internal static class ProcessDetails
    {
        // Programs whose name alone says little: what they run is in the command line.
        private static readonly HashSet<string> ScriptHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node", "python", "pythonw", "py", "java", "javaw", "powershell", "pwsh", "cmd", "wscript", "cscript",
            "dotnet", "msedgewebview2", "svchost", "ruby", "php", "perl", "bun", "deno", "rundll32", "dllhost", "mshta"
        };

        private static readonly Regex Secret = new Regex(
            @"(?ix)
              ( (?:--?|/)? (?:[\w.]+[-_.])? (?:password|passwd|pwd|token|secret|api[-_]?key|access[-_]?key|auth[-_]?token|credentials?) \s*[=:]\s*
              | (?:--?|/)   (?:password|passwd|pwd|token|secret|api[-_]?key|access[-_]?key|auth[-_]?token)\s+
              | \s/p\s+ )
              (""[^""]*""|'[^']*'|\S+)",
            RegexOptions.Compiled);

        private static readonly Regex UrlCredential = new Regex(@"(?i)(\b[a-z][a-z0-9+.-]*://[^\s:/@]+:)[^\s@/]+@", RegexOptions.Compiled);

        // Processes created by the Windows kernel itself (or minimal processes): no program file,
        // no command line, and nobody - administrator included - can look inside them.
        private static readonly Dictionary<string, string> KernelProcesses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Memory Compression"] = "details.sys.compression",
            ["System"] = "details.sys.system",
            ["Registry"] = "details.sys.registry",
            ["Secure System"] = "details.sys.secure",
            ["Idle"] = "details.sys.idle",
            ["vmmem"] = "details.sys.vmmem",
            ["vmmemWSL"] = "details.sys.vmmem",
        };

        public static bool ShowsCommandLine(string name) => ScriptHosts.Contains(name);

        /// <summary>Language key explaining a kernel process, or null for ordinary programs.</summary>
        public static string KernelExplanationKey(string name) => KernelProcesses.TryGetValue(name, out var key) ? key : null;

        public static List<ProcessInstance> Read(string name)
        {
            var result = new List<ProcessInstance>();
            var versionCache = new Dictionary<string, FileVersionInfo>(StringComparer.OrdinalIgnoreCase);
            bool wantCmd = ShowsCommandLine(name);
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    var inst = new ProcessInstance { Pid = p.Id };
                    try { inst.WorkingSet = (ulong)p.WorkingSet64; }
                    catch (InvalidOperationException) { continue; } // exited meanwhile
                    ReadNative(p.Id, wantCmd, inst);
                    if (inst.Path != null)
                    {
                        if (!versionCache.TryGetValue(inst.Path, out var v))
                        {
                            try { v = FileVersionInfo.GetVersionInfo(inst.Path); }
                            catch (FileNotFoundException) { v = null; }
                            versionCache[inst.Path] = v;
                        }
                        inst.Description = Blank(v?.FileDescription);
                        inst.Company = Blank(v?.CompanyName);
                    }
                    result.Add(inst);
                }
            }
            return result.OrderByDescending(i => i.WorkingSet).ToList();
        }

        /// <summary>Hides values that look like passwords, tokens or keys.</summary>
        public static string MaskSecrets(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine)) return commandLine;
            string s = Secret.Replace(commandLine, m => m.Groups[1].Value + "••••");
            return UrlCredential.Replace(s, m => m.Groups[1].Value + "••••@");
        }

        /// <summary>The command line without the leading program path: what the program was asked to run.</summary>
        public static string Arguments(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine)) return commandLine;
            string s = commandLine.TrimStart();
            int end = s.StartsWith("\"") ? s.IndexOf('"', 1) + 1 : s.IndexOf(' ');
            string rest = end <= 0 || end >= s.Length ? "" : s.Substring(end).Trim();
            return rest.Length == 0 ? s : rest;
        }

        private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // ---------- native ----------

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint PROCESS_VM_READ = 0x10;
        private const int ProcessBasicInformation = 0;
        private const int ProcessCommandLineInformation = 60;
        private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr process, out bool wow64);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr buffer, int length, out int returnLength);

        private static void ReadNative(int pid, bool wantCmd, ProcessInstance inst)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
            bool canRead = h != IntPtr.Zero;
            if (!canRead) h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref size)) inst.Path = sb.ToString(0, size);
                if (wantCmd)
                {
                    string cmd = ReadCommandLine(h);
                    if (cmd != null) inst.CommandLine = MaskSecrets(cmd);
                }
                if (canRead) inst.WorkingFolder = ReadWorkingFolder(h);
            }
            finally
            {
                CloseHandle(h);
            }
        }

        // Windows 8.1+: returns a UNICODE_STRING followed by its buffer.
        private static string ReadCommandLine(IntPtr h)
        {
            NtQueryInformationProcess(h, ProcessCommandLineInformation, IntPtr.Zero, 0, out int needed);
            if (needed <= 0 || needed > 1 << 20) return null;
            IntPtr buf = Marshal.AllocHGlobal(needed);
            try
            {
                if (NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, needed, out _) != 0) return null;
                int len = (ushort)Marshal.ReadInt16(buf);
                IntPtr text = Marshal.ReadIntPtr(buf, IntPtr.Size);
                return len == 0 || text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text, len / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        // PEB -> ProcessParameters -> CurrentDirectory.DosPath. Same bitness only: a 32-bit process
        // on 64-bit Windows keeps its own copy that the native one does not follow, so it is skipped.
        private static string ReadWorkingFolder(IntPtr h)
        {
            if (Environment.Is64BitOperatingSystem && IsWow64Process(h, out bool wow) && wow != !Environment.Is64BitProcess)
                return null;
            int p = IntPtr.Size;
            IntPtr pbi = Marshal.AllocHGlobal(p * 6);
            try
            {
                if (NtQueryInformationProcess(h, ProcessBasicInformation, pbi, p * 6, out _) != 0) return null;
                IntPtr peb = Marshal.ReadIntPtr(pbi, p); // PebBaseAddress
                IntPtr parameters = ReadPointer(h, peb + (p == 8 ? 0x20 : 0x10));
                if (parameters == IntPtr.Zero) return null;
                int curDir = p == 8 ? 0x38 : 0x24; // CURDIR.DosPath (UNICODE_STRING)
                byte[] us = ReadBytes(h, parameters + curDir, p * 2);
                if (us == null) return null;
                int len = BitConverter.ToUInt16(us, 0);
                IntPtr text = p == 8 ? new IntPtr(BitConverter.ToInt64(us, 8)) : new IntPtr(BitConverter.ToInt32(us, 4));
                if (len == 0 || len > 4096 || text == IntPtr.Zero) return null;
                byte[] chars = ReadBytes(h, text, len);
                return chars == null ? null : Encoding.Unicode.GetString(chars);
            }
            finally
            {
                Marshal.FreeHGlobal(pbi);
            }
        }

        private static IntPtr ReadPointer(IntPtr h, IntPtr address)
        {
            byte[] b = ReadBytes(h, address, IntPtr.Size);
            if (b == null) return IntPtr.Zero;
            return IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(b, 0)) : new IntPtr(BitConverter.ToInt32(b, 0));
        }

        private static byte[] ReadBytes(IntPtr h, IntPtr address, int count)
        {
            var b = new byte[count];
            return ReadProcessMemory(h, address, b, new IntPtr(count), out IntPtr read) && read.ToInt64() == count ? b : null;
        }
    }
}

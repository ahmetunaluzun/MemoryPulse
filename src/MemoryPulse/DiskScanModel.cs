using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MemoryPulse
{
    /// <summary>One folder of a disk space scan. Only folders are kept; files are summed into them.</summary>
    internal sealed class DirNode
    {
        public string Name;               // the root holds the full path that was scanned
        public DirNode Parent;
        public DirNode[] Children;        // null when the folder has no subfolders to show
        public long Size, OwnSize;        // Own*: files directly in this folder; the rest covers the whole subtree
        public long Modified, OwnModified; // newest file, as a Windows file time
        public int Files, OwnFiles, Dirs;
        public bool Unreadable, Sorted;

        public string FullPath
        {
            get
            {
                if (Parent == null) return Name;
                var parts = new Stack<string>();
                DirNode n = this;
                for (; n.Parent != null; n = n.Parent) parts.Push(n.Name);
                var sb = new StringBuilder(n.Name.TrimEnd('\\'));
                foreach (string part in parts) sb.Append('\\').Append(part);
                return sb.ToString();
            }
        }
    }

    internal sealed class FileEntry
    {
        public DirNode Dir;
        public string Name;
        public long Size, Modified;
        public string Path => Dir.FullPath.TrimEnd('\\') + "\\" + Name;
    }

    internal sealed class ExtStat
    {
        public string Extension; // lower case with the dot; "" for files without one
        public long Size;
        public int Count;
    }

    internal sealed class ScanResult
    {
        public DirNode Root;
        public List<FileEntry> TopFiles;
        public List<ExtStat> Types;
        public long Files, Folders, Unreadable;
        public bool Cancelled;
        public TimeSpan Elapsed;
    }

    /// <summary>Windows folder listing calls used by the disk space scan.</summary>
    internal static class DiskNative
    {
        public const uint AttrDirectory = 0x10, AttrReparsePoint = 0x400, AttrRecallOnOpen = 0x40000;
        public const uint NameSurrogateTag = 0x20000000; // junctions and symbolic links: they point somewhere else
        public const int FindExInfoBasic = 1, LargeFetch = 2;
        public const int ErrorFileNotFound = 2, ErrorNoMoreFiles = 18;
        public const uint InvalidAttributes = 0xFFFFFFFF;
        public static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private const long MaxFileTime = 2650467743999999999; // DateTime.MaxValue

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint Attributes;
            public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint SizeHigh, SizeLow;
            public uint ReparseTag, Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileExW(string fileName, int infoLevel, out WIN32_FIND_DATA data,
            int searchOp, IntPtr searchFilter, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFileW(IntPtr handle, out WIN32_FIND_DATA data);

        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributesW(string path);

        /// <summary>The path in the form that is not limited to 260 characters.</summary>
        public static string Long(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }

        public static bool Exists(string path) => GetFileAttributesW(Long(path)) != InvalidAttributes;

        /// <summary>A file time as local date and time; empty when the file has none or it is out of range.</summary>
        public static string Date(long fileTime) =>
            fileTime <= 0 || fileTime > MaxFileTime ? "" : DateTime.FromFileTime(fileTime).ToString("g", L.Culture);
    }
}

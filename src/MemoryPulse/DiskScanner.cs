using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MemoryPulse
{
    /// <summary>
    /// Sums file sizes under a folder. Read-only. Junctions and symbolic links are counted as one entry and
    /// not followed, folders that cannot be read are counted and skipped. Runs on low-priority threads.
    /// </summary>
    internal sealed class DiskScanner
    {
        public const int Workers = 2;             // the PC must stay responsive while a whole drive is read
        public const int TopFileCount = 100;
        public const int TopTypeCount = 50;
        private const int MaxExtensions = 50000;  // names with random "extensions" must not grow the table without end
        private const int MaxExtensionLength = 16;

        private readonly Stack<KeyValuePair<DirNode, string>> _work = new Stack<KeyValuePair<DirNode, string>>();
        private readonly object _gate = new object();
        private int _busy;
        private volatile bool _cancel;
        private long _files, _folders, _bytes, _unreadable;
        private volatile string _current = "";

        // Progress, readable from the UI thread while the scan runs.
        public long Files => Interlocked.Read(ref _files);
        public long Folders => Interlocked.Read(ref _folders);
        public long Bytes => Interlocked.Read(ref _bytes);
        public long Unreadable => Interlocked.Read(ref _unreadable);
        public string Current => _current;

        public void Cancel()
        {
            _cancel = true;
            lock (_gate) Monitor.PulseAll(_gate);
        }

        /// <summary>Scans and returns when done or cancelled (then with what was read so far). Blocks: call it off the UI thread.</summary>
        public ScanResult Run(string rootPath)
        {
            var watch = Stopwatch.StartNew();
            var root = new DirNode { Name = rootPath };
            _work.Push(new KeyValuePair<DirNode, string>(root, rootPath));
            var locals = new Tally[Workers];
            var threads = new Thread[Workers];
            for (int i = 0; i < Workers; i++)
            {
                var tally = locals[i] = new Tally();
                threads[i] = new Thread(() => Work(tally)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "disk-scan-" + i };
                threads[i].Start();
            }
            foreach (var t in threads) t.Join();
            Aggregate(root);
            return new ScanResult
            {
                Root = root,
                TopFiles = locals.SelectMany(l => l.Top).OrderByDescending(f => f.Size).Take(TopFileCount).ToList(),
                Types = MergeTypes(locals),
                Files = Files, Folders = Folders, Unreadable = Unreadable,
                Cancelled = _cancel,
                Elapsed = watch.Elapsed
            };
        }

        private void Work(Tally tally)
        {
            var found = new List<KeyValuePair<DirNode, string>>();
            while (true)
            {
                KeyValuePair<DirNode, string> item;
                lock (_gate)
                {
                    while (_work.Count == 0 && _busy > 0 && !_cancel) Monitor.Wait(_gate);
                    if (_cancel || _work.Count == 0) { Monitor.PulseAll(_gate); return; }
                    item = _work.Pop();
                    _busy++;
                }
                found.Clear();
                try { Enumerate(item.Key, item.Value, tally, found); }
                finally
                {
                    lock (_gate)
                    {
                        foreach (var child in found) _work.Push(child);
                        _busy--;
                        Monitor.PulseAll(_gate);
                    }
                }
            }
        }

        private void Enumerate(DirNode node, string path, Tally tally, List<KeyValuePair<DirNode, string>> found)
        {
            _current = path;
            string prefix = path.EndsWith("\\", StringComparison.Ordinal) ? path : path + "\\";
            IntPtr handle = DiskNative.FindFirstFileExW(DiskNative.Long(prefix) + "*", DiskNative.FindExInfoBasic,
                out DiskNative.WIN32_FIND_DATA data, 0, IntPtr.Zero, DiskNative.LargeFetch);
            if (handle == DiskNative.InvalidHandle)
            {
                if (Marshal.GetLastWin32Error() != DiskNative.ErrorFileNotFound) MarkUnreadable(node);
                return;
            }
            long bytes = 0;
            int files = 0, folders = 0;
            try
            {
                bool more = true;
                while (more && !_cancel)
                {
                    string name = data.FileName;
                    if ((data.Attributes & DiskNative.AttrDirectory) != 0)
                    {
                        if (name != "." && name != "..")
                        {
                            folders++;
                            if (ShouldEnter(ref data)) found.Add(new KeyValuePair<DirNode, string>(new DirNode { Name = name, Parent = node }, prefix + name));
                        }
                    }
                    else
                    {
                        long size = ((long)data.SizeHigh << 32) | data.SizeLow;
                        long modified = ((long)data.WriteHigh << 32) | data.WriteLow;
                        files++;
                        bytes += size;
                        if (modified > node.OwnModified) node.OwnModified = modified;
                        tally.Add(node, name, size, modified);
                    }
                    more = DiskNative.FindNextFileW(handle, out data);
                    // The listing stopped for another reason than its end: the drive went away or reading was refused.
                    if (!more && Marshal.GetLastWin32Error() != DiskNative.ErrorNoMoreFiles) MarkUnreadable(node);
                }
            }
            finally { DiskNative.FindClose(handle); }

            node.OwnSize = bytes;
            node.OwnFiles = files;
            node.Dirs = folders;
            if (found.Count > 0)
            {
                node.Children = new DirNode[found.Count];
                for (int i = 0; i < found.Count; i++) node.Children[i] = found[i].Key;
            }
            Interlocked.Add(ref _files, files);
            Interlocked.Add(ref _folders, folders);
            Interlocked.Add(ref _bytes, bytes);
        }

        // A link (junction, symbolic link) leads to files that are counted where they really are, and can loop.
        // A cloud folder that is not on this PC would be downloaded just by listing it.
        private static bool ShouldEnter(ref DiskNative.WIN32_FIND_DATA data)
        {
            if ((data.Attributes & DiskNative.AttrRecallOnOpen) != 0) return false;
            return (data.Attributes & DiskNative.AttrReparsePoint) == 0 || (data.ReparseTag & DiskNative.NameSurrogateTag) == 0;
        }

        private void MarkUnreadable(DirNode node)
        {
            if (node.Unreadable) return;
            node.Unreadable = true;
            Interlocked.Increment(ref _unreadable);
        }

        /// <summary>Adds every folder's own totals to all its parents, without recursion (paths can be very deep).</summary>
        private static void Aggregate(DirNode root)
        {
            var all = new List<DirNode> { root };
            for (int i = 0; i < all.Count; i++)
            {
                DirNode n = all[i];
                n.Size = n.OwnSize;
                n.Files = n.OwnFiles;
                n.Modified = n.OwnModified;
                if (n.Children != null) all.AddRange(n.Children);
            }
            for (int i = all.Count - 1; i > 0; i--) // parents come before their children in the list
            {
                DirNode n = all[i], p = n.Parent;
                p.Size += n.Size;
                p.Files += n.Files;
                p.Dirs += n.Dirs;
                if (n.Modified > p.Modified) p.Modified = n.Modified;
            }
        }

        private static List<ExtStat> MergeTypes(Tally[] locals)
        {
            var all = new Dictionary<string, ExtStat>(StringComparer.Ordinal);
            foreach (var local in locals)
                foreach (var e in local.Types.Values)
                {
                    if (!all.TryGetValue(e.Extension, out ExtStat sum)) all[e.Extension] = sum = new ExtStat { Extension = e.Extension };
                    sum.Size += e.Size;
                    sum.Count += e.Count;
                }
            return all.Values.OrderByDescending(e => e.Size).Take(TopTypeCount).ToList();
        }

        /// <summary>One worker's biggest files and per-extension totals; merged when the scan ends.</summary>
        private sealed class Tally
        {
            public readonly List<FileEntry> Top = new List<FileEntry>();
            public readonly Dictionary<string, ExtStat> Types = new Dictionary<string, ExtStat>(StringComparer.OrdinalIgnoreCase);
            private long _smallestKept = -1;

            public void Add(DirNode dir, string name, long size, long modified)
            {
                if (size > _smallestKept)
                {
                    Top.Add(new FileEntry { Dir = dir, Name = name, Size = size, Modified = modified });
                    if (Top.Count >= TopFileCount * 4) Trim();
                }
                int dot = name.LastIndexOf('.');
                string ext = dot <= 0 || dot == name.Length - 1 || name.Length - dot > MaxExtensionLength ? "" : name.Substring(dot);
                if (!Types.TryGetValue(ext, out ExtStat stat))
                {
                    if (Types.Count >= MaxExtensions && !Types.TryGetValue(ext = "", out stat)) return;
                    if (stat == null) Types[ext] = stat = new ExtStat { Extension = ext.ToLowerInvariant() };
                }
                stat.Size += size;
                stat.Count++;
            }

            private void Trim()
            {
                Top.Sort((a, b) => b.Size.CompareTo(a.Size));
                Top.RemoveRange(TopFileCount, Top.Count - TopFileCount);
                _smallestKept = Top[Top.Count - 1].Size;
            }
        }
    }
}

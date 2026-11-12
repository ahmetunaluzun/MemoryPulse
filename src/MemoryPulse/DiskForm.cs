using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemoryPulse
{
    /// <summary>Disk space: which folders, files and file types take the room on a drive. Read-only.</summary>
    internal sealed partial class DiskForm : Form
    {
        private const int ProgressMs = 250;      // progress text is refreshed at most four times a second
        private const int PathDisplayLength = 70;

        /// <summary>A drive or folder that can be scanned, as shown in the combo box.</summary>
        private sealed class Target
        {
            public string Path, Text;
            public override string ToString() => Text;
        }

        private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
        private readonly System.Windows.Forms.Timer _progressTimer = new System.Windows.Forms.Timer { Interval = ProgressMs };
        private readonly Stopwatch _watch = new Stopwatch();
        private ComboBox _targets;
        private Button _scan, _choose;
        private Label _progress, _status;
        private DirTree _tree;
        private ListView _files;
        private BarListView _types;
        private DiskScanner _scanner; // not null while a scan runs

        public DiskForm()
        {
            Text = L.T("disk.title");
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(960, 640);
            MinimumSize = new Size(640, 420);
            Padding = new Padding(10);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (SizeType type in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize })
                root.RowStyles.Add(new RowStyle(type, 100));
            Controls.Add(root);

            root.Controls.Add(BuildTopBar(), 0, 0);
            _progress = new Label { Text = L.T("disk.ready"), AutoEllipsis = true, Dock = DockStyle.Fill, Height = Font.Height + 6,
                                    Margin = new Padding(4, 0, 4, 2), ForeColor = Palette.Muted };
            root.Controls.Add(_progress, 0, 1);
            var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(4) };
            tabs.TabPages.Add(BuildFoldersTab());
            tabs.TabPages.Add(BuildFilesTab());
            tabs.TabPages.Add(BuildTypesTab());
            root.Controls.Add(tabs, 0, 2);
            _status = new Label { AutoSize = true, Margin = new Padding(4, 4, 4, 0) };
            root.Controls.Add(_status, 0, 3);
            var note = new Label { Text = L.T("disk.note"), AutoSize = true, ForeColor = Palette.Muted, Margin = new Padding(4, 2, 4, 0) };
            root.Controls.Add(note, 0, 4);
            root.Resize += (s, e) => note.MaximumSize = _status.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - 8), 0);

            _progressTimer.Tick += (s, e) => ShowProgress();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _scanner?.Cancel();
            _progressTimer.Dispose();
            _tips.Dispose();
            base.OnFormClosed(e);
        }

        private Control BuildTopBar()
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 2) };
            Button NewButton(string key) => new Button { Text = L.T(key), AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(4, 3, 4, 3) };
            _targets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, Margin = new Padding(4) };
            foreach (DriveUsage d in DiskMonitor.ReadDrives())
                _targets.Items.Add(new Target { Path = d.Name + "\\", Text = L.T("disk.driveItem", d.Title, Fmt.Bytes(d.Free), Fmt.Bytes(d.Total)) });
            if (_targets.Items.Count > 0) _targets.SelectedIndex = 0;
            _choose = NewButton("disk.choose");
            _choose.Click += (s, e) => ChooseFolder();
            _scan = NewButton("disk.scan");
            _scan.Click += (s, e) => ScanOrStop();
            _tips.SetToolTip(_scan, L.Tip("disk.scanTip"));

            f.Controls.Add(new Label { Text = L.T("disk.target"), AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
            f.Controls.Add(_targets);
            f.Controls.Add(_choose);
            f.Controls.Add(_scan);
            return f;
        }

        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = L.T("disk.chooseTitle"), ShowNewFolderButton = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) SelectTarget(dialog.SelectedPath);
        }

        /// <summary>Selects a path in the combo box, adding it when it is not one of the listed drives.</summary>
        private void SelectTarget(string path)
        {
            foreach (Target t in _targets.Items)
                if (string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) { _targets.SelectedItem = t; return; }
            var added = new Target { Path = path, Text = path };
            _targets.Items.Add(added);
            _targets.SelectedItem = added;
        }

        private void ScanOrStop()
        {
            if (_scanner != null) { _scanner.Cancel(); return; }
            if (_targets.SelectedItem is Target target) StartScan(target.Path);
        }

        internal async void StartScan(string path)
        {
            if (_scanner != null || IsDisposed) return;
            var scanner = _scanner = new DiskScanner();
            _tree.Reset();
            _files.Items.Clear();
            _types.Items.Clear();
            _status.Text = "";
            _scan.Text = L.T("disk.stop");
            _targets.Enabled = _choose.Enabled = false;
            _watch.Restart();
            _progressTimer.Start();
            Log.Write("INFO", "Disk space scan started");

            ScanResult result = await Task.Factory.StartNew(() => scanner.Run(path), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);

            _scanner = null;
            if (IsDisposed) return;
            _progressTimer.Stop();
            _scan.Text = L.T("disk.scan");
            _targets.Enabled = _choose.Enabled = true;
            Log.Write("INFO", $"Disk space scan {(result.Cancelled ? "stopped" : "finished")}: {result.Folders} folders, {result.Files} files, " +
                              $"{result.Unreadable} unreadable, {result.Elapsed.TotalSeconds:0.0} s");
            ShowResult(result);
        }

        private void ShowProgress()
        {
            DiskScanner s = _scanner;
            if (s == null) return;
            _progress.Text = L.T("disk.progress", s.Folders.ToString("N0", L.Culture), s.Files.ToString("N0", L.Culture),
                Fmt.Bytes(s.Bytes), ((int)_watch.Elapsed.TotalSeconds).ToString(L.Culture), Shorten(s.Current));
        }

        /// <summary>A long path with its middle cut out: the drive and the last folders say the most.</summary>
        internal static string Shorten(string path)
        {
            if (path.Length <= PathDisplayLength) return path;
            int head = PathDisplayLength / 3, tail = PathDisplayLength - head - 1;
            return path.Substring(0, head) + "…" + path.Substring(path.Length - tail);
        }

        private void ShowResult(ScanResult r)
        {
            _progress.Text = L.T(r.Cancelled ? "disk.stopped" : "disk.done", r.Elapsed.TotalSeconds.ToString("0.0", L.Culture));
            _status.Text = L.T("disk.summary", r.Root.Name, Fmt.Bytes(r.Root.Size), r.Files.ToString("N0", L.Culture),
                               r.Folders.ToString("N0", L.Culture))
                         + (r.Unreadable > 0 ? "  ·  " + L.T("disk.unreadable", r.Unreadable.ToString("N0", L.Culture)) : "");
            _status.ForeColor = r.Unreadable > 0 ? Palette.Warning : ForeColor;
            _tree.Show(r.Root);
            ShowFiles(r);
            ShowTypes(r);
        }
    }
}

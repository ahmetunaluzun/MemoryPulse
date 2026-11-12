using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MemoryPulse
{
    /// <summary>Main window: memory cards, usage bar, explanation, top programs, settings.</summary>
    internal sealed partial class MainForm : Form
    {
        private readonly TrayApp _app;
        private readonly Dictionary<string, Label> _values = new Dictionary<string, Label>();
        private readonly Dictionary<string, Label> _legend = new Dictionary<string, Label>();
        private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
        private readonly Timer _refresh = new Timer { Interval = 5000 };
        private Panel _bar;
        private ListView _list;
        private Label _status;
        private MemorySnapshot _last;


        public MainForm(TrayApp app)
        {
            _app = app;
            Text = "MemoryPulse";
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(700, 940);
            MinimumSize = new Size(600, 480);
            AutoScroll = true;
            Padding = new Padding(10);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);
            AddRow(root, BuildCards());
            AddRow(root, BuildBar(), SizeType.Absolute, 30);
            AddRow(root, BuildLegend());
            AddRow(root, BuildInfo());
            AddRow(root, BuildDisks());
            int listRow = root.RowCount;
            AddRow(root, BuildList(), SizeType.Percent, 100);
            AddRow(root, BuildButtons());
            AddRow(root, BuildSettings());
            AddRow(root, BuildFooter());
            KeepScrollable(root, listRow);

            _app.Updated += OnUpdated;
            _refresh.Tick += (s, e) => RefreshNow();
            VisibleChanged += (s, e) => { if (Visible) { RefreshNow(); _refresh.Start(); } else _refresh.Stop(); };
            Shown += (s, e) => FitColumns();
        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _app.Updated -= OnUpdated;
            _refresh.Dispose();
            _disk.Dispose();
            base.OnFormClosing(e);
        }

        private static void AddRow(TableLayoutPanel t, Control c, SizeType type = SizeType.AutoSize, float h = 0)
        {
            t.RowStyles.Add(type == SizeType.AutoSize ? new RowStyle(SizeType.AutoSize) : new RowStyle(type, h));
            t.Controls.Add(c, 0, t.RowCount++);
        }

        // ---------- building blocks ----------

        private Control BuildCards()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            for (int i = 0; i < 3; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            foreach (var key in new[] { "available", "cached", "free", "modified", "commit", "total" })
            {
                var card = new Panel { Dock = DockStyle.Fill, Height = 66, Margin = new Padding(4), BackColor = Palette.Card };
                var title = new Label { Text = L.T("card." + key), ForeColor = Palette.Muted, Dock = DockStyle.Top,
                                        Height = 24, Padding = new Padding(8, 5, 0, 0) };
                var value = new Label { Text = "—", Font = new Font("Segoe UI Semibold", 15f), Dock = DockStyle.Fill,
                                        Padding = new Padding(6, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
                card.Controls.Add(value);
                card.Controls.Add(title);
                foreach (Control c in new Control[] { card, title, value }) _tips.SetToolTip(c, L.Tip("tip." + key));
                _values[key] = value;
                t.Controls.Add(card);
            }
            return t;
        }

        private Control BuildBar()
        {
            _bar = new DoubleBufferedPanel { Dock = DockStyle.Fill, Margin = new Padding(4, 8, 4, 0), BackColor = Palette.Free };
            _bar.Paint += PaintBar;
            return _bar;
        }

        private Control BuildLegend()
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
            foreach (var (key, color) in new[] { ("inUse", Palette.InUse), ("modified", Palette.Modified),
                                                 ("standby", Palette.Standby), ("free", Palette.Free) })
            {
                f.Controls.Add(new Panel { Size = new Size(11, 11), BackColor = color, BorderStyle = BorderStyle.FixedSingle,
                                           Margin = new Padding(6, 7, 4, 0) });
                var l = new Label { AutoSize = true, ForeColor = Palette.Muted, Margin = new Padding(0, 4, 14, 0) };
                _tips.SetToolTip(l, L.Tip("tip." + (key == "inUse" ? "inUse" : key)));
                _legend[key] = l;
                f.Controls.Add(l);
            }
            return f;
        }

        private Control BuildInfo()
        {
            var p = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Palette.InfoBack,
                                Margin = new Padding(4, 6, 4, 4), Padding = new Padding(10, 8, 10, 8) };
            var text = new Label { Text = L.T("info.short"), AutoSize = true, Dock = DockStyle.Top };
            var more = new LinkLabel { Text = L.T("info.more"), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 4, 0, 0) };
            more.LinkClicked += (s, e) => MessageBox.Show(this, L.T("info.long"), L.T("info.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            p.Controls.Add(more);
            p.Controls.Add(text);
            p.Resize += (s, e) => text.MaximumSize = new Size(Math.Max(100, p.ClientSize.Width - p.Padding.Horizontal), 0);
            return p;
        }

        private Control BuildList()
        {
            var g = new GroupBox { Text = L.T("list.title"), Dock = DockStyle.Fill, Margin = new Padding(4), Padding = new Padding(8, 6, 8, 8),
                                   MinimumSize = new Size(0, ListMinHeight) };
            _list = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill,
                                   HeaderStyle = ColumnHeaderStyle.Nonclickable };
            int w = TextRenderer.MeasureText(L.T("list.count"), Font).Width + 30;
            _list.Columns.Add(L.T("list.program"), 200);
            _list.Columns.Add(L.T("list.count"), w, HorizontalAlignment.Right);
            _list.Columns.Add(L.T("list.ram"), Math.Max(w, 100), HorizontalAlignment.Right);
            _list.Columns.Add(L.T("list.io"), TextRenderer.MeasureText(L.T("list.io"), Font).Width + 30, HorizontalAlignment.Right);
            _list.Resize += (s, e) => FitColumns();
            _list.DoubleClick += (s, e) => OpenDetails(SelectedProgram);
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) OpenDetails(SelectedProgram); };
            var menu = new ContextMenuStrip();
            string menuTarget = null;
            menu.Items.Add(L.T("details.menu"), null, (s, e) => OpenDetails(menuTarget));
            menu.Items.Add(L.T("details.openFolder"), null, (s, e) => OpenProgramFolder(menuTarget));
            menu.Opening += (s, e) => { menuTarget = SelectedProgram; e.Cancel = menuTarget == null; };
            AddHistoryMenu(menu, () => menuTarget);
            _list.ContextMenuStrip = menu;
            _tips.SetToolTip(_list, L.T("list.hint"));
            g.Controls.Add(_list);
            return g;
        }

        private string SelectedProgram => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as string : null;

        private void OpenDetails(string name)
        {
            if (name == null) return;
            using (var f = new ProcessDetailsForm(name)) f.ShowDialog(this);
        }

        private static void OpenProgramFolder(string name)
        {
            if (name == null) return;
            var path = ProcessDetails.Read(name).Find(i => i.Path != null)?.Path;
            if (path != null && System.IO.File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        }

        private void FitColumns()
        {
            if (_list == null || _list.Columns.Count <= IoColumn) return;
            // Always reserve room for the vertical scrollbar so a horizontal one never appears.
            int inner = _list.Width - SystemInformation.Border3DSize.Width * 2 - SystemInformation.VerticalScrollBarWidth - 2;
            _list.Columns[0].Width = Math.Max(120, inner - _list.Columns[1].Width - _list.Columns[2].Width - _list.Columns[IoColumn].Width);
        }

        private Control BuildButtons()
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            void Add(string key, Action a, string tip = null)
            {
                var b = new Button { Text = L.T(key), AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(4) };
                b.Click += (s, e) => a();
                if (tip != null) _tips.SetToolTip(b, L.Tip(tip));
                f.Controls.Add(b);
            }
            if (Edition.CanClean) Add("btn.clean", () => { _app.CleanWithConfirm(this); RefreshNow(); }, "tip.clean");
            Add("btn.refresh", RefreshNow);
            Add("btn.history", _app.ShowHistory, "tip.historyBtn");
            Add("btn.disk", _app.ShowDiskSpace, "tip.diskBtn");
            Add("btn.taskmgr", Program.OpenTaskManager);
            Add("btn.log", () => { if (System.IO.File.Exists(AppPaths.LogFile)) Process.Start("notepad.exe", $"\"{AppPaths.LogFile}\""); });
            return f;
        }

        private Control BuildFooter()
        {
            var f = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(4, 6, 4, 0) };
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            f.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _status = new Label { AutoSize = true, ForeColor = Palette.Muted, Anchor = AnchorStyles.Left };
            // Two links: the project page, and a check for a newer version. The check asks GitHub in the
            // background only when clicked; MemoryPulse makes no other network connection.
            // The Store edition has no update check: the Store updates it.
            string github = "GitHub", updates = L.T("update.check");
            string text = $"MemoryPulse {Program.Version} · {github}" + (Edition.CanCheckUpdates ? $" · {updates}" : "");
            var about = new LinkLabel { Text = text, AutoSize = true, Anchor = AnchorStyles.Right };
            about.Links.Clear();
            about.Links.Add(about.Text.IndexOf(github, StringComparison.Ordinal), github.Length, Program.RepoUrl);
            if (Edition.CanCheckUpdates)
                about.Links.Add(about.Text.LastIndexOf(updates, StringComparison.Ordinal), updates.Length, null);
            about.LinkClicked += async (s, e) =>
            {
                if (e.Link.LinkData is string url) { Process.Start(url); return; }
                UseWaitCursor = true;
                try { await _app.CheckForUpdatesAsync(this); }
                finally { UseWaitCursor = false; }
            };
            f.Controls.Add(_status, 0, 0);
            f.Controls.Add(about, 1, 0);
            return f;
        }

        // ---------- data ----------

        private void OnUpdated(MemorySnapshot s)
        {
            if (Visible) Show(s);
        }

        private void RefreshNow()
        {
            if (IsDisposed) return;
            try { Show(MemoryInfo.Read()); }
            catch (System.ComponentModel.Win32Exception ex) { Log.Write("ERROR", "Window refresh failed: " + ex.Message); }
            SampleDisk();
        }

        private void Show(MemorySnapshot s)
        {
            _last = s;
            double avail = MemorySnapshot.Gb(s.Available);
            _values["available"].Text = L.Gb(avail);
            _values["available"].ForeColor = Palette.For(_app.LevelOf(avail));
            string na = L.T("value.na");
            _values["cached"].Text = s.HasDetail ? L.Gb(MemorySnapshot.Gb(s.Cached)) : na;
            _values["free"].Text = s.HasDetail ? L.Gb(MemorySnapshot.Gb(s.Free)) : na;
            _values["modified"].Text = s.HasDetail ? L.Gb(MemorySnapshot.Gb(s.Modified)) : na;
            _values["commit"].Text = $"{MemorySnapshot.Gb(s.CommitUsed).ToString("0.0", L.Culture)} / {L.Gb(MemorySnapshot.Gb(s.CommitLimit))}";
            _values["total"].Text = L.Gb(MemorySnapshot.Gb(s.Total));
            _legend["inUse"].Text = L.T("legend.inUse", L.Gb(MemorySnapshot.Gb(s.InUse)));
            _legend["modified"].Text = L.T("legend.modified", L.Gb(MemorySnapshot.Gb(s.Modified)));
            _legend["standby"].Text = L.T("legend.standby", L.Gb(MemorySnapshot.Gb(s.Standby)));
            _legend["free"].Text = L.T("legend.free", L.Gb(MemorySnapshot.Gb(s.Free)));
            _bar.Invalidate();

            string keep = SelectedProgram;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var p in MemoryInfo.TopProcesses(25))
            {
                var item = new ListViewItem(p.Name) { Tag = p.Name };
                item.SubItems.Add(p.Count.ToString(L.Culture));
                item.SubItems.Add(MemorySnapshot.Gb(p.WorkingSet).ToString("0.00", L.Culture) + " GB");
                item.SubItems.Add(IoText(p.Name));
                _list.Items.Add(item);
                if (string.Equals(p.Name, keep, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
            }
            _list.EndUpdate();
            FitColumns();
            _status.Text = L.T("status.updated", DateTime.Now.ToString("HH:mm:ss", L.Culture)) +
                           (Native.IsElevated && Edition.CanClean ? "  ·  " + L.T("status.admin") : "");
        }

        private void PaintBar(object sender, PaintEventArgs e)
        {
            var s = _last;
            var r = _bar.ClientRectangle;
            e.Graphics.Clear(Palette.Free);
            if (s == null || !s.HasDetail || s.Total == 0) return;
            var parts = new[] { (s.InUse, Palette.InUse), (s.Modified, Palette.Modified), (s.Standby, Palette.Standby), (s.Free, Palette.Free) };
            double sum = parts.Sum(p => (double)p.Item1);
            float x = 0;
            foreach (var (bytes, color) in parts)
            {
                float w = (float)(r.Width * (double)bytes / sum);
                using (var b = new SolidBrush(color)) e.Graphics.FillRectangle(b, x, 0, w + 1, r.Height);
                x += w;
            }
        }

        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        }
    }
}

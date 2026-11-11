using System;
using System.IO;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemoryPulse
{
    /// <summary>Usage history: charts, per-program totals and crashes/restarts for a chosen time range.</summary>
    internal sealed partial class HistoryForm : Form
    {
        private static readonly int[] RangeHours = { 1, 6, 24, 24 * HistoryConfig.KeepDays };
        private const int DefaultRange = 2;          // last 24 hours
        private const int ChartMinHeight = 120;
        private const string PickerFormat = "dd.MM.yyyy HH:mm";
        private const double WidestAxisValue = 1000 * 1024 * 1024; // "1000 MB/s": the widest label a chart axis shows

        private readonly Settings _settings;
        private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
        private readonly Timer _auto = new Timer { Interval = HistoryConfig.SampleSeconds * 1000 };
        private ComboBox _range;
        private DateTimePicker _from, _to;
        private LineChart _ram, _disk, _net;
        private Label _empty, _summary;
        private ListView _programs, _events;
        private bool _loading, _reloadPending;

        public HistoryForm(Settings settings)
        {
            _settings = settings;
            Text = L.T("history.title");
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(920, 660);
            MinimumSize = new Size(640, 480);
            Padding = new Padding(10);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            root.Controls.Add(BuildRangeBar(), 0, 0);

            var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(4) };
            tabs.TabPages.Add(BuildChartsTab());
            tabs.TabPages.Add(BuildProgramsTab());
            tabs.TabPages.Add(BuildEventsTab());
            root.Controls.Add(tabs, 0, 1);
            root.Controls.Add(new Label { Text = L.T("history.footer", HistoryConfig.SampleSeconds, HistoryConfig.KeepDays), AutoSize = true,
                                          ForeColor = Palette.Muted, Margin = new Padding(4, 4, 4, 0) }, 0, 2);

            _auto.Tick += (s, e) => { if (_range.SelectedIndex < RangeHours.Length) Reload(); };
            Shown += (s, e) => { Reload(); _auto.Start(); };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _auto.Dispose();
            _tips.Dispose();
            base.OnFormClosed(e);
        }

        private Control BuildRangeBar()
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            Label Caption(string key) => new Label { Text = L.T(key), AutoSize = true, Margin = new Padding(4, 8, 4, 0) };
            DateTimePicker Picker(DateTime value) => new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = PickerFormat,
                                                                          Value = value, Width = 150, Enabled = false, Margin = new Padding(4) };
            _range = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(4) };
            foreach (string key in new[] { "history.range.1h", "history.range.6h", "history.range.24h", "history.range.7d", "history.range.custom" })
                _range.Items.Add(L.T(key));
            _range.SelectedIndex = DefaultRange;
            _from = Picker(DateTime.Now.AddHours(-RangeHours[DefaultRange]));
            _to = Picker(DateTime.Now);
            _range.SelectedIndexChanged += (s, e) =>
            {
                _from.Enabled = _to.Enabled = _range.SelectedIndex == RangeHours.Length;
                if (!_from.Enabled) Reload();
            };
            var refresh = new Button { Text = L.T("btn.refresh"), AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(8, 3, 4, 3) };
            refresh.Click += (s, e) => Reload();

            f.Controls.Add(Caption("history.range"));
            f.Controls.Add(_range);
            f.Controls.Add(Caption("history.from"));
            f.Controls.Add(_from);
            f.Controls.Add(Caption("history.to"));
            f.Controls.Add(_to);
            f.Controls.Add(refresh);
            return f;
        }

        private TabPage BuildChartsTab()
        {
            var page = new TabPage(L.T("history.tab.charts")) { Padding = new Padding(6), AutoScroll = true, BackColor = Color.White };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, MinimumSize = new Size(0, ChartMinHeight * 3) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _empty = new Label { AutoSize = true, Dock = DockStyle.Top, BackColor = Palette.InfoBack, Padding = new Padding(10, 8, 10, 8),
                                 Margin = new Padding(0, 0, 0, 6), Visible = false };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(_empty, 0, 0);
            LineChart Chart(string title, Func<double, string> format, int row)
            {
                var c = new LineChart { Title = L.T(title), FormatY = format, AxisSample = Fmt.Rate(WidestAxisValue),
                                        Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
                t.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
                t.Controls.Add(c, 0, row);
                return c;
            }
            _ram = Chart("history.chart.ram", v => Fmt.Bytes(v, "0.##"), 1);
            _disk = Chart("history.chart.disk", v => Fmt.Rate(v, "0.##"), 2);
            _net = Chart("history.chart.net", v => Fmt.Rate(v, "0.##"), 3);
            _summary = new Label { AutoSize = true, Dock = DockStyle.Top, ForeColor = Palette.Muted, Margin = new Padding(0, 4, 0, 0) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(_summary, 0, 4);
            t.Resize += (s, e) => _summary.MaximumSize = _empty.MaximumSize = new Size(Math.Max(100, t.ClientSize.Width), 0);
            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildProgramsTab()
        {
            var page = new TabPage(L.T("history.tab.programs")) { Padding = new Padding(6) };
            _programs = NewList();
            _programs.Columns.Add(L.T("list.program"), MinFillWidth);
            foreach (string key in new[] { "history.col.avgRam", "history.col.peakRam" }) AddColumn(_programs, key, HorizontalAlignment.Right);
            AddColumn(_programs, "history.col.peakTime", HorizontalAlignment.Left, DateTime.Now.ToString("g", L.Culture));
            foreach (string key in new[] { "history.col.read", "history.col.write", "history.col.netRx", "history.col.netTx" })
                AddColumn(_programs, key, HorizontalAlignment.Right);
            FillColumn(_programs, 0);
            SortableList.Attach(_programs, 1, false); // biggest average RAM first
            var note = new Label { Text = L.T("history.ioNote"), AutoSize = true, Dock = DockStyle.Top, ForeColor = Palette.Muted,
                                   Padding = new Padding(0, 0, 0, 6) };
            _tips.SetToolTip(note, L.Tip("history.ioTip"));
            _tips.SetToolTip(_programs, L.Tip("history.ioTip"));
            page.Resize += (s, e) => note.MaximumSize = new Size(Math.Max(100, page.ClientSize.Width - page.Padding.Horizontal), 0);
            page.Controls.Add(_programs);
            page.Controls.Add(note);
            return page;
        }

        private TabPage BuildEventsTab()
        {
            var page = new TabPage(L.T("history.tab.events")) { Padding = new Padding(6) };
            _events = NewList();
            AddEventColumns(_events);
            SortableList.Attach(_events, 0, false); // newest first
            var note = new Label { Text = L.T("history.eventsNote"), AutoSize = true, Dock = DockStyle.Top, ForeColor = Palette.Muted,
                                   Padding = new Padding(0, 0, 0, 6) };
            page.Resize += (s, e) => note.MaximumSize = new Size(Math.Max(100, page.ClientSize.Width - page.Padding.Horizontal), 0);
            page.Controls.Add(_events);
            page.Controls.Add(note);
            return page;
        }

        private static ListView NewList() =>
            new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true };

        private const int MinFillWidth = 140, HeaderPadding = 18;

        // Columns are added before the list gets its parent's font, so measure with the font the windows use.
        private static readonly Font ListFont = new Font("Segoe UI", 9.75f);

        /// <summary>A column wide enough for its header and for a typical value.</summary>
        private static void AddColumn(ListView list, string key, HorizontalAlignment align, string sample = "999,9 MB")
        {
            int width = Math.Max(TextRenderer.MeasureText(L.T(key), ListFont).Width, TextRenderer.MeasureText(sample, ListFont).Width);
            list.Columns.Add(L.T(key), width + HeaderPadding, align);
        }

        /// <summary>Time, event, description: the columns of every crash/restart list.</summary>
        internal static void AddEventColumns(ListView list)
        {
            AddColumn(list, "history.col.time", HorizontalAlignment.Left, DateTime.Now.ToString("g", L.Culture));
            AddColumn(list, "history.col.kind", HorizontalAlignment.Left, L.T("event.kind.ShutdownRequested"));
            list.Columns.Add(L.T("history.col.what"), MinFillWidth);
            FillColumn(list, 2);
        }

        /// <summary>Lets one column take the width the others leave, so no horizontal scrollbar appears while there is room.</summary>
        private static void FillColumn(ListView list, int column)
        {
            void Fit()
            {
                int others = 0;
                for (int i = 0; i < list.Columns.Count; i++) if (i != column) others += list.Columns[i].Width;
                int room = list.Width - SystemInformation.Border3DSize.Width * 2 - SystemInformation.VerticalScrollBarWidth - 2;
                list.Columns[column].Width = Math.Max(MinFillWidth, room - others);
            }
            list.Resize += (s, e) => Fit();
            list.HandleCreated += (s, e) => Fit();
        }

        private void GetRange(out DateTime from, out DateTime to)
        {
            if (_range.SelectedIndex < RangeHours.Length)
            {
                to = DateTime.Now;
                from = to.AddHours(-RangeHours[_range.SelectedIndex]);
                _from.Value = from;
                _to.Value = to;
                return;
            }
            from = _from.Value;
            to = _to.Value;
        }

        private async void Reload()
        {
            if (IsDisposed) return;
            if (_loading) { _reloadPending = true; return; }
            GetRange(out DateTime from, out DateTime to);
            if (to <= from)
            {
                MessageBox.Show(this, L.T("history.badRange"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _loading = true;
            UseWaitCursor = true;
            try
            {
                var data = await Task.Run(() => HistoryQuery.Load(from, to, null));
                var events = await Task.Run(() => SystemEvents.Read(from, to));
                if (IsDisposed) return;
                ShowCharts(data, events, from, to);
                ShowPrograms(data);
                ShowEvents(events);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Write("ERROR", "History could not be read: " + ex.Message);
                _empty.Text = L.T("history.loadFailed", ex.Message);
                _empty.Visible = true;
            }
            finally
            {
                _loading = false;
                UseWaitCursor = false;
            }
            if (_reloadPending && !IsDisposed) { _reloadPending = false; Reload(); }
        }
    }
}

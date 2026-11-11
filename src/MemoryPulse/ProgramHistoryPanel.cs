using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemoryPulse
{
    /// <summary>One program's last 24 hours: RAM chart, read/write and network totals, its crashes and hangs.</summary>
    internal sealed class ProgramHistoryPanel : UserControl
    {
        private const int Hours = 24;
        private const int GapSamples = 3;

        private readonly string _name;
        private readonly Label _totals;
        private readonly LineChart _chart;
        private readonly ListView _events;
        private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
        private bool _loaded;

        public ProgramHistoryPanel(string name)
        {
            _name = name;
            Dock = DockStyle.Fill;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            Controls.Add(root);

            _totals = new Label { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(4, 4, 4, 6) };
            _tips.SetToolTip(_totals, L.Tip("history.ioTip"));
            root.Controls.Add(_totals, 0, 0);
            _chart = new LineChart { Title = L.T("history.chart.programRam"), FormatY = v => Fmt.Bytes(v, "0.##"), Dock = DockStyle.Fill,
                                     Margin = new Padding(4), MinimumSize = new Size(0, 100) };
            root.Controls.Add(_chart, 0, 1);
            root.Controls.Add(new Label { Text = L.T("history.programEvents"), AutoSize = true, ForeColor = Palette.Muted,
                                          Margin = new Padding(4, 6, 4, 2) }, 0, 2);
            _events = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, Margin = new Padding(4),
                                     ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            HistoryForm.AddEventColumns(_events);
            root.Controls.Add(_events, 0, 3);
            root.Resize += (s, e) => _totals.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - 8), 0);
        }

        /// <summary>Reads the files and the event log in the background, the first time the tab is shown.</summary>
        public async void LoadOnce()
        {
            if (_loaded) return;
            _loaded = true;
            DateTime to = DateTime.Now, from = to.AddHours(-Hours);
            _totals.Text = L.T("history.loading");
            HistoryData data;
            List<SystemEvent> events;
            try
            {
                data = await Task.Run(() => HistoryQuery.Load(from, to, _name));
                events = await Task.Run(() => SystemEvents.Read(from, to, _name));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Write("ERROR", "Program history could not be read: " + ex.Message);
                if (!IsDisposed) _totals.Text = L.T("history.loadFailed", ex.Message);
                return;
            }
            if (IsDisposed) return;

            data.Programs.TryGetValue(_name, out ProgramStat p);
            _totals.Text = p == null || p.Samples == 0
                ? L.T("history.programEmpty")
                : L.T("history.programTotals", Fmt.Bytes(p.AverageWorkingSet), Fmt.Bytes(p.PeakWorkingSet), p.PeakTime.ToString("g", L.Culture),
                      Fmt.Bytes(p.Read), Fmt.Bytes(p.Write))
                  + "\n" + (p.HasNet ? L.T("history.programNet", Fmt.Bytes(p.NetRx), Fmt.Bytes(p.NetTx)) : L.T("history.programNoNet"));
            _chart.SetData(from, to, TimeSpan.FromSeconds(HistoryConfig.SampleSeconds * GapSamples), HistoryForm.Markers(events),
                new ChartSeries { Name = L.T("list.ram"), Color = Palette.InUse, Times = data.ProgramTimes.ToArray(), Values = data.ProgramRam.ToArray() });
            _events.Items.Clear();
            if (events.Count == 0) _events.Items.Add(new ListViewItem(new[] { "", "", L.T("history.programNoEvents") }) { ForeColor = Palette.Muted });
            else _events.Items.AddRange(events.Select(HistoryForm.EventItem).ToArray());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips.Dispose();
            base.Dispose(disposing);
        }
    }
}

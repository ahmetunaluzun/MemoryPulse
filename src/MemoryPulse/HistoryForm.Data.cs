using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal sealed partial class HistoryForm
    {
        private const int GapBuckets = 3; // a hole of three missing records is drawn as a hole

        private void ShowCharts(HistoryData d, List<SystemEvent> events, DateTime from, DateTime to)
        {
            _empty.Visible = d.Empty;
            if (d.Empty) _empty.Text = L.T(_settings.HistoryEnabled ? "history.empty" : "history.off");
            var times = d.System.Select(p => p.Time).ToArray();
            var gap = TimeSpan.FromSeconds(d.BucketSeconds * GapBuckets);
            var markers = Markers(events);
            ChartSeries S(string key, System.Drawing.Color color, Func<SysPoint, double> value) =>
                new ChartSeries { Name = L.T(key), Color = color, Times = times, Values = d.System.Select(value).ToArray() };
            _ram.SetData(from, to, gap, markers, S("card.available", Palette.Normal, p => p.Available), S("card.cached", Palette.Standby, p => p.Cached));
            _disk.SetData(from, to, gap, markers, S("history.read", Palette.InUse, p => p.DiskRead), S("history.write", Palette.Modified, p => p.DiskWrite));
            _net.SetData(from, to, gap, markers, S("history.netRx", Palette.InUse, p => p.NetRx), S("history.netTx", Palette.Modified, p => p.NetTx));
            _summary.Text = d.Empty ? "" : Summary(d);
        }

        internal static List<ChartMarker> Markers(IEnumerable<SystemEvent> events) =>
            events.Select(e => new ChartMarker
            {
                Time = e.Time,
                Color = e.IsProblem ? Palette.Critical : Palette.Muted,
                Text = e.Time.ToString("g", L.Culture) + "  " + e.KindText + "\n" + e.Description
            }).ToList();

        private static string Summary(HistoryData d)
        {
            var sb = new StringBuilder();
            sb.Append(L.T("history.summary", Fmt.Bytes(d.MinAvailable), d.MinAvailableTime.ToString("g", L.Culture),
                Fmt.Bytes(d.DiskRead), Fmt.Bytes(d.DiskWrite), Fmt.Bytes(d.NetRx), Fmt.Bytes(d.NetTx)));
            foreach (var drive in d.Drives.Values)
            {
                long change = drive.LastFree - drive.FirstFree;
                sb.Append('\n').Append(L.T("history.drive", drive.Name, Fmt.Bytes(drive.LastFree), Fmt.Bytes(drive.Total),
                    (change >= 0 ? "+" : "−") + Fmt.Bytes(Math.Abs(change))));
            }
            return sb.ToString();
        }

        private void ShowPrograms(HistoryData d)
        {
            string none = L.T("value.na");
            _programs.BeginUpdate();
            _programs.Items.Clear();
            foreach (var p in d.Programs.Values)
            {
                var item = new ListViewItem(p.Name)
                {
                    Tag = new IComparable[] { p.Name, p.AverageWorkingSet, p.PeakWorkingSet, p.PeakTime, p.Read, p.Write,
                                              p.HasNet ? p.NetRx : -1L, p.HasNet ? p.NetTx : -1L }
                };
                item.SubItems.Add(Fmt.Bytes(p.AverageWorkingSet));
                item.SubItems.Add(Fmt.Bytes(p.PeakWorkingSet));
                item.SubItems.Add(p.PeakTime.ToString("g", L.Culture));
                item.SubItems.Add(Fmt.Bytes(p.Read));
                item.SubItems.Add(Fmt.Bytes(p.Write));
                item.SubItems.Add(p.HasNet ? Fmt.Bytes(p.NetRx) : none);
                item.SubItems.Add(p.HasNet ? Fmt.Bytes(p.NetTx) : none);
                _programs.Items.Add(item);
            }
            _programs.EndUpdate();
        }

        private void ShowEvents(List<SystemEvent> events)
        {
            _events.BeginUpdate();
            _events.Items.Clear();
            foreach (var e in events) _events.Items.Add(EventItem(e));
            _events.EndUpdate();
        }

        internal static ListViewItem EventItem(SystemEvent e)
        {
            var item = new ListViewItem(e.Time.ToString("g", L.Culture))
            {
                Tag = new IComparable[] { e.Time, e.KindText, e.Description },
                ForeColor = e.IsProblem ? Palette.Critical : System.Drawing.SystemColors.WindowText,
                ToolTipText = e.Description
            };
            item.SubItems.Add(e.KindText);
            item.SubItems.Add(e.Description);
            return item;
        }
    }

    /// <summary>Click a column header to sort a list; each row carries its sort values in Tag (IComparable[]).</summary>
    internal sealed class SortableList : IComparer
    {
        private int _column;
        private bool _ascending;

        private SortableList(int column, bool ascending) { _column = column; _ascending = ascending; }

        public static void Attach(ListView list, int column, bool ascending)
        {
            var sorter = new SortableList(column, ascending);
            list.ListViewItemSorter = sorter;
            list.ColumnClick += (s, e) =>
            {
                // First click on a text column sorts A→Z, on a number or time column biggest/newest first.
                if (e.Column == sorter._column) sorter._ascending = !sorter._ascending;
                else { sorter._column = e.Column; sorter._ascending = list.Columns[e.Column].TextAlign == HorizontalAlignment.Left && e.Column == 0; }
                list.Sort();
            };
        }

        public int Compare(object x, object y)
        {
            var a = ((ListViewItem)x).Tag as IComparable[];
            var b = ((ListViewItem)y).Tag as IComparable[];
            if (a == null || b == null || _column >= a.Length || _column >= b.Length) return 0;
            int result = a[_column] is string text ? string.Compare(text, (string)b[_column], true, L.Culture) : a[_column].CompareTo(b[_column]);
            return _ascending ? result : -result;
        }
    }
}

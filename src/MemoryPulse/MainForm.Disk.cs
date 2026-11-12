using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal sealed partial class MainForm
    {
        private const int IoColumn = 3;
        private const int ListMinHeight = 140;
        private const int FollowUpMs = 1500; // the first reading has nothing to compare with: take a second one soon
        private const double BytesPerMb = 1024 * 1024;

        private readonly DiskMonitor _disk = new DiskMonitor();
        private DriveBars _drives;
        private GroupBox _activity; // its caption carries the live activity, to keep the window short
        private Dictionary<string, double> _ioRates; // null until the second reading
        private bool _diskBusy;

        /// <summary>The "Disks" box: one fullness bar per drive and a line of live activity.</summary>
        private Control BuildDisks()
        {
            _activity = new GroupBox { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                       Margin = new Padding(4), Padding = new Padding(8, 2, 8, 2) };
            _drives = new DriveBars { Dock = DockStyle.Top };
            _drives.Set(DiskMonitor.ReadDrives());
            _tips.SetToolTip(_activity, L.Tip("disk.activityTip"));
            _tips.SetToolTip(_drives, L.Tip("disk.activityTip"));
            ShowActivity(null);
            _activity.Controls.Add(_drives);
            return _activity;
        }

        // Counters and the process list are read in the background: Windows can take seconds to load its counters.
        private async void SampleDisk()
        {
            if (_diskBusy) return;
            _diskBusy = true;
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    DiskReading r = await Task.Run(() => _disk.Sample());
                    if (r == null || IsDisposed) return;
                    ApplyDisk(r);
                    if (r.HasRates) return;
                    await Task.Delay(FollowUpMs);
                    if (IsDisposed || !Visible) return;
                }
            }
            catch (Win32Exception ex) { Log.Write("ERROR", "Disk refresh failed: " + ex.Message); }
            finally { _diskBusy = false; }
        }

        private void ApplyDisk(DiskReading r)
        {
            _drives.Set(r.Drives);
            ShowActivity(r);
            _ioRates = r.ProgramRates;
            foreach (ListViewItem item in _list.Items) item.SubItems[IoColumn].Text = IoText(item.Tag as string);
        }

        private void ShowActivity(DiskReading r)
        {
            string na = L.T("value.na");
            bool has = r != null && r.HasRates;
            _activity.Text = L.T("disk.activity",
                has && r.ReadRate >= 0 ? MbPerSec(r.ReadRate) : na,
                has && r.WriteRate >= 0 ? MbPerSec(r.WriteRate) : na,
                has && r.ActivePercent >= 0 ? L.T("disk.percent", r.ActivePercent.ToString("0", L.Culture)) : na);
        }

        private string IoText(string program) =>
            program != null && _ioRates != null && _ioRates.TryGetValue(program, out double rate) ? MbPerSec(rate) : L.T("value.na");

        private static string MbPerSec(double bytesPerSecond) =>
            (bytesPerSecond / BytesPerMb).ToString("0.0", L.Culture) + " MB" + L.T("unit.perSec");

        // On a small screen the window is shorter than its content: keep the list usable and let the window scroll.
        private void KeepScrollable(TableLayoutPanel root, int listRow)
        {
            root.Layout += (s, e) =>
            {
                int[] rows = root.GetRowHeights();
                if (rows.Length <= listRow) return;
                int needed = rows.Sum() - rows[listRow] + ListMinHeight + Padding.Vertical;
                if (AutoScrollMinSize.Height == needed || !IsHandleCreated) return;
                // Changing it here, in the middle of a layout, would not lay the window out again.
                BeginInvoke((Action)(() => { if (!IsDisposed) AutoScrollMinSize = new Size(0, needed); }));
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height) { Height = area.Height; Top = area.Top; }
        }

        /// <summary>One row per drive: name, a bar of the used share, free and total size.</summary>
        private sealed class DriveBars : Control
        {
            private const int Gap = 10;
            private List<DriveUsage> _list = new List<DriveUsage>();

            public DriveBars() { DoubleBuffered = true; ResizeRedraw = true; }

            private int RowHeight => Font.Height + 5;

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                Set(_list); // the row height follows the font the window gives
            }

            public void Set(List<DriveUsage> list)
            {
                _list = list;
                int height = Math.Max(1, list.Count) * RowHeight;
                if (Height != height) Height = height;
                Invalidate();
            }

            private static string Describe(DriveUsage d) =>
                L.T("disk.driveText", Fmt.Bytes(d.Free), Fmt.Bytes(d.Total), (d.UsedFraction * 100).ToString("0", L.Culture));

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                if (_list.Count == 0)
                {
                    TextRenderer.DrawText(e.Graphics, L.T("value.na"), Font, ClientRectangle, Palette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    return;
                }
                int titleWidth = _list.Max(d => TextRenderer.MeasureText(d.Title, Font).Width);
                int textWidth = _list.Max(d => TextRenderer.MeasureText(Describe(d), Font).Width);
                titleWidth = Math.Min(titleWidth, Width / 3);
                int barWidth = Math.Max(30, Width - titleWidth - textWidth - Gap * 2);
                for (int i = 0; i < _list.Count; i++)
                {
                    DriveUsage d = _list[i];
                    int y = i * RowHeight;
                    Color state = d.Level == Level.Normal ? ForeColor : Palette.For(d.Level);
                    TextRenderer.DrawText(e.Graphics, d.Title, Font, new Rectangle(0, y, titleWidth, RowHeight), ForeColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    var bar = new Rectangle(titleWidth + Gap, y + 4, barWidth, RowHeight - 8);
                    using (var back = new SolidBrush(Palette.Free)) e.Graphics.FillRectangle(back, bar);
                    using (var used = new SolidBrush(d.Level == Level.Normal ? Palette.InUse : Palette.For(d.Level)))
                        e.Graphics.FillRectangle(used, bar.X, bar.Y, (int)(bar.Width * d.UsedFraction), bar.Height);
                    TextRenderer.DrawText(e.Graphics, Describe(d), Font, new Rectangle(bar.Right + Gap, y, textWidth, RowHeight), state,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}

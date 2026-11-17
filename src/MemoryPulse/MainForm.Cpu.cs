using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal sealed partial class MainForm
    {
        private const int CpuColumn = 3;
        private const double CpuBusyPercent = 90; // above this the CPU bar is drawn as a warning
        private const int ListByRam = 25;            // the list shows this many programs by RAM ...
        private const int ListByCpu = 5;             // ... plus up to this many of the busiest by CPU
        private const double ListCpuMinPercent = 1;  // a program below this is not added for its CPU

        private CpuMeter _cpu;
        private Dictionary<string, double> _cpuRates; // null until the second reading

        /// <summary>One line above the memory cards: CPU usage of the whole PC as a number and a bar.</summary>
        private Control BuildCpu()
        {
            _cpu = new CpuMeter { Dock = DockStyle.Top, Margin = new Padding(4, 0, 4, 4) };
            _tips.SetToolTip(_cpu, L.Tip("tip.cpu"));
            return _cpu;
        }

        /// <summary>Called with every disk reading: both come from the same background sample.</summary>
        private void ApplyCpu(DiskReading r)
        {
            _cpu.Set(r.HasRates ? r.CpuPercent : -1);
            _cpuRates = r.ProgramCpu;
            foreach (ListViewItem item in _list.Items) item.SubItems[CpuColumn].Text = CpuText(item.Tag as string);
        }

        /// <summary>
        /// Programs busy enough on the CPU to be listed even when their RAM would not get them in:
        /// the <see cref="ListByCpu"/> busiest ones at or above <see cref="ListCpuMinPercent"/>.
        /// </summary>
        private HashSet<string> BusiestByCpu()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_cpuRates == null) return names;
            foreach (var kv in _cpuRates.Where(kv => kv.Value >= ListCpuMinPercent).OrderByDescending(kv => kv.Value).Take(ListByCpu))
                names.Add(kv.Key);
            return names;
        }

        private string CpuText(string program) =>
            program != null && _cpuRates != null && _cpuRates.TryGetValue(program, out double percent) ? Fmt.Percent(percent) : L.T("value.na");

        /// <summary>"CPU", a bar of the busy share and the percentage, on one row.</summary>
        private sealed class CpuMeter : Control
        {
            private const int Gap = 10;
            private double _percent = -1;

            public CpuMeter()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                Height = Font.Height + 10;
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                Height = Font.Height + 10; // the row follows the font the window gives
            }

            public void Set(double percent)
            {
                _percent = percent;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                string title = L.T("cpu.title");
                string value = _percent < 0 ? L.T("value.na") : L.T("disk.percent", _percent.ToString("0", L.Culture));
                int titleWidth = TextRenderer.MeasureText(title, Font).Width;
                int valueWidth = TextRenderer.MeasureText(L.T("disk.percent", "100"), Font).Width;
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(e.Graphics, title, Font, new Rectangle(0, 0, titleWidth, Height), ForeColor, flags | TextFormatFlags.Left);
                var bar = new Rectangle(titleWidth + Gap, 5, Math.Max(30, Width - titleWidth - valueWidth - Gap * 2), Height - 10);
                using (var back = new SolidBrush(Palette.Free)) e.Graphics.FillRectangle(back, bar);
                bool busy = _percent >= CpuBusyPercent;
                if (_percent > 0)
                    using (var used = new SolidBrush(busy ? Palette.Warning : Palette.InUse))
                        e.Graphics.FillRectangle(used, bar.X, bar.Y, (int)(bar.Width * Math.Min(100, _percent) / 100), bar.Height);
                TextRenderer.DrawText(e.Graphics, value, Font, new Rectangle(bar.Right + Gap, 0, valueWidth, Height),
                                      busy ? Palette.Warning : ForeColor, flags | TextFormatFlags.Right);
            }
        }
    }
}

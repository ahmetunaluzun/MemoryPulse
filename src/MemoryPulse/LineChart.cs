using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal sealed class ChartSeries
    {
        public string Name;
        public Color Color;
        public DateTime[] Times;
        public double[] Values; // NaN = no value at that time
    }

    /// <summary>A moment marked on the chart with a vertical line (a crash, a restart).</summary>
    internal sealed class ChartMarker
    {
        public DateTime Time;
        public Color Color;
        public string Text;
    }

    /// <summary>A small time chart drawn by hand: one or two lines, a value axis, time labels and event markers.</summary>
    internal sealed class LineChart : Control
    {
        private const int Divisions = 4;      // horizontal grid lines
        private const int TimeLabels = 5;
        private const int HitPixels = 4;      // how close the mouse must be to a marker
        private const int Pad = 8;
        private const int LegendBox = 9;
        private const float LineWidth = 1.6f;
        private static readonly double[] NiceSteps = { 1, 2, 4, 8, 10, 20, 40, 80, 100, 200, 400, 800, 1024 };
        private static readonly Color Grid = Color.FromArgb(228, 231, 236);

        private readonly ToolTip _tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 100 };
        private ChartSeries[] _series = new ChartSeries[0];
        private IList<ChartMarker> _markers = new ChartMarker[0];
        private DateTime _from, _to;
        private TimeSpan _gap;
        private Rectangle _plot;
        private ChartMarker _hover;
        private bool _hasValues;

        public string Title = "";
        public Func<double, string> FormatY = v => v.ToString("0.##");
        /// <summary>The widest value label to make room for, so charts stacked on top of each other line up.</summary>
        public string AxisSample = "";

        public LineChart()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
        }

        /// <param name="gap">Two points further apart than this are not joined (nothing was recorded in between).</param>
        public void SetData(DateTime from, DateTime to, TimeSpan gap, IList<ChartMarker> markers, params ChartSeries[] series)
        {
            _from = from;
            _to = to;
            _gap = gap;
            _markers = markers ?? new ChartMarker[0];
            _series = series;
            Invalidate();
        }

        private float X(DateTime t) => _plot.Left + (float)((t - _from).TotalSeconds / Math.Max(1, (_to - _from).TotalSeconds) * _plot.Width);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var values = _series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v)).ToList();
            _hasValues = values.Count > 0; // an empty chart shows its grid without made-up numbers
            double max = NiceMax(_hasValues ? values.Max() : 0);
            int line = Font.Height;
            int left = Pad + Enumerable.Range(0, Divisions + 1).Select(i => FormatY(max * i / Divisions)).Concat(new[] { AxisSample })
                                       .Max(text => TextRenderer.MeasureText(g, text, Font).Width);
            _plot = new Rectangle(left, line + Pad, Width - left - Pad * 2, Height - line * 2 - Pad * 2);
            if (_plot.Width < 20 || _plot.Height < 20 || _to <= _from) return;

            DrawHeader(g);
            DrawGrid(g, max);
            g.SetClip(_plot);
            foreach (var m in _markers)
                using (var pen = new Pen(m.Color) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, X(m.Time), _plot.Top, X(m.Time), _plot.Bottom);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var s in _series) DrawSeries(g, s, max);
            g.ResetClip();
        }

        private void DrawHeader(Graphics g)
        {
            TextRenderer.DrawText(g, Title, Font, new Point(_plot.Left, Pad / 2), ForeColor);
            int x = _plot.Right;
            foreach (var s in _series.Reverse())
            {
                x -= TextRenderer.MeasureText(g, s.Name, Font).Width;
                TextRenderer.DrawText(g, s.Name, Font, new Point(x, Pad / 2), Palette.Muted);
                x -= LegendBox + 4;
                using (var b = new SolidBrush(s.Color)) g.FillRectangle(b, x, Pad / 2 + (Font.Height - LegendBox) / 2, LegendBox, LegendBox);
                x -= Pad * 2;
            }
        }

        private void DrawGrid(Graphics g, double max)
        {
            using (var pen = new Pen(Grid))
            {
                for (int i = 0; i <= Divisions; i++)
                {
                    int y = _plot.Bottom - _plot.Height * i / Divisions;
                    g.DrawLine(pen, _plot.Left, y, _plot.Right, y);
                    if (!_hasValues) continue;
                    string text = FormatY(max * i / Divisions);
                    int w = TextRenderer.MeasureText(g, text, Font).Width;
                    TextRenderer.DrawText(g, text, Font, new Point(_plot.Left - w - 2, y - Font.Height / 2), Palette.Muted);
                }
            }
            string format = (_to - _from).TotalHours > 24 ? "d MMM HH:mm" : "HH:mm";
            for (int i = 0; i < TimeLabels; i++)
            {
                DateTime t = _from.AddSeconds((_to - _from).TotalSeconds * i / (TimeLabels - 1));
                string text = t.ToString(format, L.Culture);
                int w = TextRenderer.MeasureText(g, text, Font).Width;
                int x = (int)X(t) - (i == 0 ? 0 : i == TimeLabels - 1 ? w : w / 2);
                TextRenderer.DrawText(g, text, Font, new Point(x, _plot.Bottom + 2), Palette.Muted);
            }
        }

        private void DrawSeries(Graphics g, ChartSeries s, double max)
        {
            var run = new List<PointF>();
            using (var pen = new Pen(s.Color, LineWidth) { LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(s.Color))
            {
                void Flush()
                {
                    if (run.Count > 1) g.DrawLines(pen, run.ToArray());
                    else if (run.Count == 1) g.FillEllipse(brush, run[0].X - 1.5f, run[0].Y - 1.5f, 3, 3);
                    run.Clear();
                }
                for (int i = 0; i < s.Times.Length; i++)
                {
                    bool missing = double.IsNaN(s.Values[i]);
                    if (missing || (i > 0 && s.Times[i] - s.Times[i - 1] > _gap)) Flush();
                    if (missing) continue;
                    float y = _plot.Bottom - (float)(Math.Min(max, s.Values[i]) / max * _plot.Height);
                    run.Add(new PointF(X(s.Times[i]), y));
                }
                Flush();
            }
        }

        /// <summary>A round top for the value axis: 1, 2, 4, 8, 10 … of the unit (B, KB, MB, GB) the value falls in.</summary>
        private static double NiceMax(double max)
        {
            if (!(max > 0)) return 1;
            double unit = 1;
            while (max / unit >= 1024) unit *= 1024;
            return NiceSteps.First(n => max / unit <= n) * unit;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ChartMarker near = null;
            if (_plot.Contains(e.Location))
                near = _markers.Where(m => Math.Abs(X(m.Time) - e.X) <= HitPixels).OrderBy(m => Math.Abs(X(m.Time) - e.X)).FirstOrDefault();
            if (near == _hover) return;
            _hover = near;
            _tip.SetToolTip(this, near?.Text);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}

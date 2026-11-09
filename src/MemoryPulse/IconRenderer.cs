using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal enum Level { Normal, Warning, Critical }

    internal static class Palette
    {
        public static readonly Color Normal = Color.FromArgb(30, 130, 70);
        public static readonly Color Warning = Color.FromArgb(205, 135, 0);
        public static readonly Color Critical = Color.FromArgb(200, 40, 40);
        public static readonly Color InUse = Color.FromArgb(45, 110, 190);
        public static readonly Color Modified = Color.FromArgb(220, 140, 30);
        public static readonly Color Standby = Color.FromArgb(150, 195, 235);
        public static readonly Color Free = Color.FromArgb(225, 228, 232);
        public static readonly Color Muted = Color.FromArgb(95, 100, 108);
        public static readonly Color Card = Color.FromArgb(245, 247, 250);
        public static readonly Color InfoBack = Color.FromArgb(232, 241, 251);

        public static Color For(Level l) => l == Level.Critical ? Critical : l == Level.Warning ? Warning : Normal;
    }

    /// <summary>Draws the tray icon: available GB as a number on a coloured rounded square.</summary>
    internal static class IconRenderer
    {
        public static Icon Create(double availableGb, Level level)
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float r = size / 4f;
                using (var path = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), r))
                using (var brush = new SolidBrush(Palette.For(level)))
                    g.FillPath(brush, path);

                string text = availableGb >= 100 ? "99+" : Math.Floor(availableGb).ToString("0");
                float fontPx = size * (text.Length >= 3 ? 0.42f : text.Length == 2 ? 0.6f : 0.72f);
                using (var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, font, Brushes.White, new RectangleF(0, size * 0.04f, size, size), fmt);

                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}

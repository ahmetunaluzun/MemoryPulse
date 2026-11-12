using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MemoryPulse
{
    /// <summary>A details list that draws one column as a percentage bar.</summary>
    internal class BarListView : ListView
    {
        protected const TextFormatFlags CellText = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        private const int CellPadding = 4, HeaderPadding = 18;

        // Columns are added before the list gets its parent's font, so measure with the font the windows use.
        private static readonly Font HeaderFont = new Font("Segoe UI", 9.75f);

        /// <summary>Index of the bar column; its cells show the text over a bar of <see cref="FractionOf"/>.</summary>
        public int BarColumn = -1;

        public BarListView()
        {
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            OwnerDraw = true;
            DoubleBuffered = true;
        }

        /// <summary>Adds a column at least as wide as its header text.</summary>
        public void AddColumn(string key, int width, HorizontalAlignment align = HorizontalAlignment.Left) => AddColumn(this, key, width, align);

        public static void AddColumn(ListView list, string key, int width, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            string text = L.T(key);
            list.Columns.Add(text, Math.Max(width, TextRenderer.MeasureText(text, HeaderFont).Width + HeaderPadding), align);
        }

        /// <summary>0..1 for the bar of a row. Plain lists keep it in the item's Tag.</summary>
        protected virtual double FractionOf(int index, ListViewItem item) => item.Tag is double d ? d : 0;

        protected virtual void DrawFirstCell(Graphics g, Rectangle cell, int index, string text, Color fore) =>
            DrawText(g, cell, text, fore, HorizontalAlignment.Left);

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            e.DrawDefault = true;
            base.OnDrawColumnHeader(e);
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            if (e.ItemIndex < 0) return;
            bool selected = SelectedIndices.Contains(e.ItemIndex);
            Color back = !selected ? BackColor : Focused ? SystemColors.Highlight : SystemColors.Control;
            Color fore = selected && Focused ? SystemColors.HighlightText : ForeColor;
            Rectangle cell = e.Bounds;
            if (e.ColumnIndex == 0) cell.Width = Columns[0].Width; // the first cell reports the whole row
            using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, cell);

            if (e.ColumnIndex == 0) DrawFirstCell(e.Graphics, cell, e.ItemIndex, e.SubItem.Text, fore);
            else if (e.ColumnIndex == BarColumn) DrawBar(e.Graphics, cell, FractionOf(e.ItemIndex, e.Item), e.SubItem.Text);
            else DrawText(e.Graphics, cell, e.SubItem.Text, fore, e.Header.TextAlign);
        }

        protected void DrawText(Graphics g, Rectangle cell, string text, Color fore, HorizontalAlignment align)
        {
            var inner = Rectangle.FromLTRB(cell.Left + CellPadding, cell.Top, cell.Right - CellPadding, cell.Bottom);
            if (inner.Width <= 0) return;
            TextRenderer.DrawText(g, text, Font, inner, fore,
                CellText | (align == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left));
        }

        private void DrawBar(Graphics g, Rectangle cell, double fraction, string text)
        {
            var bar = Rectangle.FromLTRB(cell.Left + 3, cell.Top + 2, cell.Right - 3, cell.Bottom - 2);
            if (bar.Width <= 0) return;
            fraction = double.IsNaN(fraction) ? 0 : Math.Max(0, Math.Min(1, fraction));
            using (var empty = new SolidBrush(Palette.Free)) g.FillRectangle(empty, bar);
            using (var full = new SolidBrush(Palette.Standby)) g.FillRectangle(full, bar.X, bar.Y, (int)Math.Round(bar.Width * fraction), bar.Height);
            TextRenderer.DrawText(g, text, Font, bar, SystemColors.ControlText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        /// <summary>"42,1 %" for a share of a whole; the whole itself when there is nothing to compare with.</summary>
        public static string Percent(double fraction) => (fraction * 100).ToString("0.0", L.Culture) + " %";

        public static double Share(long part, long whole) => whole <= 0 ? 0 : (double)part / whole;
    }

    /// <summary>
    /// Folders of a scan as a tree with columns. Only the rows of opened folders exist, so a drive with
    /// hundreds of thousands of folders opens at once; children are sorted by size when first opened.
    /// </summary>
    internal sealed class DirTree : BarListView
    {
        private const int Indent = 18, Glyph = 9, GlyphGap = 6, LeftMargin = 6;

        private sealed class Row
        {
            public DirNode Node;
            public int Depth;
            public bool Expanded;
            public bool FilesOnly;   // the "[N files]" line: files directly in the parent folder
            public long ParentSize;
            public long Size => FilesOnly ? Node.OwnSize : Node.Size;
            public bool CanExpand => !FilesOnly && Node.Children != null;
        }

        private readonly List<Row> _rows = new List<Row>();

        public DirTree()
        {
            VirtualMode = true;
            MultiSelect = false;
            BarColumn = 2;
            AddColumn("disk.col.name", 300);
            AddColumn("disk.col.size", 90, HorizontalAlignment.Right);
            AddColumn("disk.col.share", 120, HorizontalAlignment.Center);
            AddColumn("disk.col.files", 80, HorizontalAlignment.Right);
            AddColumn("disk.col.folders", 80, HorizontalAlignment.Right);
            AddColumn("disk.col.modified", 130);
        }

        /// <summary>The folder of the selected row (for the files line: the folder holding them); null when none.</summary>
        public DirNode SelectedNode => SelectedIndices.Count > 0 && SelectedIndices[0] < _rows.Count ? _rows[SelectedIndices[0]].Node : null;

        public void Reset()
        {
            _rows.Clear();
            VirtualListSize = 0;
        }

        public void Show(DirNode root)
        {
            _rows.Clear();
            _rows.Add(new Row { Node = root, ParentSize = root.Size });
            VirtualListSize = 1;
            Toggle(0);
        }

        protected override void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e)
        {
            Row r = _rows[e.ItemIndex];
            DirNode n = r.Node;
            string name = r.FilesOnly ? L.T("disk.filesRow", n.OwnFiles)
                        : n.Unreadable ? n.Name + "  " + L.T("disk.unreadableMark") : n.Name;
            var item = new ListViewItem(name);
            item.SubItems.Add(Fmt.Bytes(r.Size));
            item.SubItems.Add(Percent(Share(r.Size, r.ParentSize)));
            item.SubItems.Add((r.FilesOnly ? n.OwnFiles : n.Files).ToString("N0", L.Culture));
            item.SubItems.Add(r.FilesOnly ? "" : n.Dirs.ToString("N0", L.Culture));
            item.SubItems.Add(DiskNative.Date(r.FilesOnly ? n.OwnModified : n.Modified));
            e.Item = item;
        }

        protected override double FractionOf(int index, ListViewItem item) =>
            index < _rows.Count ? Share(_rows[index].Size, _rows[index].ParentSize) : 0;

        protected override void DrawFirstCell(Graphics g, Rectangle cell, int index, string text, Color fore)
        {
            if (index >= _rows.Count) return;
            Row r = _rows[index];
            int x = cell.Left + LeftMargin + r.Depth * Indent;
            if (r.CanExpand && x + Glyph < cell.Right)
            {
                int mid = cell.Top + cell.Height / 2, half = Glyph / 2;
                Point[] arrow = r.Expanded
                    ? new[] { new Point(x, mid - half / 2 - 1), new Point(x + Glyph - 1, mid - half / 2 - 1), new Point(x + half, mid + half - 1) }
                    : new[] { new Point(x + 2, mid - half), new Point(x + 2 + half, mid), new Point(x + 2, mid + half) };
                using (var brush = new SolidBrush(fore)) g.FillPolygon(brush, arrow);
            }
            var rest = Rectangle.FromLTRB(x + Glyph + GlyphGap - 4, cell.Top, cell.Right, cell.Bottom);
            DrawText(g, rest, text, r.FilesOnly && fore == ForeColor ? Palette.Muted : fore, HorizontalAlignment.Left);
        }

        private void Toggle(int index)
        {
            if (index < 0 || index >= _rows.Count || !_rows[index].CanExpand) return;
            Row row = _rows[index];
            if (row.Expanded)
            {
                int end = index + 1;
                while (end < _rows.Count && _rows[end].Depth > row.Depth) end++;
                _rows.RemoveRange(index + 1, end - index - 1);
            }
            else _rows.InsertRange(index + 1, ChildRows(row));
            row.Expanded = !row.Expanded;
            VirtualListSize = _rows.Count;
            Invalidate();
        }

        private static List<Row> ChildRows(Row parent)
        {
            DirNode n = parent.Node;
            if (!n.Sorted)
            {
                Array.Sort(n.Children, (a, b) => b.Size.CompareTo(a.Size));
                n.Sorted = true;
            }
            var rows = new List<Row>(n.Children.Length + 1);
            bool filesPlaced = n.OwnFiles == 0;
            foreach (DirNode child in n.Children)
            {
                if (!filesPlaced && n.OwnSize >= child.Size)
                {
                    rows.Add(new Row { Node = n, Depth = parent.Depth + 1, FilesOnly = true, ParentSize = n.Size });
                    filesPlaced = true;
                }
                rows.Add(new Row { Node = child, Depth = parent.Depth + 1, ParentSize = n.Size });
            }
            if (!filesPlaced) rows.Add(new Row { Node = n, Depth = parent.Depth + 1, FilesOnly = true, ParentSize = n.Size });
            return rows;
        }

        private int Selected => SelectedIndices.Count > 0 ? SelectedIndices[0] : -1;

        private bool OnGlyph(int index, int mouseX)
        {
            int x = GetItemRect(index).Left + LeftMargin + _rows[index].Depth * Indent;
            return mouseX >= x - 4 && mouseX <= x + Glyph + 4;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            ListViewItem item = e.Button == MouseButtons.Left && e.Clicks == 1 ? GetItemAt(e.X, e.Y) : null;
            if (item != null && item.Index < _rows.Count && OnGlyph(item.Index, e.X)) Toggle(item.Index);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            ListViewItem item = GetItemAt(e.X, e.Y);
            if (item != null && item.Index < _rows.Count && !OnGlyph(item.Index, e.X)) Toggle(item.Index);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int index = Selected;
            if (index < 0 || index >= _rows.Count) return;
            Row row = _rows[index];
            if (e.KeyCode == Keys.Enter) Toggle(index);
            else if (e.KeyCode == Keys.Right && !row.Expanded) Toggle(index);
            else if (e.KeyCode == Keys.Left && row.Expanded) Toggle(index);
            else if (e.KeyCode == Keys.Left) SelectParent(index);
            else return;
            e.Handled = true;
        }

        private void SelectParent(int index)
        {
            int parent = index - 1;
            while (parent >= 0 && _rows[parent].Depth >= _rows[index].Depth) parent--;
            if (parent < 0) return;
            SelectedIndices.Clear();
            SelectedIndices.Add(parent);
            FocusedItem = Items[parent];
            EnsureVisible(parent);
        }

        /// <summary>Opens or closes the selected folder (what Enter does).</summary>
        public void ToggleSelected() => Toggle(Selected);
    }
}

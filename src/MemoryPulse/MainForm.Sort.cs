using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MemoryPulse
{
    /// <summary>
    /// Sorting the program list by a column header. A click on a number column picks the programs anew by that
    /// measure (the highest <see cref="ListByRam"/>); a second click on the same column only turns the order round.
    /// The program name column keeps the RAM choice and sorts it by name. Sorting by RAM, largest first, is the
    /// list as it always was: the largest by RAM plus the busiest by CPU.
    /// </summary>
    internal sealed partial class MainForm
    {
        private const int NameColumn = 0;
        private const int CountColumn = 1;
        private const int RamColumn = 2;

        private GroupBox _listBox;

        /// <summary>The list's frame title says what the programs were chosen by.</summary>
        private string ListTitle()
        {
            switch (_app.Settings.ListSortColumn)
            {
                case CountColumn: return L.T("list.title.count");
                case CpuColumn: return L.T("list.title.cpu");
                case IoColumn: return L.T("list.title.io");
                default: return L.T("list.title");
            }
        }

        private void SortBy(int column)
        {
            var s = _app.Settings;
            if (s.ListSortColumn == column)
                s.ListSortAscending = !s.ListSortAscending;
            else
            {
                s.ListSortColumn = column;
                s.ListSortAscending = column == NameColumn; // names A to Z, numbers largest first
            }
            try { s.Save(); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                Log.Write("ERROR", "List sort could not be saved: " + ex.Message);
            }
            ShowSortArrow();
            if (_listBox != null) _listBox.Text = ListTitle();
            if (_last != null) Show(_last);
        }

        /// <summary>The programs to list, in the order chosen by the column header.</summary>
        private List<ProcessUsage> SelectRows(out ulong total)
        {
            int column = _app.Settings.ListSortColumn;
            bool ascending = _app.Settings.ListSortAscending;
            List<ProcessUsage> rows;
            Func<ProcessUsage, double> measure = MeasureOf(column);
            if (measure == null) // name or RAM: the usual choice
                rows = MemoryInfo.TopProcesses(ListByRam, BusiestByCpu(), out total);
            else
                rows = MemoryInfo.AllProcesses(out total)
                    .OrderByDescending(measure).ThenByDescending(p => p.WorkingSet)
                    .Take(ListByRam).ToList();

            IOrderedEnumerable<ProcessUsage> ordered;
            if (column == NameColumn)
                ordered = ascending ? rows.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                                    : rows.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
            else
            {
                Func<ProcessUsage, double> key = measure ?? (p => p.WorkingSet);
                ordered = ascending ? rows.OrderBy(key) : rows.OrderByDescending(key);
            }
            return ordered.ThenByDescending(p => p.WorkingSet).ToList();
        }

        /// <summary>
        /// How a number column measures a program, or null when the list is chosen by RAM (RAM and name columns,
        /// or CPU/disk before their first reading, which needs two samples).
        /// </summary>
        private Func<ProcessUsage, double> MeasureOf(int column)
        {
            switch (column)
            {
                case CountColumn: return p => p.Count;
                case CpuColumn: return _cpuRates == null ? null : (Func<ProcessUsage, double>)(p => RateOf(_cpuRates, p.Name));
                case IoColumn: return _ioRates == null ? null : (Func<ProcessUsage, double>)(p => RateOf(_ioRates, p.Name));
                default: return null;
            }
        }

        private static double RateOf(Dictionary<string, double> rates, string program) =>
            rates.TryGetValue(program, out double v) ? v : 0;

        /// <summary>Windows' own sort arrow on the sorted column header.</summary>
        private void ShowSortArrow()
        {
            if (_list == null || !_list.IsHandleCreated) return;
            IntPtr header = SendMessage(_list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (header == IntPtr.Zero) return;
            for (int i = 0; i < _list.Columns.Count; i++)
            {
                var item = new HDITEM { mask = HDI_FORMAT };
                if (SendMessage(header, HDM_GETITEMW, (IntPtr)i, ref item) == IntPtr.Zero) continue;
                item.fmt &= ~(HDF_SORTUP | HDF_SORTDOWN);
                if (i == _app.Settings.ListSortColumn) item.fmt |= _app.Settings.ListSortAscending ? HDF_SORTUP : HDF_SORTDOWN;
                SendMessage(header, HDM_SETITEMW, (IntPtr)i, ref item);
            }
        }

        private const int LVM_GETHEADER = 0x101F;
        private const int HDM_GETITEMW = 0x120B;
        private const int HDM_SETITEMW = 0x120C;
        private const uint HDI_FORMAT = 0x0004;
        private const int HDF_SORTDOWN = 0x0200;
        private const int HDF_SORTUP = 0x0400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct HDITEM
        {
            public uint mask;
            public int cxy;
            public IntPtr pszText;
            public IntPtr hbm;
            public int cchTextMax;
            public int fmt;
            public IntPtr lParam;
            public int iImage;
            public int iOrder;
            public uint type;
            public IntPtr pvFilter;
            public uint state;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref HDITEM lParam);
    }
}

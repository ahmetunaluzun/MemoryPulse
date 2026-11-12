using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal sealed partial class DiskForm
    {
        private const int MinFillWidth = 160;

        private TabPage BuildFoldersTab()
        {
            var page = new TabPage(L.T("disk.tab.folders")) { Padding = new Padding(6) };
            _tree = new DirTree { Dock = DockStyle.Fill };
            Fill(_tree);
            _tree.ContextMenuStrip = PathMenu(() => _tree.SelectedNode?.FullPath, true, true);
            _tips.SetToolTip(_tree, L.Tip("disk.treeTip"));
            page.Controls.Add(_tree);
            return page;
        }

        private TabPage BuildFilesTab()
        {
            var page = new TabPage(L.T("disk.tab.files", DiskScanner.TopFileCount)) { Padding = new Padding(6) };
            _files = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true };
            BarListView.AddColumn(_files, "disk.col.file", 500);
            BarListView.AddColumn(_files, "disk.col.size", 100, HorizontalAlignment.Right);
            BarListView.AddColumn(_files, "disk.col.modified", 140);
            Fill(_files);
            SortableList.Attach(_files, 1, false); // biggest first
            _files.ContextMenuStrip = PathMenu(() => _files.SelectedItems.Count > 0 ? _files.SelectedItems[0].Text : null, false, false);
            page.Controls.Add(_files);
            return page;
        }

        private TabPage BuildTypesTab()
        {
            var page = new TabPage(L.T("disk.tab.types")) { Padding = new Padding(6) };
            _types = new BarListView { Dock = DockStyle.Fill, BarColumn = 2 };
            _types.AddColumn("disk.col.type", 200);
            _types.AddColumn("disk.col.size", 100, HorizontalAlignment.Right);
            _types.AddColumn("disk.col.shareAll", 140, HorizontalAlignment.Center);
            _types.AddColumn("disk.col.files", 100, HorizontalAlignment.Right);
            Fill(_types);
            page.Controls.Add(_types);
            return page;
        }

        /// <summary>Lets the first column take the width the others leave.</summary>
        private static void Fill(ListView list)
        {
            void Fit()
            {
                int others = 0;
                for (int i = 1; i < list.Columns.Count; i++) others += list.Columns[i].Width;
                int room = list.Width - SystemInformation.Border3DSize.Width * 2 - SystemInformation.VerticalScrollBarWidth - 2;
                list.Columns[0].Width = Math.Max(MinFillWidth, room - others);
            }
            list.Resize += (s, e) => Fit();
            list.HandleCreated += (s, e) => Fit();
        }

        private void ShowFiles(ScanResult r)
        {
            _files.BeginUpdate();
            foreach (FileEntry f in r.TopFiles)
            {
                string path = f.Path;
                var item = new ListViewItem(path) { Tag = new IComparable[] { path, f.Size, f.Modified } };
                item.SubItems.Add(Fmt.Bytes(f.Size));
                item.SubItems.Add(DiskNative.Date(f.Modified));
                _files.Items.Add(item);
            }
            _files.EndUpdate();
        }

        private void ShowTypes(ScanResult r)
        {
            _types.BeginUpdate();
            foreach (ExtStat t in r.Types)
            {
                double share = BarListView.Share(t.Size, r.Root.Size);
                var item = new ListViewItem(t.Extension.Length == 0 ? L.T("disk.noExtension") : t.Extension) { Tag = share };
                item.SubItems.Add(Fmt.Bytes(t.Size));
                item.SubItems.Add(BarListView.Percent(share));
                item.SubItems.Add(t.Count.ToString("N0", L.Culture));
                _types.Items.Add(item);
            }
            _types.EndUpdate();
        }

        /// <summary>Right-click menu of a folder or file: show it in Explorer, copy its path, scan again from it.</summary>
        private ContextMenuStrip PathMenu(Func<string> selected, bool folder, bool canRescan)
        {
            var menu = new ContextMenuStrip();
            string target = null;
            menu.Items.Add(L.T("disk.menu.explorer"), null, (s, e) => OpenInExplorer(target, folder));
            menu.Items.Add(L.T("disk.menu.copy"), null, (s, e) => CopyPath(target));
            ToolStripItem rescan = canRescan ? menu.Items.Add(L.T("disk.menu.rescan"), null, (s, e) => Rescan(target)) : null;
            menu.Opening += (s, e) =>
            {
                target = selected();
                e.Cancel = target == null;
                if (rescan != null) rescan.Enabled = _scanner == null;
            };
            return menu;
        }

        // Explorer is the only program started, and only to show a path the scan itself found.
        private void OpenInExplorer(string path, bool folder)
        {
            if (path == null) return;
            if (!DiskNative.Exists(path))
            {
                MessageBox.Show(this, L.T("disk.gone", path), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start("explorer.exe", folder ? $"\"{path}\"" : $"/select,\"{path}\"");
        }

        private static void CopyPath(string path)
        {
            if (path == null) return;
            try { Clipboard.SetText(path); }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                Log.Write("WARN", "Path could not be copied, the clipboard is in use: " + ex.Message);
            }
        }

        private void Rescan(string path)
        {
            if (path == null || _scanner != null) return;
            SelectTarget(path);
            StartScan(path);
        }
    }
}

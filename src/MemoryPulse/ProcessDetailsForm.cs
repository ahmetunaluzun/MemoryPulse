using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MemoryPulse
{
    /// <summary>Every running instance of one program: path, description, working folder, command line.</summary>
    internal sealed class ProcessDetailsForm : Form
    {
        private readonly string _name;
        private readonly bool _showCmd;
        private readonly string _kernelKey; // set for kernel processes: explained instead of "not readable"
        private readonly ListView _list;
        private readonly TextBox _detail;
        private readonly Label _summary;
        private readonly Button _open;
        private List<ProcessInstance> _items = new List<ProcessInstance>();

        public ProcessDetailsForm(string name)
        {
            _name = name;
            _showCmd = ProcessDetails.ShowsCommandLine(name);
            _kernelKey = ProcessDetails.KernelExplanationKey(name);
            Text = L.T("details.title", name);
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 560);
            MinimumSize = new Size(560, 420);
            Padding = new Padding(10);
            ShowInTaskbar = false;
            MinimizeBox = false;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            _summary = new Label { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(4, 0, 4, 8) };
            root.Controls.Add(_summary, 0, 0);

            _list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                                   Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable, Margin = new Padding(4) };
            _list.Columns.Add("PID", 70, HorizontalAlignment.Right);
            _list.Columns.Add(L.T("list.ram"), 90, HorizontalAlignment.Right);
            _list.Columns.Add(L.T(_showCmd ? "details.cmd" : "details.folder"), 400);
            _list.Resize += (s, e) => FitColumns();
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            _list.DoubleClick += (s, e) => OpenLocation();
            root.Controls.Add(_list, 0, 1);

            _detail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                                    Dock = DockStyle.Fill, Margin = new Padding(4), BackColor = Palette.Card,
                                    Font = new Font("Consolas", 9.5f) };
            root.Controls.Add(_detail, 0, 2);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
                                                Margin = new Padding(0, 6, 0, 0) };
            Button Add(string key, Action a)
            {
                var b = new Button { Text = L.T(key), AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(4) };
                b.Click += (s, e) => a();
                buttons.Controls.Add(b);
                return b;
            }
            var close = Add("details.close", Close);
            Add("details.copy", Copy);
            Add("btn.refresh", Reload);
            _open = Add("details.openFolder", OpenLocation);
            CancelButton = close;
            root.Controls.Add(buttons, 0, 3);

            Shown += (s, e) => { Reload(); FitColumns(); };
        }

        private void FitColumns()
        {
            int inner = _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[1].Width - 4;
            _list.Columns[2].Width = Math.Max(150, inner);
        }

        private void Reload()
        {
            int keepPid = Selected?.Pid ?? -1;
            UseWaitCursor = true;
            try { _items = ProcessDetails.Read(_name); }
            finally { UseWaitCursor = false; }

            ulong total = 0;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var i in _items)
            {
                total += i.WorkingSet;
                var item = new ListViewItem(i.Pid.ToString(L.Culture)) { Tag = i };
                item.SubItems.Add(Mb(i.WorkingSet));
                item.SubItems.Add((_showCmd ? ProcessDetails.Arguments(i.CommandLine) : i.WorkingFolder) ?? L.T(_kernelKey != null ? "details.sys.none" : "details.noAccess"));
                _list.Items.Add(item);
                if (i.Pid == keepPid) item.Selected = true;
            }
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
            _list.EndUpdate();

            var first = _items.Find(i => i.Description != null || i.Company != null);
            var sb = new StringBuilder();
            sb.Append(L.T("details.summary", _items.Count, L.Gb(MemorySnapshot.Gb(total))));
            if (first?.Description != null) sb.Append("\n").Append(first.Description);
            if (first?.Company != null) sb.Append("  ·  ").Append(first.Company);
            if (_kernelKey != null && _items.Count > 0) sb.Append("\n").Append(L.T("details.sys.kind"));
            if (_items.Count == 0) sb.Append("\n").Append(L.T("details.gone"));
            _summary.Text = sb.ToString();
            ShowSelected();
        }

        private ProcessInstance Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ProcessInstance : null;

        private void ShowSelected()
        {
            var i = Selected;
            _open.Enabled = i?.Path != null;
            if (i == null) { _detail.Text = ""; return; }
            if (_kernelKey != null)
            {
                _detail.Text = string.Join(Environment.NewLine, L.T("details.f.pid") + i.Pid.ToString(L.Culture),
                    L.T("details.f.ram") + Mb(i.WorkingSet), "", L.T(_kernelKey));
                return;
            }
            string na = L.T("details.noAccess");
            var lines = new List<string>
            {
                L.T("details.f.pid") + i.Pid.ToString(L.Culture),
                L.T("details.f.ram") + Mb(i.WorkingSet),
                L.T("details.f.path") + (i.Path ?? na),
                L.T("details.f.desc") + (i.Description ?? "—"),
                L.T("details.f.company") + (i.Company ?? "—"),
                L.T("details.f.folder") + (i.WorkingFolder ?? na),
            };
            if (_showCmd) lines.Add(L.T("details.f.cmd") + (i.CommandLine ?? na));
            _detail.Text = string.Join(Environment.NewLine, lines);
        }

        private static string Mb(ulong bytes) => (bytes / 1048576.0).ToString("N0", L.Culture) + " MB";

        private void OpenLocation()
        {
            string path = Selected?.Path;
            if (path == null || !File.Exists(path)) return;
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }

        private void Copy()
        {
            if (_detail.TextLength > 0) Clipboard.SetText(_detail.Text);
        }
    }
}

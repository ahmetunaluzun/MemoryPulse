using System;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal sealed partial class MainForm
    {
        private CheckBox _history;

        /// <summary>Right-click items of the program list: the program's history and its network watch.</summary>
        private void AddHistoryMenu(ContextMenuStrip menu, Func<string> target)
        {
            menu.Items.Add(L.T("history.menu"), null, (s, e) => OpenHistoryFor(target()));
            var watch = new ToolStripMenuItem(L.T("history.watchNet")) { ToolTipText = L.Tip("history.watchNetTip") };
            watch.Click += (s, e) => ToggleNetworkWatch(target());
            menu.Items.Add(watch);
            menu.Opening += (s, e) => watch.Checked = target() != null && _app.IsNetworkWatched(target());
        }

        private void OpenHistoryFor(string name)
        {
            if (name == null) return;
            using (var f = new ProcessDetailsForm(name, true)) f.ShowDialog(this);
        }

        // Turning the watch on is refused with a reason when it cannot work; turning it off always works.
        private void ToggleNetworkWatch(string name)
        {
            if (name == null) return;
            bool on = !_app.IsNetworkWatched(name);
            string blocked = !on ? null
                : Edition.IsStore ? "history.watchNet.store"
                : !Native.IsElevated ? "history.watchNet.needsAdmin"
                : !_app.Settings.HistoryEnabled ? "history.watchNet.off"
                : null;
            if (blocked != null)
            {
                MessageBox.Show(this, L.T(blocked), L.T("history.watchNet"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _app.SetNetworkWatch(name, on);
            if (_status != null) _status.Text = L.T(on ? "history.watchNet.on" : "history.watchNet.stopped", name);
        }
    }
}

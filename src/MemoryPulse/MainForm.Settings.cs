using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal sealed partial class MainForm
    {
        private const int SettingsWidth = 600; // the settings window's content width at 100% scaling

        private NumericUpDown _critical, _warning, _interval, _cooldown;
        private CheckBox _autoClean, _startup;
        private ComboBox _language;

        /// <summary>The settings, in their own window so the main window stays short.</summary>
        private void ShowSettings()
        {
            bool languageChanged;
            using (var d = new Form { Text = L.T("settings.title"), Font = Font, AutoScaleMode = AutoScaleMode.Dpi,
                                      FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                                      ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, Icon = Icon,
                                      Padding = new Padding(12) })
            {
                var t = BuildSettings(d);
                d.Controls.Add(t);
                Size pref = t.GetPreferredSize(new Size(SettingsWidth, 0));
                d.ClientSize = new Size(Math.Max(SettingsWidth, pref.Width) + d.Padding.Horizontal, pref.Height + d.Padding.Vertical);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                languageChanged = (string)d.Tag == "language";
            }
            if (_status != null) _status.Text = L.T("settings.saved", DateTime.Now.ToString("HH:mm:ss", L.Culture));
            _app.ApplySettings(languageChanged); // recreates this window when the language changed
        }

        private TableLayoutPanel BuildSettings(Form dialog)
        {
            var s = _app.Settings;
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _critical = Number(t, "settings.critical", "tip.critical", 1, 512, s.CriticalGb, 0, 0);
            _warning = Number(t, "settings.warning", "tip.warning", 2, 1024, s.WarningGb, 2, 0);
            _interval = Number(t, "settings.interval", "tip.interval", 5, 3600, s.IntervalSec, 0, 1);
            _cooldown = Number(t, "settings.cooldown", "tip.cooldown", 1, 1440, s.CooldownMin, 2, 1);

            t.Controls.Add(Caption("settings.language"), 0, 2);
            _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Anchor = AnchorStyles.Left, Margin = new Padding(3, 4, 3, 4) };
            _language.Items.Add(new LangItem("auto", L.T("settings.languageAuto")));
            foreach (var kv in L.Available()) _language.Items.Add(new LangItem(kv.Key, kv.Value));
            _language.SelectedItem = _language.Items.Cast<LangItem>().FirstOrDefault(i => i.Code == s.Language) ?? _language.Items[0];
            _language.Width = _language.Items.Cast<LangItem>().Max(i => TextRenderer.MeasureText(i.Name, _language.Font).Width) + 36;
            t.Controls.Add(_language, 1, 2);
            t.SetColumnSpan(_language, 3);

            // Store edition: no automatic clearing option.
            int row = 3;
            if (Edition.CanAutoClean) _autoClean = Check(t, "settings.autoClean", "tip.autoClean", s.AutoClean, row++);
            _history = Check(t, "settings.history", "tip.history", s.HistoryEnabled, row++);
            _startup = Check(t, "settings.startup", Edition.IsStore ? "tip.startupStore" : "tip.startup", Startup.IsEnabled(), row++);

            // Bottom row: the log on the left, Save and Cancel on the right.
            var log = new LinkLabel { Text = L.T("btn.log"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 18, 3, 0) };
            log.LinkClicked += (o, e) => { if (System.IO.File.Exists(AppPaths.LogFile)) Process.Start("notepad.exe", $"\"{AppPaths.LogFile}\""); };
            t.Controls.Add(log, 0, row);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right,
                                                Margin = new Padding(0, 12, 0, 0), WrapContents = false };
            var cancel = new Button { Text = L.T("settings.cancel"), AutoSize = true, Padding = new Padding(14, 2, 14, 2), DialogResult = DialogResult.Cancel };
            var save = new Button { Text = L.T("settings.save"), AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
            save.Click += (o, e) =>
            {
                if (!SaveSettings(dialog, out bool languageChanged)) return;
                dialog.Tag = languageChanged ? "language" : null;
                dialog.DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            t.Controls.Add(buttons, 1, row);
            t.SetColumnSpan(buttons, 3);
            dialog.AcceptButton = save;
            dialog.CancelButton = cancel;
            return t;
        }

        private Label Caption(string key) =>
            new Label { Text = L.T(key), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 7) };

        private NumericUpDown Number(TableLayoutPanel t, string key, string tip, int min, int max, int value, int col, int row)
        {
            var label = Caption(key);
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Width = 80,
                                        TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Left };
            _tips.SetToolTip(label, L.Tip(tip));
            _tips.SetToolTip(n, L.Tip(tip));
            t.Controls.Add(label, col, row);
            t.Controls.Add(n, col + 1, row);
            return n;
        }

        private CheckBox Check(TableLayoutPanel t, string key, string tip, bool value, int row)
        {
            var c = new CheckBox { Text = L.T(key), AutoSize = true, Checked = value, Margin = new Padding(3, 8, 3, 2), Anchor = AnchorStyles.Left };
            _tips.SetToolTip(c, L.Tip(tip));
            t.Controls.Add(c, 0, row);
            t.SetColumnSpan(c, 4);
            return c;
        }

        /// <summary>Saves the settings window's values. False when they are not valid or could not be written.</summary>
        private bool SaveSettings(IWin32Window owner, out bool languageChanged)
        {
            languageChanged = false;
            if (_warning.Value <= _critical.Value)
            {
                MessageBox.Show(owner, L.T("settings.warningTooLow"), L.T("settings.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var s = _app.Settings;
            string lang = ((LangItem)_language.SelectedItem).Code;
            languageChanged = lang != s.Language;
            s.CriticalGb = (int)_critical.Value;
            s.WarningGb = (int)_warning.Value;
            s.IntervalSec = (int)_interval.Value;
            s.CooldownMin = (int)_cooldown.Value;
            if (_autoClean != null) s.AutoClean = _autoClean.Checked;
            s.Language = lang;
            s.HistoryEnabled = _history.Checked;
            try { s.Save(); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(owner, L.T("settings.saveFailed", ex.Message), L.T("settings.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (_startup.Checked != Startup.IsEnabled())
            {
                var r = Startup.Set(_startup.Checked);
                Log.Write("INFO", $"Start with Windows -> {_startup.Checked}: {r}");
                if (r != ActionResult.Ok)
                {
                    _startup.Checked = Startup.IsEnabled();
                    if (r == ActionResult.Failed)
                        MessageBox.Show(owner, L.T("settings.startupFailed"), L.T("settings.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else if (Edition.IsStore && StoreStartup.DisabledByUser())
                        MessageBox.Show(owner, L.T("settings.startupDisabledByUser"), L.T("settings.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            return true;
        }

        private sealed class LangItem
        {
            public readonly string Code, Name;
            public LangItem(string code, string name) { Code = code; Name = name; }
            public override string ToString() => Name;
        }
    }
}

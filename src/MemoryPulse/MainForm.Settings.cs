using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MemoryPulse
{
    internal sealed partial class MainForm
    {
        private NumericUpDown _critical, _warning, _interval, _cooldown;
        private CheckBox _autoClean, _startup;
        private ComboBox _language;

        private Control BuildSettings()
        {
            var s = _app.Settings;
            var g = new GroupBox { Text = L.T("settings.title"), Dock = DockStyle.Fill, AutoSize = true,
                                   Margin = new Padding(4), Padding = new Padding(10, 8, 10, 8) };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
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

            _autoClean = Check(t, "settings.autoClean", "tip.autoClean", s.AutoClean, 3);
            _startup = Check(t, "settings.startup", "tip.startup", Startup.IsEnabled(), 4);
            t.SetColumnSpan(_startup, 3);

            var save = new Button { Text = L.T("settings.save"), AutoSize = true, Padding = new Padding(14, 2, 14, 2), Anchor = AnchorStyles.Right };
            save.Click += (o, e) => SaveSettings();
            t.Controls.Add(save, 3, 4);
            g.Controls.Add(t);
            return g;
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

        private void SaveSettings()
        {
            if (_warning.Value <= _critical.Value)
            {
                MessageBox.Show(this, L.T("settings.warningTooLow"), L.T("settings.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var s = _app.Settings;
            string lang = ((LangItem)_language.SelectedItem).Code;
            bool languageChanged = lang != s.Language;
            s.CriticalGb = (int)_critical.Value;
            s.WarningGb = (int)_warning.Value;
            s.IntervalSec = (int)_interval.Value;
            s.CooldownMin = (int)_cooldown.Value;
            s.AutoClean = _autoClean.Checked;
            s.Language = lang;
            try { s.Save(); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, L.T("settings.saveFailed", ex.Message), L.T("settings.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_startup.Checked != Startup.IsEnabled())
            {
                var r = Startup.Set(_startup.Checked);
                Log.Write("INFO", $"Start with Windows -> {_startup.Checked}: {r}");
                if (r != ActionResult.Ok)
                {
                    _startup.Checked = Startup.IsEnabled();
                    if (r == ActionResult.Failed)
                        MessageBox.Show(this, L.T("settings.startupFailed"), L.T("settings.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            if (_status != null) _status.Text = L.T("settings.saved", DateTime.Now.ToString("HH:mm:ss", L.Culture));
            _app.ApplySettings(languageChanged); // recreates this window when the language changed
        }

        private sealed class LangItem
        {
            public readonly string Code, Name;
            public LangItem(string code, string name) { Code = code; Name = name; }
            public override string ToString() => Name;
        }
    }
}

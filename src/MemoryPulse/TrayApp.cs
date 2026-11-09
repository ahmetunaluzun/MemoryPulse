using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MemoryPulse
{
    /// <summary>Runs in the notification area: periodic check, icon, menu, notifications.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly SynchronizationContext _ui;
        private MainForm _form;
        private string _iconKey = "";
        private DateTime _lastAction = DateTime.MinValue;

        public Settings Settings { get; private set; }
        public event Action<MemorySnapshot> Updated;

        public TrayApp(bool showWindow, EventWaitHandle showSignal)
        {
            Settings = Settings.Load();
            L.Use(Settings.Language);
            _ui = new WindowsFormsSynchronizationContext();

            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            BuildMenu();
            _timer.Tick += (s, e) => Check();
            _timer.Interval = Settings.IntervalSec * 1000;
            _timer.Start();

            Log.Write("INFO", $"Started {Program.Version}. Elevated: {Native.IsElevated}. Critical {Settings.CriticalGb} GB, " +
                              $"warning {Settings.WarningGb} GB, interval {Settings.IntervalSec} s");
            Check();
            _tray.Visible = true;

            var waiter = new Thread(() => { while (showSignal.WaitOne()) _ui.Post(_ => ShowWindow(), null); })
                { IsBackground = true, Name = "show-signal" };
            waiter.Start();

            if (showWindow) ShowWindow();
            else _ui.Post(_ => ReleaseMemory(), null);
        }

        public Level LevelOf(double availableGb) =>
            availableGb < Settings.CriticalGb ? Level.Critical : availableGb < Settings.WarningGb ? Level.Warning : Level.Normal;

        private void BuildMenu()
        {
            var old = _tray.ContextMenuStrip;
            var m = new ContextMenuStrip();
            var open = m.Items.Add(L.T("menu.open"), null, (s, e) => ShowWindow());
            open.Font = new Font(open.Font, FontStyle.Bold);
            m.Items.Add(L.T("menu.clean"), null, (s, e) => CleanWithConfirm(null));
            m.Items.Add(L.T("menu.taskmgr"), null, (s, e) => Program.OpenTaskManager());
            m.Items.Add(L.T("update.check"), null, (s, e) => System.Diagnostics.Process.Start(Program.ReleasesUrl));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("menu.exit"), null, (s, e) => ExitApp());
            _tray.ContextMenuStrip = m;
            old?.Dispose();
        }

        /// <summary>Called after settings change (language, interval, thresholds).</summary>
        public void ApplySettings(bool languageChanged)
        {
            _timer.Interval = Settings.IntervalSec * 1000;
            _iconKey = "";
            if (languageChanged)
            {
                L.Use(Settings.Language);
                BuildMenu();
                if (_form != null && !_form.IsDisposed)
                {
                    var bounds = _form.Bounds;
                    _form.Close();
                    _form = new MainForm(this) { StartPosition = FormStartPosition.Manual, Bounds = bounds };
                    _form.Show();
                }
            }
            Check();
        }

        public MemorySnapshot Check()
        {
            MemorySnapshot s;
            try { s = MemoryInfo.Read(); }
            catch (System.ComponentModel.Win32Exception ex) { Log.Write("ERROR", "Memory read failed: " + ex.Message); return null; }

            double avail = MemorySnapshot.Gb(s.Available);
            var level = LevelOf(avail);
            string key = $"{Math.Floor(avail)}|{level}";
            if (key != _iconKey)
            {
                var old = _tray.Icon;
                _tray.Icon = IconRenderer.Create(avail, level);
                old?.Dispose();
                _iconKey = key;
                if (_form != null && !_form.IsDisposed) _form.Icon = _tray.Icon;
            }
            string tip = L.T("tray.tooltip", L.Gb(avail), L.Gb(MemorySnapshot.Gb(s.Cached)));
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            if (level == Level.Critical && (DateTime.Now - _lastAction).TotalMinutes >= Settings.CooldownMin)
            {
                _lastAction = DateTime.Now;
                OnCritical(s);
            }
            Updated?.Invoke(s);
            return s;
        }

        private void OnCritical(MemorySnapshot before)
        {
            string top = TopList();
            string body;
            if (Settings.AutoClean && Native.IsElevated)
            {
                var r = Elevated.PurgeStandby();
                body = r == ActionResult.Ok ? L.T("notify.cleaned", L.Gb(MemorySnapshot.Gb(before.Standby)))
                                            : L.T("notify.cleanFailed");
            }
            else if (Settings.AutoClean)
                body = L.T("notify.needsAdmin");
            else
                body = L.T("notify.autoOff");

            Log.Write("WARN", $"Critical: available {MemorySnapshot.Gb(before.Available):0.0} GB (threshold {Settings.CriticalGb} GB), " +
                              $"standby {MemorySnapshot.Gb(before.Standby):0.0} GB. {body} Top: {top}");
            _tray.ShowBalloonTip(10000, L.T("notify.lowTitle", L.Gb(MemorySnapshot.Gb(before.Available))),
                body + "\n" + L.T("notify.top", top), ToolTipIcon.Warning);
        }

        public static string TopList() =>
            string.Join(", ", MemoryInfo.TopProcesses(5).Select(p => $"{p.Name} {L.Gb(MemorySnapshot.Gb(p.WorkingSet))}"));

        /// <summary>Manual clean from menu or window, after explaining why it is rarely needed.</summary>
        public void CleanWithConfirm(IWin32Window owner)
        {
            if (MessageBox.Show(owner, L.T("clean.confirm"), L.T("clean.title"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            var before = MemoryInfo.Read();
            Cursor.Current = Cursors.WaitCursor;
            var r = Elevated.PurgeStandby();
            Cursor.Current = Cursors.Default;
            Thread.Sleep(500);
            var after = Check() ?? MemoryInfo.Read();
            Log.Write("INFO", $"Manual clean: {r}. Standby {MemorySnapshot.Gb(before.Standby):0.0} -> " +
                              $"{MemorySnapshot.Gb(after.Standby):0.0} GB, available {MemorySnapshot.Gb(after.Available):0.0} GB");
            if (r == ActionResult.Ok)
                _tray.ShowBalloonTip(5000, L.T("clean.doneTitle"),
                    L.T("clean.done", L.Gb(MemorySnapshot.Gb(before.Standby)), L.Gb(MemorySnapshot.Gb(after.Available))), ToolTipIcon.Info);
            else if (r == ActionResult.Failed)
                MessageBox.Show(owner, L.T("clean.failed"), L.T("clean.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void ShowWindow()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new MainForm(this);
                _form.FormClosed += (s, e) => _ui.Post(_ => ReleaseMemory(), null);
            }
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Show();
            Native.ShowWindow(_form.Handle, 9); // SW_RESTORE: the first show can be ignored when started hidden
            _form.Activate();
            Native.SetForegroundWindow(_form.Handle);
        }

        // The window is the heavy part; once it is closed, give its memory back.
        private static void ReleaseMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Native.TrimWorkingSet();
        }

        private void ExitApp()
        {
            Log.Write("INFO", "Exit from tray menu");
            _timer.Stop();
            _tray.Visible = false;
            if (_form != null && !_form.IsDisposed) _form.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _tray.Icon?.Dispose(); _tray.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace MemoryPulse
{
    /// <summary>
    /// "Start with Windows" in the Store edition: the startup task declared in the package manifest. Runs without
    /// administrator rights; the user can also turn it off in Task Manager > Startup apps, and then only the user
    /// can turn it back on there.
    /// </summary>
    internal static class StoreStartup
    {
        public static bool IsEnabled()
        {
            var state = Run(t => Task.FromResult(t.State));
            return state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy;
        }

        public static ActionResult Set(bool enable)
        {
            StartupTaskState? state = Run(async t =>
            {
                if (!enable) { t.Disable(); return t.State; }
                return await t.RequestEnableAsync();
            });
            Log.Write("INFO", $"Store startup task -> {enable}: {state?.ToString() ?? "unknown"}");
            if (state == null) return ActionResult.Failed;
            bool on = state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy;
            if (on == enable) return ActionResult.Ok;
            // Turned off by the user in Task Manager or by policy: Windows does not let the app turn it back on.
            return state == StartupTaskState.DisabledByUser || state == StartupTaskState.DisabledByPolicy
                ? ActionResult.Cancelled : ActionResult.Failed;
        }

        /// <summary>True when the user turned the task off in Task Manager; only they can turn it back on there.</summary>
        public static bool DisabledByUser() => Run(t => Task.FromResult(t.State)) == StartupTaskState.DisabledByUser;

        private static StartupTaskState? Run(Func<StartupTask, Task<StartupTaskState>> action)
        {
            try
            {
                // Off the UI thread: waiting on a WinRT call from the UI thread can block the window.
                return Task.Run(async () => await action(await StartupTask.GetAsync(Edition.StartupTaskId))).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is COMException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                Log.Write("ERROR", "Store startup task: " + ex.Message);
                return null;
            }
        }
    }
}

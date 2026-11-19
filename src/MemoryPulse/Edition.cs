using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MemoryPulse
{
    /// <summary>
    /// One exe, two editions. Started from the Microsoft Store package (MSIX) it is the Store edition: no cache
    /// clearing (the Store does not accept apps that need administrator rights), no update check (the Store
    /// updates it) and "Start with Windows" through the package's startup task. Started from the GitHub exe it
    /// is the full edition.
    /// </summary>
    internal static class Edition
    {
        private const int APPMODEL_ERROR_NO_PACKAGE = 15700;
        public const string StartupTaskId = "MemoryPulseStartup"; // must match Package.appxmanifest

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);

        public static readonly bool IsStore = DetectPackage();

        /// <summary>
        /// Clearing the cache by hand: both editions. It needs administrator rights; when the app is not elevated
        /// (always so in the Store edition) a helper copy of the exe asks for them through UAC.
        /// </summary>
        public static bool CanClean => true;

        /// <summary>
        /// Clearing the cache on its own at the critical level: GitHub edition only. It never asks through UAC,
        /// so it works only when the app already runs elevated, which the Store edition never does.
        /// </summary>
        public static bool CanAutoClean => !IsStore;

        /// <summary>The Store updates its apps itself.</summary>
        public static bool CanCheckUpdates => !IsStore;

        public static string Name => IsStore ? "Store" : "GitHub";

        private static bool DetectPackage()
        {
            try
            {
                int length = 0;
                return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
            }
            catch (EntryPointNotFoundException) { return false; } // Windows 7: no packages
        }

        /// <summary>True when the Store edition was started by its startup task at sign-in.</summary>
        public static bool StartedAtSignIn()
        {
            if (!IsStore) return false;
            try
            {
                var args = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
                return args != null && args.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
            }
            catch (Exception ex) when (ex is COMException || ex is TypeLoadException || ex is UnauthorizedAccessException)
            {
                Log.Write("WARN", "Activation kind could not be read: " + ex.Message);
                return false;
            }
        }
    }
}

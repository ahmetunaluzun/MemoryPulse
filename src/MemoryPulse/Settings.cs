using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MemoryPulse
{
    internal static class AppPaths
    {
        public static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemoryPulse");
        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        public static string LogFile => Path.Combine(DataDir, "memorypulse.log");
    }

    [DataContract]
    internal sealed class Settings
    {
        [DataMember] public int CriticalGb;     // below this: red icon, notification, optional purge
        [DataMember] public int WarningGb;      // below this: amber icon
        [DataMember] public int IntervalSec;    // background check interval
        [DataMember] public int CooldownMin;    // minimum gap between two automatic actions
        [DataMember] public bool AutoClean;     // purge standby on critical (needs administrator)
        [DataMember] public string Language;
        [DataMember] public bool CheckUpdates;      // ask GitHub once a day for a newer release
        [DataMember] public string NotifiedVersion; // newest release already announced in a notification

        public Settings() => SetDefaults();

        // The JSON serializer skips constructors and field initializers; without this, a field missing from
        // the file (older file, hand edit) would come back as 0/false instead of its default.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            CriticalGb = 4;
            WarningGb = 8;
            IntervalSec = 60;
            CooldownMin = 10;
            AutoClean = true;
            Language = "auto";
            CheckUpdates = true;
            NotifiedVersion = null;
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    using (var fs = File.OpenRead(AppPaths.SettingsFile))
                    {
                        if (new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs) is Settings s)
                            return s.Validated();
                        Log.Write("ERROR", "Settings file is empty (null), using defaults");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException)
            {
                Log.Write("ERROR", "Settings could not be read, using defaults: " + ex.Message);
            }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(w, this);
                File.WriteAllBytes(AppPaths.SettingsFile, ms.ToArray());
            }
            Log.Write("INFO", $"Settings saved: critical {CriticalGb} GB, warning {WarningGb} GB, interval {IntervalSec} s, " +
                              $"cooldown {CooldownMin} min, auto clean {AutoClean}, language {Language}, update check {CheckUpdates}");
        }

        private Settings Validated()
        {
            CriticalGb = Clamp(CriticalGb, 1, 512, 4);
            WarningGb = Clamp(WarningGb, CriticalGb + 1, 1024, Math.Max(8, CriticalGb + 1));
            IntervalSec = Clamp(IntervalSec, 5, 3600, 60);
            CooldownMin = Clamp(CooldownMin, 1, 1440, 10);
            if (string.IsNullOrWhiteSpace(Language)) Language = "auto";
            return this;
        }

        private static int Clamp(int v, int min, int max, int fallback) => v < min || v > max ? fallback : v;
    }

    internal static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object Gate = new object();

        public static void Write(string level, string message)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.DataDir);
                    var f = new FileInfo(AppPaths.LogFile);
                    if (f.Exists && f.Length > MaxBytes)
                        File.Copy(f.FullName, f.FullName + ".1", true);
                    if (f.Exists && f.Length > MaxBytes) File.Delete(f.FullName);
                    File.AppendAllText(AppPaths.LogFile,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
                }
                catch (IOException) { /* logging must never break the app */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace MemoryPulse
{
    /// <summary>
    /// UI strings from embedded Lang/*.json files. Adding a language = adding one JSON file
    /// with the same keys and a "_name" entry (its own name, e.g. "Deutsch").
    /// </summary>
    internal static class L
    {
        private const string Prefix = "MemoryPulse.Lang.";
        private static Dictionary<string, string> _current = new Dictionary<string, string>();
        private static Dictionary<string, string> _fallback = new Dictionary<string, string>();

        public static string Code { get; private set; } = "en";
        public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

        /// <summary>Available languages: code → display name.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Available()
        {
            foreach (var code in Codes())
            {
                var d = Load(code);
                yield return new KeyValuePair<string, string>(code, d.TryGetValue("_name", out var n) ? n : code);
            }
        }

        public static void Use(string setting)
        {
            var codes = Codes().ToList();
            string code = setting;
            if (string.IsNullOrEmpty(code) || code == "auto" || !codes.Contains(code))
                code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (!codes.Contains(code)) code = "en";
            Code = code;
            _fallback = Load("en");
            _current = code == "en" ? _fallback : Load(code);
            try { Culture = CultureInfo.GetCultureInfo(_current.TryGetValue("_culture", out var c) ? c : code); }
            catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
        }

        public static string T(string key)
        {
            if (_current.TryGetValue(key, out var v) || _fallback.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string T(string key, params object[] args) => string.Format(Culture, T(key), args);

        /// <summary>Text for a tooltip, wrapped to ~60 characters per line (WinForms tooltips do not wrap).</summary>
        public static string Tip(string key)
        {
            var sb = new System.Text.StringBuilder();
            int line = 0;
            foreach (var word in T(key).Split(' '))
            {
                if (line > 0 && line + word.Length > 60) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(word);
                line += word.Length;
            }
            return sb.ToString();
        }

        public static string Gb(double gb) => gb.ToString("0.0", Culture) + " GB";

        private static IEnumerable<string> Codes() =>
            typeof(L).Assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                .Select(n => n.Substring(Prefix.Length, n.Length - Prefix.Length - 5))
                .OrderBy(c => c);

        private static Dictionary<string, string> Load(string code)
        {
            using (Stream s = typeof(L).Assembly.GetManifestResourceStream(Prefix + code + ".json"))
            {
                if (s == null) return new Dictionary<string, string>();
                var ser = new DataContractJsonSerializer(typeof(Dictionary<string, string>),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                return (Dictionary<string, string>)ser.ReadObject(s);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MemoryPulse
{
    /// <summary>One parsed line of a day file.</summary>
    internal struct HistoryLine
    {
        public char Kind;      // S system, D drive, P program, N network of a watched program
        public DateTime Time;
        public string Name;    // drive or program; null for S
        public long[] V;
    }

    /// <summary>
    /// The usage history on disk: one text file per day under %APPDATA%\MemoryPulse\history.
    ///   S,time,seconds,available,cached,commitUsed,diskRead,diskWrite,netRx,netTx
    ///   D,time,drive,free,total
    ///   P,time,program,processCount,workingSet,ioRead,ioWrite
    ///   N,time,program,netRx,netTx
    /// workingSet is the private working set from 1.7.1 on (older files: full working set, shared pages included).
    /// Sizes are bytes; disk, network and I/O values are bytes during the interval; -1 = not available.
    /// </summary>
    internal static class HistoryStore
    {
        private static readonly object Gate = new object();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string FileFor(DateTime day) =>
            Path.Combine(HistoryConfig.Dir, day.ToString(HistoryConfig.FileDateFormat, Inv) + ".csv");

        /// <summary>Program names go into a comma separated line.</summary>
        public static string Clean(string name) => name.Replace(',', '_').Replace('\r', ' ').Replace('\n', ' ');

        public static void Append(DateTime now, string lines)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(HistoryConfig.Dir);
                string file = FileFor(now);
                if (!File.Exists(file)) lines = HistoryConfig.Header + Environment.NewLine + lines;
                File.AppendAllText(file, lines, Encoding.UTF8);
            }
        }

        /// <summary>Deletes day files older than the keep period. Files with other names are left alone.</summary>
        public static void DeleteOld(DateTime today)
        {
            if (!Directory.Exists(HistoryConfig.Dir)) return;
            DateTime oldest = today.Date.AddDays(-HistoryConfig.KeepDays);
            foreach (string file in Directory.GetFiles(HistoryConfig.Dir, "*.csv"))
            {
                if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), HistoryConfig.FileDateFormat, Inv,
                        DateTimeStyles.None, out DateTime day) || day >= oldest) continue;
                try { File.Delete(file); Log.Write("INFO", "History: deleted old day file " + Path.GetFileName(file)); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Write("WARN", $"History: {Path.GetFileName(file)} could not be deleted: {ex.Message}");
                }
            }
        }

        /// <summary>Streams the lines between two moments, oldest first. Unreadable lines are skipped.</summary>
        public static IEnumerable<HistoryLine> Read(DateTime from, DateTime to)
        {
            for (DateTime day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                string file = FileFor(day);
                if (!File.Exists(file)) continue;
                foreach (var line in ReadFile(file, day))
                    if (line.Time >= from && line.Time <= to) yield return line;
            }
        }

        private static IEnumerable<HistoryLine> ReadFile(string file, DateTime day)
        {
            StreamReader reader;
            try { reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Write("WARN", $"History: {Path.GetFileName(file)} could not be opened: {ex.Message}");
                yield break;
            }
            using (reader)
            {
                int number = 0, bad = 0, firstBad = 0;
                string text;
                while ((text = reader.ReadLine()) != null)
                {
                    number++;
                    if (text.Length == 0 || text[0] == '#') continue;
                    if (TryParse(text, day, out HistoryLine line)) yield return line;
                    else if (bad++ == 0) firstBad = number;
                }
                if (bad > 0)
                    Log.Write("WARN", $"History: {Path.GetFileName(file)} has {bad} unreadable line(s), skipped (first: line {firstBad})");
            }
        }

        private static int NumberCount(char kind) => kind == 'S' ? 8 : kind == 'P' ? 4 : kind == 'D' || kind == 'N' ? 2 : -1;

        private static bool TryParse(string text, DateTime day, out HistoryLine line)
        {
            line = default(HistoryLine);
            string[] p = text.Split(',');
            if (p[0].Length != 1) return false;
            int count = NumberCount(p[0][0]);
            int first = p[0][0] == 'S' ? 2 : 3;
            if (count < 0 || p.Length != first + count) return false;
            if (!TimeSpan.TryParseExact(p[1], "hh\\:mm\\:ss", Inv, out TimeSpan time)) return false;
            var v = new long[count];
            for (int i = 0; i < count; i++)
                if (!long.TryParse(p[first + i], NumberStyles.AllowLeadingSign, Inv, out v[i])) return false;
            line.Kind = p[0][0];
            line.Time = day + time;
            line.Name = first == 3 ? p[2] : null;
            line.V = v;
            return true;
        }
    }
}

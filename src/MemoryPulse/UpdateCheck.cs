using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MemoryPulse
{
    /// <summary>A release on GitHub that is newer than the running version.</summary>
    internal sealed class UpdateInfo
    {
        public string Version;  // "1.2.0"
        public string Url;      // release page
    }

    /// <summary>
    /// Asks GitHub for the number of the latest release. Only this one request is made; nothing about
    /// the PC or its memory is sent. GitHub sees the request's IP address, as with any web request.
    /// </summary>
    internal static class UpdateCheck
    {
        private const string LatestApi = "https://api.github.com/repos/ahmetunaluzun/MemoryPulse/releases/latest";
        private const int TimeoutMs = 15000;

        private static readonly Regex Tag = new Regex(@"""tag_name""\s*:\s*""v?(?<v>\d+\.\d+\.\d+)""", RegexOptions.Compiled);
        private static readonly Regex Page = new Regex(@"""html_url""\s*:\s*""(?<u>https://github\.com/[^""]+/releases/tag/[^""]+)""", RegexOptions.Compiled);

        /// <summary>The newer release, or null when up to date. Throws on network or parse failure.</summary>
        public static Task<UpdateInfo> FindNewerAsync() => Task.Run(() =>
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; // .NET 4.8 may default to older TLS
            var req = (HttpWebRequest)WebRequest.Create(LatestApi);
            req.UserAgent = "MemoryPulse/" + Program.Version; // GitHub rejects requests without one
            req.Accept = "application/vnd.github+json";
            req.Timeout = req.ReadWriteTimeout = TimeoutMs;
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
                json = r.ReadToEnd();
            return Newer(json, Program.Version);
        });

        /// <summary>Parses the GitHub answer; null when its version is not newer than <paramref name="current"/>.</summary>
        public static UpdateInfo Newer(string json, string current)
        {
            var t = Tag.Match(json);
            if (!t.Success) throw new InvalidDataException("No version number in the GitHub answer");
            var latest = new Version(t.Groups["v"].Value);
            if (latest <= new Version(current)) return null;
            var p = Page.Match(json);
            return new UpdateInfo { Version = latest.ToString(3), Url = p.Success ? p.Groups["u"].Value : Program.RepoUrl + "/releases/latest" };
        }
    }
}

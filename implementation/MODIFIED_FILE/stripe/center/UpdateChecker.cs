using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace IvanCenter
{
    /// <summary>更新检查：查询 GitHub 最新 Release 与本地版本比较；任何失败一律静默（不打扰安装流程）</summary>
    internal static class UpdateChecker
    {
        public static readonly string ReleasesPage = "https://github.com/IvanXxxxyyuffff/ivan-rhino-plugins/releases/latest";

        /// <summary>返回 true = 查到比本地更新的版本；tag/url 有效。失败或无更新返回 false。</summary>
        public static bool FindNewer(out string tag, out string url)
        {
            tag = null; url = null;
            try
            {
                const string api = "https://api.github.com/repos/IvanXxxxyyuffff/ivan-rhino-plugins/releases/latest";
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                var req = (HttpWebRequest)WebRequest.Create(api);
                req.UserAgent = "IVAN-CENTER";
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.KeepAlive = false;
                string body;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    body = sr.ReadToEnd();
                Match mt = Regex.Match(body, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                if (!mt.Success) return false;
                string t = mt.Groups[1].Value.Trim();
                if (!IsNewer(t, BuildVersion.Current)) return false;
                Match mu = Regex.Match(body, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                tag = t;
                url = mu.Success ? mu.Groups[1].Value : ReleasesPage;
                return true;
            }
            catch { return false; }
        }

        static int[] ParseVer(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return null;
                s = s.Trim();
                if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
                string[] parts = s.Split('.');
                if (parts.Length < 2) return null;
                int a = int.Parse(parts[0]);
                int b = int.Parse(parts[1]);
                int c = parts.Length > 2 ? int.Parse(parts[2].Split('-')[0]) : 0;
                return new[] { a, b, c };
            }
            catch { return null; }
        }

        public static bool IsNewer(string candidate, string current)
        {
            int[] c = ParseVer(candidate), l = ParseVer(current);
            if (c == null || l == null) return false;
            for (int i = 0; i < 3; i++)
                if (c[i] != l[i]) return c[i] > l[i];
            return false;
        }
    }
}

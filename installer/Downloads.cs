using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace PBZSetup
{
    public sealed class LoaderRelease
    {
        public string Name, Url, Digest;
        public long Size;
        public bool Experimental;
        public override string ToString() { return Name + (Experimental ? "  [实验版]" : "  [正式版]"); }
    }
    public static class Downloads
    {
        public static HttpClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PBZ-Item-Giver-Setup/1.0");
            return client;
        }
        public static async Task<List<LoaderRelease>> Releases()
        {
            using (var client = Client())
            {
                string text = await client.GetStringAsync("https://api.github.com/repos/UE4SS-RE/RE-UE4SS/releases?per_page=10");
                return Parse(text);
            }
        }
        public static List<LoaderRelease> Parse(string text)
        {
            var results = new List<LoaderRelease>();
            var rows = Core.Json.DeserializeObject(text) as object[];
            if (rows == null) throw new IOException("官方版本列表格式无效。");
            foreach (Dictionary<string, object> row in rows)
            {
                if (Convert.ToBoolean(row["draft"])) continue;
                // The historical experimental archive contains hundreds of obsolete builds.
                if (Convert.ToBoolean(row["prerelease"]) && Convert.ToString(row["tag_name"]) != "experimental-latest") continue;
                foreach (Dictionary<string, object> asset in (object[])row["assets"])
                {
                    string name = Convert.ToString(asset["name"]), url = Convert.ToString(asset["browser_download_url"]);
                    if (!name.StartsWith("UE4SS_v", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!url.StartsWith("https://github.com/UE4SS-RE/RE-UE4SS/releases/download/", StringComparison.Ordinal)) continue;
                    results.Add(new LoaderRelease { Name = name, Url = url, Size = Convert.ToInt64(asset["size"]),
                        Experimental = Convert.ToBoolean(row["prerelease"]), Digest = asset.ContainsKey("digest") ? Convert.ToString(asset["digest"]) : "" });
                }
            }
            return results.OrderBy(r => r.Experimental).ToList();
        }
        public static async Task<string> Download(LoaderRelease release, string cache, Action<int> progress)
        {
            if (release.Size <= 0 || release.Size > 256L * 1024 * 1024) throw new IOException("官方下载包大小异常。");
            if (!release.Url.StartsWith("https://github.com/UE4SS-RE/RE-UE4SS/releases/download/", StringComparison.Ordinal)) throw new IOException("仅支持 UE4SS 官方下载地址。");
            Directory.CreateDirectory(cache); Core.NoLinks(cache);
            string file = Core.SafePath(cache, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(release.Name));
            string partial = file + ".partial";
            try
            {
                using (var client = Client()) using (var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var source = await response.Content.ReadAsStreamAsync()) using (var dest = File.Create(partial))
                    {
                        var buffer = new byte[81920]; long total = 0; int count;
                        while ((count = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            total += count; if (total > release.Size) throw new IOException("下载数据超过官方标示大小。");
                            await dest.WriteAsync(buffer, 0, count); progress((int)(total * 100 / release.Size));
                        }
                        if (total != release.Size) throw new IOException("下载不完整，请重试或使用本地 ZIP。");
                    }
                }
                if (!String.IsNullOrEmpty(release.Digest) && release.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals("sha256:" + Core.Hash(partial), release.Digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("官方下载包 SHA256 校验失败。");
                using (var archive = System.IO.Compression.ZipFile.OpenRead(partial)) if (archive.Entries.Count == 0) throw new IOException("官方下载包为空。");
                File.Move(partial, file); return file;
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
    }
}

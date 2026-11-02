using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PBZSetup
{
    public sealed class InstallOptions
    {
        public string Exe, Work, Save, Backups, LoaderZip;
        public bool InstallLoader, InstallMod = true, BackupSave = true;
    }
    public sealed class CheckResult
    {
        public string Name, State, Detail;
        public CheckResult(string name, string state, string detail) { Name = name; State = state; Detail = detail; }
    }
    public sealed class FileChange
    {
        public string Relative, BeforeHash, AfterHash;
        public bool Existed;
    }
    public sealed class InstallRecord
    {
        public string Target, CreatedUtc, Status;
        public List<FileChange> Files = new List<FileChange>();
    }
    public sealed class SaveRecord
    {
        public string Source, CreatedUtc, ArchiveSha256;
        public Dictionary<string, string> Files = new Dictionary<string, string>();
    }
    public static class Core
    {
        public const string ModHash = "3ffbb2727a0eab26a4e90377ee75e6fb9a1370da0757e10a47d21b83b9a33ec3";
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public static readonly string DefaultBackups = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PBZ-Item-Giver-Backups");
        public static string Full(string path) { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        public static bool Within(string path, string root)
        {
            return Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        public static void NoLinks(string path)
        {
            string at = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(at))
            {
                if ((File.Exists(at) || Directory.Exists(at)) && (File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("路径包含符号链接或目录联接，不能安全写入：" + at);
                at = Path.GetDirectoryName(at);
            }
        }
        public static string SafePath(string root, string relative)
        {
            string clean = relative.Replace('/', '\\');
            if (String.IsNullOrWhiteSpace(clean) || Path.IsPathRooted(clean) || clean.Contains(":") || clean.Split('\\').Any(p => p == ".." || p == "."))
                throw new InvalidDataException("压缩包包含不安全路径：" + relative);
            foreach (string segment in clean.Split('\\').Where(p => p.Length > 0))
            {
                if (segment.EndsWith(".") || segment.EndsWith(" ") || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("压缩包包含无效文件名：" + relative);
            }
            string target = Path.GetFullPath(Path.Combine(root, clean));
            if (!Within(target, root)) throw new InvalidDataException("文件路径超出目标目录。");
            NoLinks(target);
            return target;
        }
        public static string Hash(string file)
        {
            using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void WriteJson(string path, object value)
        {
            string temp = path + ".new";
            File.WriteAllText(temp, Json.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static string EmbeddedMod(string staging)
        {
            string path = Path.Combine(staging, "PBZItemGiver-0.5.1.zip");
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZ.Mod.zip"))
            using (var output = File.Create(path)) { if (input == null) throw new IOException("内置模组缺失。"); input.CopyTo(output); }
            if (Hash(path) != ModHash) throw new IOException("内置模组校验失败。");
            return path;
        }
        public static void Extract(string zip, string destination)
        {
            NoLinks(destination); Directory.CreateDirectory(destination);
            using (var archive = ZipFile.OpenRead(zip))
            {
                long total = 0;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (archive.Entries.Count > 20000) throw new InvalidDataException("压缩包条目过多。");
                // Validate the entire archive before extracting even the first entry.
                foreach (var entry in archive.Entries)
                {
                    string target = SafePath(destination, entry.FullName);
                    if (!seen.Add(target)) throw new InvalidDataException("压缩包存在重复路径。");
                    total = checked(total + entry.Length);
                    if (total > 1024L * 1024 * 1024 || entry.Length > 512L * 1024 * 1024)
                        throw new InvalidDataException("压缩包展开大小超出限制。");
                    int kind = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (kind == 0xA000) throw new InvalidDataException("不支持压缩包中的符号链接。");
                }
                foreach (var entry in archive.Entries)
                {
                    string target = SafePath(destination, entry.FullName);
                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open()) using (var output = File.Create(target)) input.CopyTo(output);
                }
            }
        }
        public static List<string> Files(string root)
        {
            NoLinks(root);
            var result = new List<string>();
            var queue = new Queue<string>(); queue.Enqueue(root);
            while (queue.Count > 0)
            {
                string dir = queue.Dequeue(); NoLinks(dir);
                foreach (string file in Directory.GetFiles(dir)) { NoLinks(file); result.Add(file); }
                foreach (string sub in Directory.GetDirectories(dir)) { NoLinks(sub); queue.Enqueue(sub); }
                if (result.Count > 50000) throw new IOException("文件数量超出限制。");
            }
            return result;
        }
        public static string Relative(string root, string file) { return file.Substring(Full(root).Length + 1); }
        public static void ValidateExe(string exe)
        {
            if (!File.Exists(exe) || !Path.GetFileName(exe).ToLowerInvariant().Contains("pbz") || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("请选择名称包含 PBZ 的游戏主程序（优先 Win64-Shipping），不要选择安装器。");
            NoLinks(exe);
            using (var reader = new BinaryReader(File.OpenRead(exe)))
            {
                if (reader.BaseStream.Length < 128 || reader.ReadUInt16() != 0x5A4D) throw new IOException("游戏程序不是有效的 Windows EXE。");
                reader.BaseStream.Position = 0x3C; int offset = reader.ReadInt32();
                if (offset < 64 || offset + 6 > reader.BaseStream.Length) throw new IOException("游戏 EXE 头无效。");
                reader.BaseStream.Position = offset;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new IOException("需要 Windows x64 游戏程序。");
            }
        }
        public static void RequireClosed(string exe)
        {
            foreach (var process in Process.GetProcesses()) using (process)
            {
                try
                {
                    if (process.ProcessName.IndexOf("pbz", StringComparison.OrdinalIgnoreCase) >= 0 && process.Id != Process.GetCurrentProcess().Id)
                        throw new IOException("检测到游戏进程 " + process.ProcessName + "。请先关闭游戏，再备份或安装。");
                }
                catch (InvalidOperationException) { }
            }
        }
        public static List<string> FindGames(string root)
        {
            var found = new List<string>(); var queue = new Queue<Tuple<string, int>>(); queue.Enqueue(Tuple.Create(root, 0));
            int visited = 0;
            while (queue.Count > 0 && visited++ < 400)
            {
                var item = queue.Dequeue();
                try
                {
                    NoLinks(item.Item1);
                    found.AddRange(Directory.GetFiles(item.Item1, "*PBZ*.exe"));
                    if (item.Item2 < 4) foreach (string sub in Directory.GetDirectories(item.Item1))
                    {
                        string name = Path.GetFileName(sub);
                        if (new[] { "Content", "Saved", "Engine", "Mods", "UE4SS", ".git" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                        queue.Enqueue(Tuple.Create(sub, item.Item2 + 1));
                    }
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return found.OrderByDescending(p => p.IndexOf("Shipping", StringComparison.OrdinalIgnoreCase) >= 0).ThenBy(p => p).ToList();
        }
        public static string WorkFor(string exe)
        {
            string dir = Path.GetDirectoryName(Full(exe)), over = Path.Combine(dir, "override.txt");
            if (File.Exists(over))
            {
                string value = File.ReadAllText(over).Trim().Trim('"');
                if (value.Length == 0) throw new IOException("override.txt 为空，请手动指定 UE4SS 目录。");
                string target = Path.GetFullPath(Path.Combine(dir, value));
                return target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(target) : target;
            }
            if (File.Exists(Path.Combine(dir, "ue4ss", "UE4SS.dll"))) return Path.Combine(dir, "ue4ss");
            if (File.Exists(Path.Combine(dir, "UE4SS.dll"))) return dir;
            return Path.Combine(dir, "ue4ss");
        }
        public static string SuggestedWork(string exe, string zip)
        {
            string current = WorkFor(exe);
            if (File.Exists(Path.Combine(current, "UE4SS.dll")) || !File.Exists(zip)) return current;
            using (var archive = ZipFile.OpenRead(zip))
            {
                var dlls = archive.Entries.Where(e => Path.GetFileName(e.FullName.Replace('/', '\\')).Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase)).ToList();
                if (dlls.Count != 1) return current;
                string folder = Path.GetFileName(Path.GetDirectoryName(dlls[0].FullName.Replace('/', '\\')));
                return folder.Equals("ue4ss", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Path.GetDirectoryName(exe), "ue4ss") : Path.GetDirectoryName(exe);
            }
        }
        public static List<string> SaveCandidates(string exe)
        {
            var result = new List<string>();
            result.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectPBZ", "Saved", "SaveGames"));
            string at = Path.GetDirectoryName(exe);
            for (int i = 0; i < 5 && !String.IsNullOrEmpty(at); i++, at = Path.GetDirectoryName(at))
            {
                result.Add(Path.Combine(at, "ProjectPBZ", "Saved", "SaveGames"));
                result.Add(Path.Combine(at, "Saved", "SaveGames"));
            }
            return result.Where(Directory.Exists).Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static void ValidateBackupRoot(string backups, string source, string game)
        {
            if (String.IsNullOrWhiteSpace(backups) || !Path.IsPathRooted(backups)) throw new IOException("请选择完整的备份目录。");
            NoLinks(backups);
            foreach (string forbidden in new[] { source, game }.Where(p => !String.IsNullOrWhiteSpace(p)))
                if (Full(backups) == Full(forbidden) || Within(backups, forbidden) || Within(forbidden, backups))
                    throw new IOException("备份目录必须独立于游戏及存档目录，不能互相包含。");
        }
        public static string BackupSaves(string source, string backups, string exe, Action<string> log)
        {
            RequireClosed(exe);
            if (!Directory.Exists(source)) throw new IOException("未找到存档目录。请选择存档目录；没有存档时可取消勾选存档备份。");
            source = Full(source); NoLinks(source); ValidateBackupRoot(backups, source, Path.GetDirectoryName(exe));
            List<string> files = Files(source);
            if (files.Count == 0) throw new IOException("存档目录为空，未创建备份。请确认位置。");
            string batch = Path.Combine(backups, "Saves", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(batch);
            string partial = Path.Combine(batch, "SaveGames.zip.partial"), zip = Path.Combine(batch, "SaveGames.zip");
            var record = new SaveRecord { Source = source, CreatedUtc = DateTime.UtcNow.ToString("o") };
            using (var archive = ZipFile.Open(partial, ZipArchiveMode.Create))
                foreach (string file in files)
                {
                    string name = Relative(source, file).Replace('\\', '/');
                    string before = Hash(file); archive.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                    if (before != Hash(file)) throw new IOException("备份过程中存档发生变化，请关闭游戏后重试。");
                    record.Files.Add(name, before);
                }
            using (var archive = ZipFile.OpenRead(partial))
                foreach (var entry in archive.Entries) using (var stream = entry.Open()) using (var sha = SHA256.Create())
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (record.Files[entry.FullName] != hash) throw new IOException("存档备份校验失败。");
                }
            var afterFiles = Files(source);
            if (!new HashSet<string>(files, StringComparer.OrdinalIgnoreCase).SetEquals(afterFiles)) throw new IOException("备份期间存档文件列表发生变化，请重试。");
            foreach (string file in afterFiles)
                if (Hash(file) != record.Files[Relative(source, file).Replace('\\', '/')]) throw new IOException("备份期间存档内容发生变化，请重试。");
            RequireClosed(exe); File.Move(partial, zip); record.ArchiveSha256 = Hash(zip);
            WriteJson(Path.Combine(batch, "manifest.json"), record);
            log("存档备份完成并通过 SHA256 校验：" + zip); return zip;
        }
        public static string LoaderRoot(string extracted)
        {
            var candidates = Files(extracted).Where(p => Path.GetFileName(p).Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count != 1) throw new IOException("UE4SS ZIP 应包含唯一 UE4SS.dll，请选择官方运行版完整压缩包。");
            string dllDir = Path.GetDirectoryName(candidates[0]);
            string root = Path.GetFileName(dllDir).Equals("ue4ss", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(dllDir) : dllDir;
            if (!File.Exists(Path.Combine(root, "dwmapi.dll"))) throw new IOException("UE4SS 包缺少 dwmapi.dll，仅支持使用该代理的标准运行版。");
            if (!File.Exists(Path.Combine(dllDir, "UE4SS-settings.ini"))) throw new IOException("UE4SS 包缺少配置文件。");
            return root;
        }
        public static void ValidateOptions(InstallOptions o)
        {
            ValidateExe(o.Exe); RequireClosed(o.Exe);
            if (String.IsNullOrWhiteSpace(o.Work) || !Path.IsPathRooted(o.Work)) throw new IOException("请指定完整的 UE4SS 工作目录。");
            NoLinks(o.Work); ValidateBackupRoot(o.Backups, o.Save, Path.GetDirectoryName(o.Exe));
            if (!o.InstallLoader && !o.InstallMod) throw new IOException("请至少选择一项安装内容。");
            if (o.InstallLoader && !File.Exists(o.LoaderZip)) throw new IOException("请先选择或下载 UE4SS ZIP。");
            if (o.BackupSave && !Directory.Exists(o.Save)) throw new IOException("未找到存档目录，请选择目录；尚无存档时请明确取消勾选存档备份。");
        }
        public static string Install(InstallOptions o, Action<string> log, Action<int> progress)
        {
            ValidateOptions(o);
            string stage = Path.Combine(Path.GetTempPath(), "PBZSetup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            try
            {
                var writes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string gameDir = Full(Path.GetDirectoryName(o.Exe));
                string targetRoot = gameDir;
                string work = Full(o.Work);
                if (o.InstallLoader)
                {
                    if (File.Exists(Path.Combine(gameDir, "override.txt"))) throw new IOException("检测到自定义 override.txt。请保留现有适配环境并仅安装模组，或先手动处理 UE4SS 安装。");
                    string extracted = Path.Combine(stage, "loader"); Extract(o.LoaderZip, extracted); string loader = LoaderRoot(extracted);
                    bool modern = File.Exists(Path.Combine(loader, "ue4ss", "UE4SS.dll"));
                    string packageWork = modern ? Path.Combine(gameDir, "ue4ss") : gameDir;
                    if (File.Exists(Path.Combine(gameDir, modern ? "UE4SS.dll" : "ue4ss\\UE4SS.dll")))
                        throw new IOException("UE4SS 新旧目录布局冲突，请使用与已有环境相同布局的包，或仅安装模组。");
                    if (!Full(packageWork).Equals(work, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("该 UE4SS 包的工作目录为 " + packageWork + "。请修改 UE4SS 目录后重新安装。");
                    foreach (string file in Files(loader))
                    {
                        string rel = Relative(loader, file); string dest = SafePath(gameDir, rel);
                        if (File.Exists(dest) && (Path.GetFileName(dest).Equals("UE4SS-settings.ini", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(dest).Equals("mods.txt", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(dest).Equals("enabled.txt", StringComparison.OrdinalIgnoreCase))) continue;
                        // Loader updates must not overwrite unrelated installed mods.
                        if (File.Exists(dest) && rel.Replace('\\', '/').IndexOf("Mods/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        writes[dest] = file;
                    }
                }
                else if (!File.Exists(Path.Combine(work, "UE4SS.dll"))) throw new IOException("指定目录中没有 UE4SS.dll。请先安装 UE4SS，或选择正确目录。");
                if (o.InstallMod)
                {
                    string extracted = Path.Combine(stage, "mod"); Extract(EmbeddedMod(stage), extracted);
                    foreach (string file in Files(Path.Combine(extracted, "Mods")))
                        writes[SafePath(work, Path.Combine("Mods", Relative(Path.Combine(extracted, "Mods"), file)))] = file;
                    string list = Path.Combine(work, "Mods", "mods.txt");
                    if (File.Exists(list) || writes.ContainsKey(list))
                    {
                        string original = File.ReadAllText(File.Exists(list) ? list : writes[list]);
                        string updated = Regex.IsMatch(original, @"^\s*PBZItemGiver\s*:", RegexOptions.Multiline)
                            ? Regex.Replace(original, @"^\s*PBZItemGiver\s*:[^\r\n]*", "PBZItemGiver : 1", RegexOptions.Multiline)
                            : original.TrimEnd() + Environment.NewLine + "PBZItemGiver : 1" + Environment.NewLine;
                        string listStage = Path.Combine(stage, "mods.txt"); File.WriteAllText(listStage, updated, new UTF8Encoding(false)); writes[list] = listStage;
                    }
                }
                if (!Within(work, gameDir) && !work.Equals(gameDir, StringComparison.OrdinalIgnoreCase))
                {
                    if (o.InstallLoader) throw new IOException("自动安装 UE4SS 仅支持游戏程序目录。");
                    targetRoot = work;
                }
                foreach (string dest in writes.Keys) if (!Within(dest, targetRoot)) throw new IOException("安装目标超出预期目录。");
                progress(20);
                if (o.BackupSave) BackupSaves(o.Save, o.Backups, o.Exe, log); else log("用户选择跳过存档备份。");
                RequireClosed(o.Exe);
                string snapshot = Path.Combine(o.Backups, "Installations", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                using (var tx = new InstallTransaction(targetRoot, snapshot))
                {
                    int i = 0;
                    foreach (var entry in writes.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        RequireClosed(o.Exe); tx.Put(entry.Value, entry.Key); progress(25 + (++i * 65 / Math.Max(1, writes.Count)));
                    }
                    tx.Commit(); log("安装快照：" + snapshot);
                }
                foreach (var entry in writes) if (Hash(entry.Key) != Hash(entry.Value)) throw new IOException("安装后文件核对失败：" + entry.Key);
                progress(100); log("文件安装完成。请正常启动游戏、载入存档后按 F7 验证；文件检查不能证明游戏兼容性。");
                return snapshot;
            }
            finally { CleanupStage(stage); }
        }
        public static void CleanupStage(string stage)
        {
            // Remove only individual files inside the unique staging directory, never recursive delete.
            try { foreach (string f in Files(stage)) { if (Within(f, stage)) File.Delete(f); } } catch { }
        }
        public static List<CheckResult> Check(InstallOptions o)
        {
            var result = new List<CheckResult>();
            try { ValidateExe(o.Exe); result.Add(new CheckResult("游戏主程序", "通过", o.Exe)); }
            catch (Exception ex) { result.Add(new CheckResult("游戏主程序", "未通过", ex.Message)); return result; }
            try { RequireClosed(o.Exe); result.Add(new CheckResult("游戏运行状态", "通过", "当前未检测到 PBZ 游戏进程")); }
            catch (Exception ex) { result.Add(new CheckResult("游戏运行状态", "未通过", ex.Message)); }
            string work = o.Work;
            if (String.IsNullOrWhiteSpace(work) || !Path.IsPathRooted(work)) { result.Add(new CheckResult("UE4SS 目录", "未通过", "请指定完整路径")); return result; }
            result.Add(new CheckResult("UE4SS 加载器", File.Exists(Path.Combine(work, "UE4SS.dll")) ? "通过" : "缺失", Path.Combine(work, "UE4SS.dll")));
            bool proxy = File.Exists(Path.Combine(Path.GetDirectoryName(o.Exe), "dwmapi.dll"));
            result.Add(new CheckResult("代理加载文件", proxy ? "通过" : "需确认", proxy ? "已找到 dwmapi.dll" : "未发现 dwmapi.dll；若使用其他注入方式，请手动确认"));
            string mod = Path.Combine(work, "Mods", "PBZItemGiver");
            string stage = Path.Combine(Path.GetTempPath(), "PBZSetup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            try
            {
                string unpacked = Path.Combine(stage, "mod"); Extract(EmbeddedMod(stage), unpacked);
                int missing = 0, different = 0;
                foreach (string source in Files(Path.Combine(unpacked, "Mods")))
                {
                    string dest = Path.Combine(work, "Mods", Relative(Path.Combine(unpacked, "Mods"), source));
                    if (!File.Exists(dest)) missing++; else if (Hash(dest) != Hash(source)) different++;
                }
                result.Add(new CheckResult("模组及菜单框架", missing == 0 && different == 0 ? "通过" : "需处理", "缺失 " + missing + " 项，区别于内置 0.5.1 包 " + different + " 项（自定义修改也会被标记）"));
            }
            finally { CleanupStage(stage); }
            string modsTxt = Path.Combine(work, "Mods", "mods.txt");
            bool disabled = File.Exists(modsTxt) && Regex.IsMatch(File.ReadAllText(modsTxt), @"^\s*PBZItemGiver\s*:\s*0\b", RegexOptions.Multiline);
            result.Add(new CheckResult("模组启用状态", File.Exists(Path.Combine(mod, "enabled.txt")) && !disabled ? "通过" : "需处理", disabled ? "mods.txt 明确禁用了 PBZItemGiver" : "检查 enabled.txt 与 mods.txt"));
            result.Add(new CheckResult("存档位置", Directory.Exists(o.Save) ? "已找到" : "未找到", o.Save ?? "请手动选择存档目录"));
            try { ValidateBackupRoot(o.Backups, o.Save, Path.GetDirectoryName(o.Exe)); result.Add(new CheckResult("备份目录", "通过", o.Backups)); }
            catch (Exception ex) { result.Add(new CheckResult("备份目录", "未通过", ex.Message)); }
            string log = Path.Combine(work, "UE4SS.log");
            if (File.Exists(log))
            {
                string tail;
                using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                { stream.Position = Math.Max(0, stream.Length - 256 * 1024); using (var reader = new StreamReader(stream)) tail = reader.ReadToEnd(); }
                bool loaded = tail.Contains("[PBZItemGiver] 0.5.1 loaded");
                result.Add(new CheckResult("历史日志线索", loaded ? "发现记录" : "未发现", "日志时间 " + File.GetLastWriteTime(log).ToString("yyyy-MM-dd HH:mm") + "；" + (loaded ? "包含 0.5.1 加载信息；历史记录不代表本次运行状态" : "最近日志中没有找到模组加载信息")));
            }
            else result.Add(new CheckResult("历史日志线索", "尚无日志", "首次安装后请启动游戏，再检查 UE4SS.log"));
            return result;
        }
    }
    public sealed class InstallTransaction : IDisposable
    {
        private readonly string snapshot, manifest;
        private readonly InstallRecord record;
        private bool finished;
        public InstallTransaction(string target, string snapshotPath)
        {
            Core.NoLinks(target); Core.NoLinks(snapshotPath); snapshot = snapshotPath;
            if (Core.Within(snapshotPath, target) || Core.Within(target, snapshotPath) || Core.Full(snapshotPath) == Core.Full(target)) throw new IOException("安装快照目录必须独立于目标。");
            Directory.CreateDirectory(Path.Combine(snapshot, "files")); manifest = Path.Combine(snapshot, "manifest.json");
            record = new InstallRecord { Target = Core.Full(target), CreatedUtc = DateTime.UtcNow.ToString("o"), Status = "pending" };
            Core.WriteJson(manifest, record);
        }
        public void Put(string source, string destination)
        {
            if (!Core.Within(destination, record.Target)) throw new IOException("安装文件越界。");
            Core.NoLinks(destination);
            string relative = Core.Relative(record.Target, destination);
            var change = new FileChange { Relative = relative, Existed = File.Exists(destination), AfterHash = Core.Hash(source) };
            if (record.Files.Any(f => f.Relative.Equals(relative, StringComparison.OrdinalIgnoreCase))) throw new IOException("重复写入目标。");
            if (change.Existed)
            {
                change.BeforeHash = Core.Hash(destination);
                string backup = Core.SafePath(Path.Combine(snapshot, "files"), relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(destination, backup);
                if (Core.Hash(backup) != change.BeforeHash) throw new IOException("旧文件备份校验失败。");
            }
            record.Files.Add(change); Core.WriteJson(manifest, record); // Persist journal before write.
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temp = destination + ".pbz-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temp);
                if (Core.Hash(temp) != change.AfterHash) throw new IOException("文件复制校验失败。");
                if (change.Existed && (!File.Exists(destination) || Core.Hash(destination) != change.BeforeHash)) throw new IOException("安装期间目标文件发生变化，已停止。");
                if (!change.Existed && File.Exists(destination)) throw new IOException("安装期间出现了同名文件，已停止。");
                if (change.Existed) File.Replace(temp, destination, null); else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Commit() { record.Status = "complete"; Core.WriteJson(manifest, record); finished = true; }
        public void Dispose()
        {
            if (finished) return;
            try { Rollback(snapshot); } catch (Exception ex) { throw new IOException("安装失败且未完全回退。请保留安装快照并查看日志。" + ex.Message, ex); }
        }
        public static void Rollback(string snapshot)
        {
            Core.NoLinks(snapshot);
            string manifest = Path.Combine(snapshot, "manifest.json");
            var record = Core.Json.Deserialize<InstallRecord>(File.ReadAllText(manifest));
            if (record.Status != "complete" && record.Status != "pending") throw new IOException("该快照已回退或不是有效安装快照。");
            // Preflight all files before changing anything; do not overwrite later user edits.
            foreach (var f in record.Files)
            {
                string dest = Core.SafePath(record.Target, f.Relative);
                if (File.Exists(dest) && Core.Hash(dest) != f.AfterHash && (!f.Existed || Core.Hash(dest) != f.BeforeHash))
                    throw new IOException("安装后文件已变化，无法自动回退：" + dest);
                if (f.Existed)
                {
                    string backup = Core.SafePath(Path.Combine(snapshot, "files"), f.Relative);
                    if (!File.Exists(backup) || Core.Hash(backup) != f.BeforeHash) throw new IOException("快照文件缺失或校验失败。");
                }
            }
            foreach (var f in record.Files.AsEnumerable().Reverse())
            {
                string dest = Core.SafePath(record.Target, f.Relative);
                if (f.Existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(Core.SafePath(Path.Combine(snapshot, "files"), f.Relative), dest, true);
                }
                else if (File.Exists(dest)) File.Delete(dest);
            }
            record.Status = "rolled-back"; Core.WriteJson(manifest, record);
        }
    }
}

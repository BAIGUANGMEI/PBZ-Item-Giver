using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PBZSetup
{
    // Isolated filesystem integration tests. Never scans or modifies a real game installation.
    public static class SelfTests
    {
        static readonly List<string> results = new List<string>();
        static string root;
        static int failures;
        static void Test(string name, Action action)
        {
            try { action(); results.Add("PASS " + name); }
            catch (Exception ex) { failures++; results.Add("FAIL " + name + " :: " + ex); }
        }
        static void Assert(bool value, string why) { if (!value) throw new Exception(why); }
        static void Reject(Action action)
        {
            bool rejected = false; try { action(); } catch { rejected = true; }
            Assert(rejected, "Expected refusal");
        }
        static string Dir(string name) { string path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
        static void Write(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, value, new UTF8Encoding(false)); }
        static void Exe(string path)
        {
            var bytes = new byte[256]; bytes[0] = 0x4D; bytes[1] = 0x5A; bytes[0x3C] = 0x80;
            bytes[0x80] = 0x50; bytes[0x81] = 0x45; bytes[0x84] = 0x64; bytes[0x85] = 0x86;
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes);
        }
        static string Zip(string name, params string[] entries)
        {
            string zip = Path.Combine(root, name + ".zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (string entry in entries) using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("fixture:" + entry);
            return zip;
        }
        static InstallOptions Options(string name, bool existing)
        {
            string game = Dir(name + "-game"); string exe = Path.Combine(game, "PBZ-Win64-Shipping.exe"); Exe(exe);
            string work = Path.Combine(game, "ue4ss"); if (existing) Write(Path.Combine(work, "UE4SS.dll"), "existing-adapted-loader");
            string saves = Dir(name + "-saves"); Write(Path.Combine(saves, "Steam", "fixture-user", "Progress.sav"), "save-content");
            return new InstallOptions { Exe = exe, Work = work, Save = saves, Backups = Path.Combine(root, name + "-backups"), BackupSave = true };
        }
        public static async Task<int> Network(string output)
        {
            Directory.CreateDirectory(output);
            try
            {
                var releases = await Downloads.Releases(); var release = releases.First(r => !r.Experimental);
                string zip = await Downloads.Download(release, output, i => { });
                string stage = Path.Combine(output, "unpacked-" + Guid.NewGuid().ToString("N")); Core.Extract(zip, stage);
                string loaderRoot = Core.LoaderRoot(stage);
                File.WriteAllText(Path.Combine(output, "network-results.txt"), "PASS live official API and download\r\nPackage: " + release.Name + "\r\nBytes: " + new FileInfo(zip).Length + "\r\nSHA256: " + Core.Hash(zip) + "\r\nPASS archive layout validation. No package code executed or installed.\r\n", Encoding.UTF8);
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "network-results.txt"), "FAIL " + ex.Message, Encoding.UTF8); return 1; }
        }
        public static int Run(string output)
        {
            string baseDir = Path.GetFullPath(output); Directory.CreateDirectory(baseDir); root = Path.Combine(baseDir, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            Test("Embedded mod SHA256 matches original archive", () => Assert(Core.Hash(Core.EmbeddedMod(Dir("embedded"))) == Core.ModHash, "hash"));
            Test("Reject parent traversal before extracting any file", () =>
            {
                string dest = Dir("slip"); Reject(() => Core.Extract(Zip("slip", "good.txt", "../outside.txt"), dest));
                Assert(!File.Exists(Path.Combine(dest, "good.txt")) && !File.Exists(Path.Combine(root, "outside.txt")), "Partial archive extraction");
            });
            Test("Reject backslash traversal", () => Reject(() => Core.Extract(Zip("backslash", "a\\..\\escape"), Dir("backslash"))));
            Test("Reject absolute paths", () => Reject(() => Core.Extract(Zip("absolute", "C:/outside.txt"), Dir("absolute"))));
            Test("Reject alternate data streams", () => Reject(() => Core.Extract(Zip("ads", "file.txt:stream"), Dir("ads"))));
            Test("Reject DOS device filenames", () => Reject(() => Core.Extract(Zip("device", "NUL.txt"), Dir("device"))));
            Test("Reject trailing-dot aliases", () => Reject(() => Core.Extract(Zip("dot", "a. /file"), Dir("dot"))));
            Test("Reject case-insensitive duplicate entries", () => Reject(() => Core.Extract(Zip("duplicates", "A.txt", "a.txt"), Dir("duplicates"))));
            Test("Reject Unix symlink archive entry", () =>
            {
                string file = Zip("symlink", "link"); using (var archive = ZipFile.Open(file, ZipArchiveMode.Update)) archive.Entries[0].ExternalAttributes = unchecked((int)0xA1FF0000);
                Reject(() => Core.Extract(file, Dir("symlink")));
            });
            Test("Reject non-PBZ executable", () => { string p = Path.Combine(Dir("wrong"), "Other.exe"); Exe(p); Reject(() => Core.ValidateExe(p)); });
            Test("Reject invalid PE executable", () => { string p = Path.Combine(Dir("badpe"), "PBZ.exe"); Write(p, "not-exe"); Reject(() => Core.ValidateExe(p)); });
            Test("Recognize valid x64 PBZ fixture", () => { var o = Options("exe", true); Core.ValidateExe(o.Exe); });
            Test("Detect shipping game executable below selected root", () => { var o = Options("detect", true); Assert(Core.FindGames(Path.GetDirectoryName(o.Exe)).Contains(o.Exe), "not found"); });
            Test("Resolve modern loader layout", () => { var o = Options("modern", true); Assert(Core.WorkFor(o.Exe) == o.Work, "wrong work"); });
            Test("Resolve legacy loader layout", () => { var o = Options("legacy", false); Write(Path.Combine(Path.GetDirectoryName(o.Exe), "UE4SS.dll"), "old"); Assert(Core.WorkFor(o.Exe) == Path.GetDirectoryName(o.Exe), "wrong work"); });
            Test("Resolve override.txt relative path", () => { var o = Options("override", false); Write(Path.Combine(Path.GetDirectoryName(o.Exe), "override.txt"), "custom-loader/UE4SS.dll"); Assert(Core.WorkFor(o.Exe) == Path.Combine(Path.GetDirectoryName(o.Exe), "custom-loader"), "wrong override"); });
            Test("Suggest fresh legacy install directory from ZIP layout", () => { var o = Options("suggest", false); string zip = Zip("suggest-legacy", "UE4SS.dll", "dwmapi.dll", "UE4SS-settings.ini"); Assert(Core.SuggestedWork(o.Exe, zip) == Path.GetDirectoryName(o.Exe), "wrong fresh work directory"); });
            Test("Reject backup under save tree", () => { var o = Options("nested", true); Reject(() => Core.ValidateBackupRoot(Path.Combine(o.Save, "backups"), o.Save, Path.GetDirectoryName(o.Exe))); });
            Test("Reject backup parent containing game", () => { var o = Options("parent", true); Reject(() => Core.ValidateBackupRoot(root, o.Save, Path.GetDirectoryName(o.Exe))); });
            Test("Backup save files and verify manifest hashes", () =>
            {
                var o = Options("save", true); string zip = Core.BackupSaves(o.Save, o.Backups, o.Exe, s => { });
                var record = Core.Json.Deserialize<SaveRecord>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(zip), "manifest.json")));
                Assert(record.ArchiveSha256 == Core.Hash(zip) && record.Files.Count == 1, "manifest mismatch");
                using (var archive = ZipFile.OpenRead(zip)) Assert(archive.Entries.Single().FullName == "Steam/fixture-user/Progress.sav", "relative path");
            });
            Test("Reject missing save directory", () => { var o = Options("nosave", true); Reject(() => Core.BackupSaves(Path.Combine(root, "absent"), o.Backups, o.Exe, s => { })); });
            Test("Reject empty save directory", () => { var o = Options("empty", true); Reject(() => Core.BackupSaves(Dir("empty-dir"), o.Backups, o.Exe, s => { })); });
            Test("Rollback modified and newly installed files", () =>
            {
                string target = Dir("tx-game"), snap = Path.Combine(root, "tx-snap"), old = Path.Combine(target, "old.dll"), add = Path.Combine(target, "new.dll"), src = Path.Combine(Dir("tx-stage"), "new");
                Write(old, "before"); Write(src, "after");
                using (var tx = new InstallTransaction(target, snap)) { tx.Put(src, old); tx.Put(src, add); tx.Commit(); }
                InstallTransaction.Rollback(snap); Assert(File.ReadAllText(old) == "before" && !File.Exists(add), "rollback mismatch");
            });
            Test("Automatic rollback on interrupted transaction", () =>
            {
                string target = Dir("auto-game"), old = Path.Combine(target, "old"), src = Path.Combine(Dir("auto-stage"), "new"); Write(old, "before"); Write(src, "after");
                Reject(() => { using (var tx = new InstallTransaction(target, Path.Combine(root, "auto-snap"))) { tx.Put(src, old); tx.Put(Path.Combine(root, "missing-source"), Path.Combine(target, "second")); } });
                Assert(File.ReadAllText(old) == "before", "not restored");
            });
            Test("Rollback refuses files edited after installation", () =>
            {
                string target = Dir("edited-game"), file = Path.Combine(target, "a"), src = Path.Combine(Dir("edited-stage"), "a"), snap = Path.Combine(root, "edited-snap"); Write(src, "installed");
                using (var tx = new InstallTransaction(target, snap)) { tx.Put(src, file); tx.Commit(); } Write(file, "user change");
                Reject(() => InstallTransaction.Rollback(snap)); Assert(File.ReadAllText(file) == "user change", "edit lost");
            });
            Test("Transaction rejects target outside selected root", () =>
            {
                string src = Path.Combine(Dir("escape-stage"), "a"); Write(src, "a");
                Reject(() => { using (var tx = new InstallTransaction(Dir("escape-game"), Path.Combine(root, "escape-snap"))) tx.Put(src, Path.Combine(root, "outside-target")); });
            });
            Test("Install embedded mod, backup saves, preserve unrelated mods, verify files", () =>
            {
                var o = Options("install", true); Write(Path.Combine(o.Work, "Mods", "mods.txt"), "Other : 1\nPBZItemGiver : 0\n"); Write(Path.Combine(o.Work, "Mods", "Other", "keep.lua"), "keep");
                string snap = Core.Install(o, s => { }, i => { });
                Assert(File.Exists(Path.Combine(o.Work, "Mods", "PBZItemGiver", "Scripts", "main.lua")), "mod missing");
                Assert(File.ReadAllText(Path.Combine(o.Work, "Mods", "Other", "keep.lua")) == "keep", "unrelated mod changed");
                Assert(File.ReadAllText(Path.Combine(o.Work, "Mods", "mods.txt")).Contains("PBZItemGiver : 1"), "disabled entry");
                Assert(Core.Check(o).Single(r => r.Name == "模组及菜单框架").State == "通过", "integrity check failed");
                InstallTransaction.Rollback(snap); Assert(!File.Exists(Path.Combine(o.Work, "Mods", "PBZItemGiver", "Scripts", "main.lua")), "new file retained");
                Assert(File.ReadAllText(Path.Combine(o.Work, "Mods", "mods.txt")).Contains("PBZItemGiver : 0"), "mods list not restored");
            });
            Test("Missing loader blocks mod-only install", () => { var o = Options("missingloader", false); Reject(() => Core.Install(o, s => { }, i => { })); });
            Test("Explicitly skipping saves allows fresh game install", () => { var o = Options("skipsave", true); o.Save = ""; o.BackupSave = false; Core.Install(o, s => { }, i => { }); });
            Test("Install modern UE4SS fixture and mod together", () =>
            {
                var o = Options("loaderinstall", false); o.InstallLoader = true;
                o.LoaderZip = Zip("modern-package", "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini", "ue4ss/Mods/mods.txt");
                Core.Install(o, s => { }, i => { }); Assert(File.Exists(Path.Combine(o.Work, "UE4SS.dll")), "loader missing");
            });
            Test("Preserve existing UE4SS configuration on update", () =>
            {
                var o = Options("preserve", true); o.InstallLoader = true; string config = Path.Combine(o.Work, "UE4SS-settings.ini"); Write(config, "custom-adaptation=keep");
                o.LoaderZip = Zip("preserve-package", "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini"); Core.Install(o, s => { }, i => { }); Assert(File.ReadAllText(config) == "custom-adaptation=keep", "config overwritten");
            });
            Test("Refuse mixed legacy and modern layout before modifying files", () =>
            {
                var o = Options("mixed", false); Write(Path.Combine(Path.GetDirectoryName(o.Exe), "UE4SS.dll"), "legacy"); o.InstallLoader = true;
                o.LoaderZip = Zip("mixed-package", "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini"); Reject(() => Core.Install(o, s => { }, i => { })); Assert(!File.Exists(Path.Combine(Path.GetDirectoryName(o.Exe), "dwmapi.dll")), "partial install");
            });
            Test("Refuse unsupported loader package", () => { string dir = Dir("unsupported"); Core.Extract(Zip("unsupported", "UE4SS.dll"), dir); Reject(() => Core.LoaderRoot(dir)); });
            Test("Official release parser excludes development and external packages", () =>
            {
                string json = "[{\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"UE4SS_v3.zip\",\"size\":123,\"browser_download_url\":\"https://github.com/UE4SS-RE/RE-UE4SS/releases/download/v3/a.zip\"},{\"name\":\"zDEV-UE4SS_v3.zip\",\"size\":123,\"browser_download_url\":\"https://github.com/UE4SS-RE/RE-UE4SS/releases/download/v3/b.zip\"},{\"name\":\"UE4SS_v4.zip\",\"size\":123,\"browser_download_url\":\"https://example.com/b.zip\"}]}]";
                Assert(Downloads.Parse(json).Count == 1, "unsafe downloads listed");
            });
            results.Add("RESULT " + (results.Count - failures) + " passed, " + failures + " failed. Fixtures only; no game launched or real saves modified.");
            File.WriteAllLines(Path.Combine(baseDir, "results.txt"), results, new UTF8Encoding(true)); return failures == 0 ? 0 : 1;
        }
    }
}

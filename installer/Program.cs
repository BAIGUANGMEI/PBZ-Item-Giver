using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PBZSetup
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--self-test") return SelfTests.Run(args[1]);
            if (args.Length == 2 && args[0] == "--network-test") return SelfTests.Network(args[1]).GetAwaiter().GetResult();
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "发生错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm()); return 0;
        }
    }
    public sealed class MainForm : Form
    {
        readonly Color ink = Color.FromArgb(26, 47, 46), muted = Color.FromArgb(94, 112, 112), accent = Color.FromArgb(21, 112, 99);
        readonly TextBox exe = new TextBox(), work = new TextBox(), save = new TextBox(), backups = new TextBox(), loader = new TextBox();
        readonly CheckBox backupSave = new CheckBox(), installLoader = new CheckBox(), installMod = new CheckBox();
        readonly ComboBox versions = new ComboBox();
        readonly ListView checks = new ListView();
        readonly RichTextBox log = new RichTextBox();
        readonly ProgressBar progress = new ProgressBar();
        readonly Label status = new Label();
        readonly TabControl tabs = new TabControl();
        readonly Button install = new Button();
        readonly List<Control> actions = new List<Control>();
        bool busy;
        public MainForm()
        {
            Text = "藏物阁 · PBZ Item Giver 安装器";
            Font = new Font("Microsoft YaHei UI", 9F); ForeColor = ink; BackColor = Color.FromArgb(243, 246, 244);
            ClientSize = new Size(1060, 860); MinimumSize = new Size(1000, 800); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24, 18, 24, 16) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 106)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); Controls.Add(shell);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "藏物阁 / 安装助手", Font = new Font(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
            header.Controls.Add(new Label { Text = "影之刃零 · PBZ Item Giver 0.5.1     选择路径 → 检查环境 → 备份并安装", ForeColor = muted, AutoSize = true, Location = new Point(3, 49) });
            var help = new LinkLabel { Text = "使用说明 ↗", Dock = DockStyle.Right, Width = 110, TextAlign = ContentAlignment.TopRight, LinkColor = accent };
            help.LinkClicked += (s, e) => OpenUrl("https://github.com/BAIGUANGMEI/PBZ-Item-Giver#readme"); header.Controls.Add(help); shell.Controls.Add(header, 0, 0);
            tabs.Dock = DockStyle.Fill; tabs.Padding = new Point(20, 8); tabs.AccessibleName = "安装器功能"; shell.Controls.Add(tabs, 0, 1);
            BuildInstall(); BuildChecks(); BuildBackups();
            log.Dock = DockStyle.Fill; log.ReadOnly = true; log.AccessibleName = "操作日志"; log.BackColor = Color.FromArgb(233, 240, 236); log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Microsoft YaHei UI", 9); log.Margin = new Padding(4, 14, 4, 4); shell.Controls.Add(log, 0, 2);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            status.Text = "就绪 · 尚未修改游戏文件"; status.AutoSize = true; status.ForeColor = muted; status.Anchor = AnchorStyles.Left;
            progress.Dock = DockStyle.Fill; progress.Margin = new Padding(5, 10, 0, 9); footer.Controls.Add(status); footer.Controls.Add(progress); shell.Controls.Add(footer, 0, 3);
            backups.Text = Core.DefaultBackups;
            Log("内置模组 0.5.1。请选择实际游戏主程序，安装前关闭游戏。检查操作只读；安装时自动备份被替换文件。");
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; MessageBox.Show(this, "任务正在进行，请等待完成后关闭窗口。", "任务进行中"); } };
        }
        TableLayoutPanel Page(string title)
        {
            var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(16) }; tabs.TabPages.Add(page);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true }; panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); page.Controls.Add(panel); return panel;
        }
        void Row(TableLayoutPanel page, Control control, int height)
        {
            int index = page.RowCount++; page.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); control.Dock = DockStyle.Fill; control.Margin = Padding.Empty; page.Controls.Add(control, 0, index);
        }
        Label Hint(string text) { return new Label { Text = text, AutoSize = false, ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft }; }
        Label Title(string text) { return new Label { Text = text, Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }; }
        Button Btn(string text, EventHandler click)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 32, MinimumSize = new Size(96, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 2, 4, 2), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(199, 215, 208); button.Click += click; actions.Add(button); return button;
        }
        Control PathRow(string title, TextBox box, EventHandler browse, string browseText)
        {
            var panel = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            panel.Controls.Add(Hint(title), 0, 0); box.Dock = DockStyle.Fill; box.Margin = new Padding(4, 6, 8, 4); panel.Controls.Add(box, 1, 0);
            panel.Controls.Add(Btn(browseText, browse), 2, 0); actions.Add(box); return panel;
        }
        void BuildInstall()
        {
            var page = Page("安装");
            Row(page, Title("01  选择游戏与存档"), 28);
            Row(page, PathRow("游戏主程序", exe, (s, e) => ChooseExe(), "选择 EXE"), 36);
            var detect = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight };
            detect.Controls.Add(Btn("扫描游戏文件夹", (s, e) => ScanFolder()));
            detect.Controls.Add(Hint("优先选择 PBZ 的 Win64-Shipping 主程序，避免选到启动器。")); detect.Controls[1].Width = 650; Row(page, detect, 36);
            Row(page, PathRow("UE4SS 工作目录", work, (s, e) => ChooseFolder(work), "选择目录"), 36);
            Row(page, PathRow("游戏存档目录", save, (s, e) => ChooseFolder(save), "选择目录"), 36);
            Row(page, PathRow("备份保存位置", backups, (s, e) => ChooseFolder(backups), "选择目录"), 36);
            backupSave.Text = "安装前备份存档（默认开启；尚无存档时可取消）"; backupSave.Checked = true; actions.Add(backupSave); Row(page, backupSave, 28);
            Row(page, Title("02  选择安装内容"), 28);
            installMod.Text = "安装 / 更新 PBZ Item Giver 0.5.1（已内置模组与 ModMenu）"; installMod.Checked = true; actions.Add(installMod); Row(page, installMod, 28);
            installLoader.Text = "安装 / 更新 UE4SS（已有适配环境时建议保留，仅安装模组）"; actions.Add(installLoader); Row(page, installLoader, 28);
            Row(page, PathRow("UE4SS 本地 ZIP", loader, (s, e) => { using (var d = new OpenFileDialog { Filter = "UE4SS ZIP|*.zip" }) if (d.ShowDialog(this) == DialogResult.OK) SetLoader(d.FileName); }, "选择 ZIP"), 36);
            var download = new TableLayoutPanel { ColumnCount = 3 };
            download.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156)); download.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); download.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            download.Controls.Add(Btn("获取官方版本列表", async (s, e) => await FetchVersions()), 0, 0);
            versions.DropDownStyle = ComboBoxStyle.DropDownList; versions.Dock = DockStyle.Fill; versions.Margin = new Padding(4, 7, 4, 2); actions.Add(versions); download.Controls.Add(versions, 1, 0);
            download.Controls.Add(Btn("下载选中版本", async (s, e) => await DownloadVersion()), 2, 0); Row(page, download, 36);
            Row(page, Hint("支持标准运行版的 dwmapi.dll 加载方式。官方通用包仍需适配本作；不会自动选择实验版或修改适配配置。"), 30);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight };
            buttons.Controls.Add(Btn("检查环境", async (s, e) => await RunCheck()));
            install.Text = "备份并安装"; install.Size = new Size(162, 36); install.FlatStyle = FlatStyle.Flat; install.BackColor = accent; install.ForeColor = Color.White;
            install.Click += async (s, e) => await RunInstall(); actions.Add(install); buttons.Controls.Add(install);
            buttons.Controls.Add(Btn("打开备份目录", (s, e) => OpenFolder(backups.Text)));
            // Keep primary actions visible even on a short screen or high DPI display.
            var tabPage = (TabPage)page.Parent;
            tabPage.Controls.Remove(page);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.Controls.Add(page, 0, 0); buttons.Dock = DockStyle.Fill; buttons.Padding = new Padding(0, 8, 0, 0); layout.Controls.Add(buttons, 0, 1); tabPage.Controls.Add(layout);
        }
        void BuildChecks()
        {
            var page = Page("环境检查");
            Row(page, Hint("检查主程序、游戏运行状态、UE4SS、模组文件校验、启用状态和历史日志。不会启动游戏。"), 44);
            checks.View = View.Details; checks.FullRowSelect = true; checks.GridLines = false; checks.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            checks.Columns.Add("检查项目", 155); checks.Columns.Add("状态", 85); checks.Columns.Add("详细信息", 640); checks.Dock = DockStyle.Fill;
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); int row = page.RowCount++; page.Controls.Add(checks, 0, row);
            var bar = new FlowLayoutPanel(); bar.Controls.Add(Btn("重新检查", async (s, e) => await RunCheck()));
            bar.Controls.Add(Btn("打开 UE4SS 目录", (s, e) => OpenFolder(work.Text)));
            bar.Controls.Add(Btn("导出操作日志", (s, e) => ExportLog())); Row(page, bar, 46);
        }
        void BuildBackups()
        {
            var page = Page("备份与回退");
            Row(page, Title("存档备份"), 38);
            Row(page, Hint("备份整个所选存档目录，保留相对路径，并核对每个文件及 ZIP 的 SHA256。安装器不会解析或修改存档内容。"), 56);
            var saveBar = new FlowLayoutPanel();
            saveBar.Controls.Add(Btn("立即备份存档", async (s, e) => await RunBackup())); saveBar.Controls.Add(Btn("打开备份目录", (s, e) => OpenFolder(backups.Text))); Row(page, saveBar, 48);
            Row(page, Hint("备份位于 Saves 子目录。恢复存档时请先关闭游戏，保留当前存档副本，再将 ZIP 中的文件解压回 manifest.json 记录的原目录。Steam 云同步需要自行核对。"), 68);
            Row(page, Title("安装文件回退"), 38);
            Row(page, Hint("每次安装都在 Installations 子目录保留文件快照。回退会还原旧文件，并移除这次安装新增的文件；不会恢复存档或撤销游戏内物品变化。"), 66);
            Row(page, Hint("请按时间选择与当前游戏对应的快照，按从新到旧的顺序回退。如果文件在安装后已被修改，自动回退会停止，保留文件供你处理。"), 60);
            var restoreBar = new FlowLayoutPanel(); restoreBar.Controls.Add(Btn("选择安装快照并回退…", async (s, e) => await Rollback())); Row(page, restoreBar, 48);
            Row(page, Hint("备份和操作日志仅保存在本机，不上传存档。关闭窗口前可导出日志。"), 40);
        }
        void ChooseExe()
        {
            using (var dialog = new OpenFileDialog { Filter = "游戏程序（PBZ）|*PBZ*.exe|所有 EXE|*.exe", Title = "选择 PBZ 实际游戏主程序" })
                if (dialog.ShowDialog(this) == DialogResult.OK) SetExe(dialog.FileName);
        }
        void SetExe(string path)
        {
            exe.Text = path;
            try
            {
                work.Text = Core.SuggestedWork(path, loader.Text); var candidates = Core.SaveCandidates(path);
                if (candidates.Count == 1) { save.Text = candidates[0]; Log("发现存档目录：" + candidates[0]); }
                else if (candidates.Count > 1) { save.Text = ""; Log("发现多个可能的存档目录，请手动选择：" + String.Join("；", candidates)); }
                else { save.Text = ""; Log("未发现存档目录。请手动选择；尚未开始游戏时可取消勾选存档备份。"); }
            }
            catch (Exception ex) { ShowError(ex); }
        }
        void SetLoader(string path)
        {
            loader.Text = path;
            if (File.Exists(exe.Text))
            {
                work.Text = Core.SuggestedWork(exe.Text, path);
                Log("已根据现有环境或安装包布局选择 UE4SS 工作目录：" + work.Text);
            }
        }
        void ChooseFolder(TextBox field)
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择目录", ShowNewFolderButton = true })
            { if (Directory.Exists(field.Text)) dialog.SelectedPath = field.Text; if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.SelectedPath; }
        }
        async void ScanFolder()
        {
            string root;
            using (var dialog = new FolderBrowserDialog { Description = "选择游戏安装目录（最多扫描 4 层）" })
            { if (dialog.ShowDialog(this) != DialogResult.OK) return; root = dialog.SelectedPath; }
            await Run(async () =>
            {
                List<string> found = await Task.Run(() => Core.FindGames(root));
                if (found.Count == 0) throw new IOException("没有找到 PBZ 游戏程序，请手动选择 EXE。");
                if (found.Count == 1) SetExe(found[0]);
                else
                {
                    using (var dialog = new Form { Text = "选择实际游戏主程序", Size = new Size(860, 330), StartPosition = FormStartPosition.CenterParent, Font = Font })
                    {
                        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true }; list.Items.AddRange(found.Cast<object>().ToArray()); list.SelectedIndex = 0;
                        var ok = new Button { Text = "使用选中程序", Dock = DockStyle.Bottom, Height = 42, DialogResult = DialogResult.OK }; dialog.Controls.Add(list); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                        if (dialog.ShowDialog(this) == DialogResult.OK) SetExe((string)list.SelectedItem);
                    }
                }
            });
        }
        InstallOptions Options()
        {
            return new InstallOptions { Exe = exe.Text.Trim(), Work = work.Text.Trim(), Save = save.Text.Trim(), Backups = backups.Text.Trim(), LoaderZip = loader.Text.Trim(), InstallLoader = installLoader.Checked, InstallMod = installMod.Checked, BackupSave = backupSave.Checked };
        }
        async Task Run(Func<Task> action)
        {
            if (busy) return; busy = true; actions.ForEach(c => c.Enabled = false); progress.Value = 0; status.Text = "任务进行中…"; UseWaitCursor = true;
            try { await action(); status.Text = "任务完成 · 请查看日志"; }
            catch (Exception ex) { status.Text = "任务未完成 · 请查看提示"; ShowError(ex); }
            finally { busy = false; actions.ForEach(c => c.Enabled = true); UseWaitCursor = false; }
        }
        void Log(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), text); return; }
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); log.SelectionStart = log.TextLength; log.ScrollToCaret();
        }
        void Progress(int value) { if (InvokeRequired) { BeginInvoke(new Action<int>(Progress), value); return; } progress.Value = Math.Max(0, Math.Min(100, value)); }
        void ShowError(Exception ex) { Log("未完成：" + ex.Message); MessageBox.Show(this, ex.Message, "无法完成操作", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        async Task RunCheck()
        {
            var options = Options();
            await Run(async () =>
            {
                var results = await Task.Run(() => Core.Check(options)); checks.Items.Clear();
                foreach (var r in results)
                {
                    var item = new ListViewItem(new[] { r.Name, r.State, r.Detail });
                    if (r.State == "通过") item.ForeColor = accent;
                    else if (r.State == "未通过" || r.State == "缺失" || r.State == "需处理") item.ForeColor = Color.FromArgb(164, 76, 39);
                    checks.Items.Add(item); Log(r.Name + " / " + r.State + "：" + r.Detail);
                }
                tabs.SelectedIndex = 1; Progress(100);
            });
        }
        async Task RunInstall()
        {
            var options = Options();
            try { Core.ValidateOptions(options); } catch (Exception ex) { ShowError(ex); return; }
            string summary = "游戏程序：" + options.Exe + "\nUE4SS 目录：" + options.Work + "\n内容：" + (options.InstallLoader ? "UE4SS " : "") + (options.InstallMod ? "物品模组 0.5.1" : "") +
                "\n存档备份：" + (options.BackupSave ? options.Save : "已选择跳过") + "\n备份目录：" + options.Backups + "\n\n将备份并替换相关文件（包括共享 ModMenu）。安装不代表已验证游戏兼容性。是否继续？";
            if (MessageBox.Show(this, summary, "确认安装", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            await Run(async () => { await Task.Run(() => Core.Install(options, Log, Progress)); MessageBox.Show(this, "文件安装完成。\n请启动游戏进入存档，按 F7 验证。", "安装完成"); });
        }
        async Task RunBackup()
        {
            var options = Options();
            await Run(async () => { Core.ValidateExe(options.Exe); await Task.Run(() => Core.BackupSaves(options.Save, options.Backups, options.Exe, Log)); Progress(100); });
        }
        async Task FetchVersions()
        {
            await Run(async () => { var releases = await Downloads.Releases(); versions.Items.Clear(); versions.Items.AddRange(releases.Cast<object>().ToArray()); versions.SelectedIndex = -1; Log("已获取 " + releases.Count + " 个官方包，请手动选择与本作适配的版本。最新版本不保证适配本作。"); });
        }
        async Task DownloadVersion()
        {
            var release = versions.SelectedItem as LoaderRelease;
            if (release == null) { MessageBox.Show(this, "请先获取列表并选择版本。"); return; }
            if (release.Experimental && MessageBox.Show(this, "选中的是 UE4SS 实验版，请确认它适用于当前游戏版本。继续下载？", "实验版本", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            await Run(async () =>
            {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBZ-Item-Giver-Setup", "Downloads");
                SetLoader(await Downloads.Download(release, cache, Progress));
                Log("下载完成：" + release.Name + (String.IsNullOrEmpty(release.Digest) ? "。官方未提供摘要；已核对下载长度和 ZIP 格式。" : "。已核对官方下载 SHA256。"));
                Log("下载不等于安装。勾选安装 UE4SS 后，确认工作目录与压缩包布局一致再执行安装。");
            });
        }
        async Task Rollback()
        {
            string file;
            using (var dialog = new OpenFileDialog { Title = "选择 Installations 快照中的 manifest.json", Filter = "安装快照|manifest.json", InitialDirectory = Path.Combine(backups.Text, "Installations") })
            { if (dialog.ShowDialog(this) != DialogResult.OK) return; file = dialog.FileName; }
            var options = Options();
            try
            {
                Core.ValidateExe(options.Exe); Core.RequireClosed(options.Exe);
                var record = Core.Json.Deserialize<InstallRecord>(File.ReadAllText(file));
                string gameDir = Core.Full(Path.GetDirectoryName(options.Exe));
                if (!Core.Full(record.Target).Equals(gameDir, StringComparison.OrdinalIgnoreCase) && !Core.Full(record.Target).Equals(Core.Full(options.Work), StringComparison.OrdinalIgnoreCase)) throw new IOException("快照与当前选择的游戏 / UE4SS 目录不匹配。");
                if (MessageBox.Show(this, "目标：" + record.Target + "\n文件数：" + record.Files.Count + "\n创建时间（UTC）：" + record.CreatedUtc + "\n\n还原旧文件并移除该次新增文件，不恢复存档。确认回退？", "确认回退", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            }
            catch (Exception ex) { ShowError(ex); return; }
            await Run(async () => { await Task.Run(() => { Core.RequireClosed(options.Exe); InstallTransaction.Rollback(Path.GetDirectoryName(file)); }); Log("安装文件已回退，存档未修改。"); Progress(100); });
        }
        void ExportLog()
        {
            using (var d = new SaveFileDialog { Filter = "文本日志|*.txt", FileName = "PBZ-Setup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" })
                if (d.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(d.FileName, log.Text, new System.Text.UTF8Encoding(true)); Log("日志已导出到：" + d.FileName); }
        }
        void OpenFolder(string path)
        {
            try { if (!Directory.Exists(path)) throw new IOException("目录尚不存在，请先完成相应操作。"); Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + Path.GetFullPath(path) + "\"", UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); }
        }
        void OpenUrl(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    }
}

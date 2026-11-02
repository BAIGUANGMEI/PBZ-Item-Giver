# 藏物阁 · Windows 图形安装器

面向 Windows 10 / 11 x64 的中文安装助手，使用 C#、WinForms 和系统 .NET Framework，内置原始 `PBZItemGiver-0.5.1.zip`。生成的 EXE 可单独分发，玩家无需安装 Python、Node.js 或 .NET SDK；系统需具备 .NET Framework 4.5 或更高版本。

## 下载

- [独立 EXE](https://github.com/BAIGUANGMEI/PBZ-Item-Giver/releases/download/v0.5.1/PBZ-Item-Giver-Setup-0.5.1.exe)
- [完整安装包 ZIP（含 EXE、使用说明和 EXE 校验值）](https://github.com/BAIGUANGMEI/PBZ-Item-Giver/releases/download/v0.5.1/PBZ-Item-Giver-Setup-0.5.1.zip)
- [安装器源码包（含构建所需的原始模组 ZIP）](https://github.com/BAIGUANGMEI/PBZ-Item-Giver/releases/download/v0.5.1/PBZ-Item-Giver-Setup-0.5.1-source.zip)
- [Release 附件 SHA256 校验清单](https://github.com/BAIGUANGMEI/PBZ-Item-Giver/releases/download/v0.5.1/SHA256SUMS.txt)

## 使用

1. 运行 `PBZ-Item-Giver-Setup-0.5.1.exe`，先关闭游戏。
2. 点击「选择 EXE」选择实际 PBZ 游戏主程序，优先选择 `Win64-Shipping` 程序；也可扫描游戏安装目录后选择。不要选 Steam 启动器或本安装器。
3. 核对 UE4SS 工作目录。安装器识别现有 `ue4ss/UE4SS.dll`、旧式同目录布局及 `override.txt`。手动自定义的加载参数、多 Mods 目录等需要自行确认。
4. 核对存档目录。自动查找游戏附近及 `%LOCALAPPDATA%/ProjectPBZ/Saved/SaveGames`；如果发现多个目录，则要求手动选择，不会猜测正在使用哪一份。备份包含所选目录中的所有账号子目录。
5. 指定独立的备份位置，默认是「文档 / PBZ-Item-Giver-Backups」。备份目录不能与游戏程序目录、UE4SS 安装目标或存档目录互相包含。安装前默认备份存档；尚无存档时可以明确取消勾选。
6. 选择安装内容。已有能正常工作的本作 UE4SS 适配环境时，通常仅需安装模组。
7. 如需 UE4SS，可选择本地标准运行版 ZIP，或点击「获取官方版本列表」后手动选择并下载。列表区分正式版与实验版，排除开发包和历史实验归档。不会自动选择版本，也不会因为版本较新就认定适用于本作。
8. 点击「检查环境」阅读检查结果，然后回到「安装」页，点击「备份并安装」核对目标后确认。
9. 安装后正常启动游戏，载入存档，按 F7 手动确认模组工作。

初次安装时，选择 UE4SS ZIP 后会根据包的实际布局建议工作目录。包的布局与已有 UE4SS 冲突时停止安装；存在自定义 `override.txt` 时不自动更新加载器，可继续仅安装模组。安装器仅支持带 `dwmapi.dll` 的标准完整运行包，不会下载或运行安装脚本。

## 备份与回退

- **存档备份**：可单独执行。保存在 `Saves/<时间与随机编号>/SaveGames.zip`，同目录 `manifest.json` 包含原始目录、文件摘要和 ZIP 摘要。备份时检查文件集合和内容是否变化，完成后重新校验压缩包。
- **安装快照**：每次安装在 `Installations/<时间与随机编号>/` 保存旧文件及修改清单。即使跳过存档备份，也会保留安装文件快照。
- **失败回退**：文件替换失败时尝试按日志回退。如果进程被强制结束，可以选择相应 `pending` 快照回退。若文件已被其他程序修改，则停止自动回退，保留快照供处理。
- **手动回退**：在「备份与回退」页选择对应的 `manifest.json`，核对目标后确认。多次安装请从最新快照开始逐次回退。只还原旧文件、移除该次新增文件，不会删除其他模组；允许保留空目录。
- **恢复存档**：关闭游戏，先另存当前存档，再将备份 ZIP 的内容解压到清单记录的原目录。安装器不自动恢复存档，不处理 Steam 云同步冲突。

安装文件回退不会撤销游戏中的物品或任务进度。备份、下载缓存和日志仅保存在本机；联网功能只请求 GitHub 官方 UE4SS 发布信息与运行包，不上传存档。

## 检查范围

检查 PBZ x64 程序头、游戏是否运行、加载器及代理文件、内置模组和共享菜单文件摘要、启用标记、`mods.txt`、存档及备份路径，以及最近的模组加载日志线索。自定义文件被标记为与内置包不同，不代表一定损坏。

这些是文件和环境检查，不能证明游戏版本兼容、UE4SS 已成功注入，或实际增删物品正确。日志会注明其更新时间，历史加载记录不能代表本次运行成功。

已有 `UE4SS-settings.ini`、`mods.txt` 和其他模组的既有文件在加载器更新时保留。安装模组时会替换内置的 `PBZItemGiver` 与共享 `ModMenu` 对应文件，原文件进入快照；如果有 `mods.txt`，仅启用或追加 `PBZItemGiver` 条目。

安装器默认以普通用户权限运行。若游戏目录不允许写入，会报错并尝试回退；需要由使用者确认目录权限后重试。当前构建未做数字签名。

## 构建与验证

在 Windows PowerShell 中进入仓库目录执行：

```powershell
.\installer\build.ps1 -Test
```

使用系统自带 .NET Framework C# 编译器，无 NuGet 依赖。输出到 `dist/PBZ-Item-Giver-Setup-0.5.1.exe`，内置模组 SHA256 固定校验为：

```text
3ffbb2727a0eab26a4e90377ee75e6fb9a1370da0757e10a47d21b83b9a33ec3
```

34 项隔离文件系统测试覆盖 ZIP 越界、设备名、重复条目、符号链接、备份校验、安装回退、修改冲突、布局识别、配置保留及完整模组安装。测试夹具和结果保存在 `installer/test-output/`，不写真实游戏目录或存档。

另已验证真实 GitHub API 请求、官方 `UE4SS_v3.0.1.zip` 下载与压缩包布局检查，未执行或安装下载包；该下载验证不代表该版本已适配本作。已进行 Windows 原生窗口显示、页面切换和只读环境检查的界面验证。未启动游戏或实机验证模组。

联网验证为可选开发命令，仅下载到给定目录并检查文件：

```powershell
.\dist\PBZ-Item-Giver-Setup-0.5.1.exe --network-test .\installer\test-output\network
```

参考：[UE4SS 官方下载](https://github.com/UE4SS-RE/RE-UE4SS/releases)、[官方目录与加载说明](https://docs.ue4ss.com/dev/installation-guide.html)。

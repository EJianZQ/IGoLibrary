# Windows 绿色版自动更新发布说明

## 支持范围

- 仅支持 Windows 10/11 x64 绿色版
- macOS 仍由用户前往 GitHub Release 页面手动下载
- Windows ARM64、Inno 安装器版和增量更新不在当前范围；Release 不提供安装器资产
- 版本号只接受不带前导零的稳定版 `N.N.N`，Git tag 使用 `vN.N.N`
- 第一版包含 `IGoLibrary.Ex.Updater.exe`、`update-manifest.json` 与 `portable-release.marker` 的绿色版必须由用户手动安装一次

## 发布命令

发布机必须是 Windows x64，并安装：

- .NET 10 SDK
- Visual Studio 2026 或 Build Tools 的“使用 C++ 的桌面开发”工作负载
- 对应的 MSVC x64 linker/runtime libraries 与 Windows 10/11 SDK

脚本会在清理任何旧产物之前检查 `vcvarsall.bat`、x64 `link.exe`、`libcmt.lib`、`kernel32.lib` 和 `ucrt.lib`，缺失时给出可操作错误。准备完成后，在仓库根目录使用 PowerShell 7 执行：

发布门禁还需要上一稳定版的 managed updater 做一次真实迁移测试。默认路径为已忽略的 `artifacts/validation/managed-updater-baseline/IGoLibrary.Ex.Updater.exe`；也可以通过 `-ManagedUpdaterBaselinePath` 指向其它受信任的上一版 EXE。该文件不会进入仓库或发布包，缺失时脚本会在清理旧产物前停止。

```powershell
.\build\publish-windows.ps1 -Configuration Release -AppVersion 1.0.1
```

显式指定迁移基线时：

```powershell
.\build\publish-windows.ps1 -Configuration Release -AppVersion 1.0.1 -ManagedUpdaterBaselinePath D:\release-baseline\IGoLibrary.Ex.Updater.exe
```

脚本只执行一次 Desktop 和一次 Updater 的 `dotnet publish`，固定生成两个 ZIP：

```text
artifacts/windows/win-x64/IGoLibrary-Ex-v1.0.1-windows-x64.zip
artifacts/windows/win-x64/IGoLibrary-Ex-v1.0.1-windows-x64-with-cloudflared.zip
```

- 无后缀 ZIP 是不含整个根级 `tools` 目录的轻量包，也是唯一的应用内自动更新资产
- `-with-cloudflared` ZIP 是供用户手动下载的完整包，仅在 manifest 管理文件之外增加 `tools/cloudflared/cloudflared.exe`、`LICENSE.txt` 和 `THIRD-PARTY-NOTICES.txt`
- `artifacts/publish/win-x64` 最终保留包含 cloudflared 的完整发布树，供 Inno Setup 等后续打包入口复用
- Native AOT 生成的全部 PDB 不进入 ZIP，按版本归档到 `artifacts/symbols/win-x64/v1.0.1/`
- 第三方许可证和声明只随完整包提供；轻量包不携带 `tools` 或这些声明

脚本会依次完成：

1. 预检 Windows x64 Native AOT 工具链
2. 安全清理 `artifacts/publish/win-x64` 与 updater 临时发布目录
3. 发布自包含 Desktop
4. 以 Native AOT、`OptimizationPreference=Size`、ILLink/ILC 警告视为错误的方式发布 updater
5. 验证 updater 只有原生 EXE、版本资源正确且原始大小不超过 20 MiB，并执行无界面启动与真实 Win32 TaskDialog smoke
6. 直接运行发布出来的 AOT EXE，完成提交、显式回滚、包/manifest 篡改、apply 后恢复、健康成功、进程崩溃、无法启动、60 秒健康超时和 managed→AOT→AOT 共 9 条进程级验收
7. 准备并校验固定版本的 cloudflared、许可证和第三方声明
8. 写入固定内容的 `portable-release.marker`，并生成 UTF-8 无 BOM 的 `update-manifest.json`；manifest 大小写不敏感地排除整个根级 `tools/**`
9. 从完整发布树组装不含 `tools` 的轻量 staging，使用 `CompressionLevel.SmallestSize` 分别创建两个临时 ZIP
10. 成对验证两个 ZIP 的文件集合、大小、SHA-256、清单顺序及版本；两个 manifest 必须逐字节一致，updater 条目压缩后不得超过 10 MiB，且不得携带 updater PDB/map/obj/托管 sidecar
11. 全部验证通过后，才把两个 ZIP 与本次 AOT PDB 符号目录作为同一组产物替换；任一步失败都会恢复上一组 ZIP/符号

脚本校验失败时不得上传其中任何一个 ZIP

## 单独验证已有 ZIP

```powershell
.\build\verify-windows-package.ps1 -PackagePath .\artifacts\windows\win-x64\IGoLibrary-Ex-v1.0.1-windows-x64.zip -CompanionPackagePath .\artifacts\windows\win-x64\IGoLibrary-Ex-v1.0.1-windows-x64-with-cloudflared.zip
```

验证器会拒绝轻量包中的任何 `tools` 条目，完整包也只允许三个固定的 cloudflared 文件；可执行文件按 `cloudflared-assets.json` 校验 SHA-256，许可证和声明与仓库源文件逐字节比对。两个包都必须且只能在根目录包含一个名称精确的 `IGoLibrary.Ex.Updater.exe`，其 manifest 大小和 SHA-256、FileVersion/ProductVersion、20 MiB 原始大小及 10 MiB 压缩大小门槛均会被验证。

## Updater 运行时与界面

- `IGoLibrary.Ex.Updater.exe` 是 `win-x64` Native AOT 原生程序，不携带 `.runtimeconfig.json`、`.deps.json` 或托管 DLL
- 普通协调器使用系统 Common Controls v6 `TaskDialogIndirect`，显示 marquee 进度并同步刷新状态；任务完成前关闭、Esc 和 Alt+F4 均被拒绝
- 失败界面提供“前往 GitHub”和“关闭”，允许关闭按钮、窗口关闭、Esc 和 Alt+F4；浏览器启动失败时会直接显示可复制的 Release 地址并保持窗口打开
- TaskDialog 创建失败时更新任务尚未启动，并由 `MessageBoxW` 给出兜底错误
- `--bootstrap`、`--worker`、`--recover`、`--recover-worker` 和 `--cleanup` 仍为完全无界面模式；只有现有 bootstrap 流程可以请求 UAC
- 应用 manifest 固定为 `asInvoker`、`uiAccess=false`、PerMonitorV2 DPI、Windows 10 compatibility

## GitHub Release 契约

- Git tag 必须使用稳定版形式 `v1.0.1`，不接受 beta、rc 或其他预发布后缀
- 应用内更新资产名必须精确为 `IGoLibrary-Ex-v1.0.1-windows-x64.zip`
- 同一 Release 可以同时上传 `IGoLibrary-Ex-v1.0.1-windows-x64-with-cloudflared.zip`，但应用不会选择、下载或安装这个完整包
- 资产必须处于 `uploaded` 状态
- GitHub API 必须返回非零 `size` 和 `sha256:<64位十六进制>` digest
- 应用只接受该仓库 HTTPS Release 下载路径

上传后，将 GitHub API 展示的资产大小和 digest 与发布脚本输出再次核对。若 GitHub 尚未提供 digest，应用会保留更新说明与 GitHub 入口，但不会显示“下载并安装”按钮

## `tools` 独立更新域

根级 `tools` 与其全部后代不属于主程序更新包的所有权范围，路径比较不区分大小写；`tools-old`、`my-tools` 和 `subdir/tools` 不匹配。目标更新包的 ZIP 或 manifest 只要出现该保留路径就会在目录交换前失败。

验证当前安装目录时，旧 manifest 中可能存在的 `tools/**` 条目会被忽略，不再校验其存在性、大小或哈希。构造候选目录时会原样保留当前整个 `tools/**`，因此用户自行替换 cloudflared 或添加工具不会阻止主程序更新，也不会被轻量更新覆盖或删除。安装目录原本没有 `tools` 时，更新不会创建空目录。任何位置出现符号链接、联接或其它 reparse point 仍会中止更新。

已确认没有向用户分发 2026-07-14 之后的未打标签自动更新构建，正式标签也早于当前 updater。因此下一正式版本可直接作为轻量自动更新基线，不需要两阶段过渡发布。

## 发布前验收

```powershell
dotnet test .\IGoLibrary-Ex.sln
dotnet test .\tests\IGoLibrary.Ex.Updater.Tests\IGoLibrary.Ex.Updater.Tests.csproj -c Release
```

发布后进程级验收项目 `tests/IGoLibrary.Ex.Updater.AcceptanceTests` 不加入跨平台主解决方案，由 `publish-windows.ps1` 在 AOT EXE 生成后自动传入发布路径与 managed 基线并运行；不得用普通托管构建替代这一步。

还应至少人工验证：

- 100%、150%、200% DPI，以及浅色、深色、高对比度和键盘操作
- 更新期间关闭、Esc、Alt+F4 均无法误关，状态文字持续刷新，失败按钮和 GitHub 跳转正常
- 可写绿色目录的正常升级
- 自定义文件保留、旧 manifest 独有程序文件删除
- 从完整包安装后执行轻量更新，确认 `tools` 内容、大小和哈希保持不变
- 从轻量包安装后执行轻量更新，确认不会凭空创建 `tools`
- 旧 manifest 曾声明 cloudflared、用户又替换或新增 `tools` 文件时，确认更新仍成功且文件保持不变
- 目标 ZIP 或 manifest 携带 `tools/**` 时，确认在安装目录发生任何变化前失败
- 新版本无法启动时自动回滚并重新启动旧版本
- ACL 保护目录的 UAC 接受与取消
- UAC 路径由安装目录中已通过当前 manifest 校验的 updater 启动 bootstrap；普通协调器和普通 worker 不得自行 `runas`
- 在 UAC bootstrap 与主程序之间使用绑定到 bootstrap PID 的命名管道传递可信事务，ZIP 在复制完成前保持不可写，管理员 worker 只读取受保护工作目录
- 模拟在“旧目录已改名、候选目录尚未落位”时中断，确认下次登录可从持久恢复入口还原旧版
- 四种活动任务均阻止进入安装
- 断网、摘要错误、空间不足和旧进程退出超时不修改安装目录
- 更新后的应用和更新协调器以普通权限运行
- 当前 managed updater 能安装包含 Native AOT updater 的新包，Native AOT updater 能继续安装下一版包
- macOS 仍只显示 GitHub 下载入口

## 故障排查

下载、GitHub SHA-256 摘要校验、安全解压、`update-manifest.json` 校验、当前安装包复核、磁盘空间检查、UAC 决策和更新交接都发生在主进程中，并写入当前配置的应用日志目录下的 `app-*.log`。每次尝试会记录当前版本、目标版本、资源名、事务目录和失败阶段；失败记录包含异常与堆栈，同时 `Activity.Update` 会写入与用户通知一致的结果摘要。旧 ZIP 缺少清单时，日志会明确记录 `缺少必需的更新清单文件：update-manifest.json`

普通 coordinator/worker 日志位于：

```text
%LOCALAPPDATA%\IGoLibrary-Ex\updates\logs
```

提权 worker 为避免管理员进程写入可由普通用户重定向的日志路径，只在受保护事务目录内写临时日志，并随安全清理一同删除。日志不包含 Cookie、Token 或业务请求内容。不要手动删除存在活动 PID、非终态 `worker-status.json`、`.IGoLibrary-Ex.secure-*` 工作目录或对应登录恢复项的事务；它可能是断电后还原旧版所需的恢复证据

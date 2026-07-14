# Windows 绿色版自动更新发布说明

## 支持范围

- 仅支持 Windows 10/11 x64 绿色版
- macOS 仍由用户前往 GitHub Release 页面手动下载
- Windows ARM64、Inno 安装器版和增量更新不在当前范围；Release 不提供安装器资产
- 版本号只接受不带前导零的稳定版 `N.N.N`，Git tag 使用 `vN.N.N`
- 第一版包含 `IGoLibrary.Ex.Updater.exe`、`update-manifest.json` 与 `portable-release.marker` 的绿色版必须由用户手动安装一次

## 发布命令

在仓库根目录使用 PowerShell 7 执行：

```powershell
.\build\publish-windows.ps1 -Configuration Release -AppVersion 1.0.1
```

脚本固定生成：

```text
artifacts/windows/win-x64/IGoLibrary-Ex-v1.0.1-windows-x64.zip
```

脚本会依次完成：

1. 安全清理 `artifacts/publish/win-x64` 与 updater 临时发布目录
2. 发布自包含 Desktop
3. 发布 win-x64、自包含、单文件、不裁剪的 updater
4. 将 updater 放入 Desktop 发布根目录
5. 写入固定内容的 `portable-release.marker`，并生成 UTF-8 无 BOM 的 `update-manifest.json`
6. 创建 ZIP
7. 重新打开 ZIP，验证文件集合、大小、SHA-256、清单顺序及版本
8. 输出 ZIP 大小与 SHA-256

脚本校验失败时不得上传该 ZIP

## 单独验证已有 ZIP

```powershell
.\build\verify-windows-package.ps1 `
  -PackagePath .\artifacts\windows\win-x64\IGoLibrary-Ex-v1.0.1-windows-x64.zip
```

## GitHub Release 契约

- Git tag 必须使用稳定版形式 `v1.0.1`，不接受 beta、rc 或其他预发布后缀
- 资产名必须精确为 `IGoLibrary-Ex-v1.0.1-windows-x64.zip`
- 资产必须处于 `uploaded` 状态
- GitHub API 必须返回非零 `size` 和 `sha256:<64位十六进制>` digest
- 应用只接受该仓库 HTTPS Release 下载路径

上传后，将 GitHub API 展示的资产大小和 digest 与发布脚本输出再次核对。若 GitHub 尚未提供 digest，应用会保留更新说明与 GitHub 入口，但不会显示“下载并安装”按钮

## 发布前验收

```powershell
dotnet test .\IGoLibrary-Ex.sln
```

还应至少人工验证：

- 可写绿色目录的正常升级
- 自定义文件保留、旧 manifest 独有程序文件删除
- 新版本无法启动时自动回滚并重新启动旧版本
- ACL 保护目录的 UAC 接受与取消
- UAC 路径由安装目录中已通过当前 manifest 校验的 updater 启动 bootstrap；普通协调器和普通 worker 不得自行 `runas`
- 在 UAC bootstrap 与主程序之间使用绑定到 bootstrap PID 的命名管道传递可信事务，ZIP 在复制完成前保持不可写，管理员 worker 只读取受保护工作目录
- 模拟在“旧目录已改名、候选目录尚未落位”时中断，确认下次登录可从持久恢复入口还原旧版
- 四种活动任务均阻止进入安装
- 断网、摘要错误、空间不足和旧进程退出超时不修改安装目录
- 更新后的应用和更新协调器以普通权限运行
- macOS 仍只显示 GitHub 下载入口

## 故障排查

下载、GitHub SHA-256 摘要校验、安全解压、`update-manifest.json` 校验、当前安装包复核、磁盘空间检查、UAC 决策和更新交接都发生在主进程中，并写入当前配置的应用日志目录下的 `app-*.log`。每次尝试会记录当前版本、目标版本、资源名、事务目录和失败阶段；失败记录包含异常与堆栈，同时 `Activity.Update` 会写入与用户通知一致的结果摘要。旧 ZIP 缺少清单时，日志会明确记录 `缺少必需的更新清单文件：update-manifest.json`

下载、GitHub SHA-256 摘要校验、安全解压、`update-manifest.json` 校验、当前安装包复核、磁盘空间检查、UAC 决策和更新交接都发生在主进程中，并写入当前配置的应用日志目录下的 `app-*.log`。每次尝试会记录当前版本、目标版本、资源名、事务目录和失败阶段；失败记录包含异常与堆栈，同时 `Activity.Update` 会写入与用户通知一致的结果摘要。旧 ZIP 缺少清单时，日志会明确记录 `缺少必需的更新清单文件：update-manifest.json`

普通 coordinator/worker 日志位于：

```text
%LOCALAPPDATA%\IGoLibrary-Ex\updates\logs
```

提权 worker 为避免管理员进程写入可由普通用户重定向的日志路径，只在受保护事务目录内写临时日志，并随安全清理一同删除。日志不包含 Cookie、Token 或业务请求内容。不要手动删除存在活动 PID、非终态 `worker-status.json`、`.IGoLibrary-Ex.secure-*` 工作目录或对应登录恢复项的事务；它可能是断电后还原旧版所需的恢复证据

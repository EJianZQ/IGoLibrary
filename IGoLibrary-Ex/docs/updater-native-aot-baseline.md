# Updater Native AOT 迁移基线

基线采集日期：2026-07-14。基线产物是改造前的 WinForms、自包含 managed updater；二进制仅保留在已忽略的本地 `artifacts` 目录用于迁移验证，不提交仓库。

## 体积基线

| 指标 | 改造前数值 |
| --- | ---: |
| `IGoLibrary.Ex.Updater.exe` 原始大小 | 116,109,999 bytes（110.73 MiB） |
| 轻量 ZIP 中 updater 的 `CompressedLength` | 47,294,555 bytes（45.10 MiB） |
| 轻量 ZIP 总大小 | 115,547,344 bytes |
| updater 压缩条目占轻量 ZIP | 40.93% |

发布硬门槛为 EXE 不超过 20 MiB、ZIP 条目不超过 10 MiB；两种 Windows ZIP 必须同时满足。

## Native AOT 实测结果

最终验证日期：2026-07-15，版本资源为 `1.0.0.0 / 1.0.0`。

本节体积与 SHA-256 均为发布包命名切换前的历史测量：当时无后缀 ZIP 指轻量包，带 cloudflared 的完整包使用旧后缀。数值仅用于 Native AOT 回归比较，不代表当前发布资产命名契约。

| 指标 | 改造前 | Native AOT | 降幅/结果 |
| --- | ---: | ---: | ---: |
| updater 原始大小 | 116,109,999 bytes | 3,944,960 bytes（3.76 MiB） | 96.60% |
| ZIP `CompressedLength` | 47,294,555 bytes | 1,878,892 bytes（1.79 MiB） | 96.03% |
| updater 占轻量 ZIP | 40.93% | 2.77% | 降低 38.16 个百分点 |
| 轻量 ZIP 总大小 | 115,547,344 bytes | 67,926,749 bytes | 41.21% |

- 原始体积使用 20 MiB 门槛的 18.81%，压缩体积使用 10 MiB 门槛的 17.92%
- EXE 是 AMD64 原生 PE，不含 CLR header，也不依赖 `coreclr`、`hostfxr` 或 `hostpolicy`
- Native AOT 发布输出无 `.runtimeconfig.json`、`.deps.json` 或 DLL；ZIP 中只包含根目录 updater EXE，不包含 PDB
- 主 PDB 与 Updater.Core PDB 均归档在 `artifacts/symbols/win-x64/v1.0.0/`
- updater EXE SHA-256：`5E844694E668D9C84991F9C77F04A14D22EBAB0FFEDFA69D26A671F41ED35967`
- 轻量包 SHA-256：`521b9c3c836f0d8ba2bc9a20608b13a62f2e2efe9bb8739e2202533f74c23c69`
- cloudflared 包 SHA-256：`8f9a3a84c3fe775e4b984e5b758f2907d0812535a2f1f7f84e7abbe91e10e2a9`

## 2026-07-15 全面复核结论

- Win32 `TASKDIALOGCONFIG`/`TASKDIALOG_BUTTON` 按 Windows SDK 的 1-byte packing 固定为 x64 `160/12` bytes，并由偏移测试与真实 AOT TaskDialog smoke 双重门禁
- 失败 TaskDialog 允许“关闭”、系统关闭、Esc 与 Alt+F4；更新进行中的 TaskDialog 仍拒绝一切用户关闭
- 跨平台测试 `1017/1017`、Windows updater 测试 `19/19`、发布后进程验收 `9/9` 全部通过
- 发布后验收覆盖提交、显式回滚、包与 manifest 篡改、apply 后恢复、健康成功、进程崩溃、无法启动、真实 60 秒健康超时，以及 managed→AOT→AOT 连续迁移
- 两个 ZIP 与版本化 PDB 目录以同一发布事务安装；包验证失败或符号安装失败时恢复上一组产物

## CLI 与错误基线

- `--worker --request <不存在文件>`：完全无界面，退出码 `1`
- `--bootstrap` 缺少 `--pipe`：完全无界面，退出码 `2`
- `--recover-worker` 的协调器 PID 无效：完全无界面，退出码 `2`
- 普通协调器缺少 `--request`：显示“更新请求参数无效。请返回应用后重试”，退出码 `2`
- 模式优先级固定为 `bootstrap`、`cleanup`、`recover-worker`、`recover`、`worker`、普通协调器

## 冻结契约

- 协议 schema 仍为 `2`，命名管道仍使用 4 字节小端长度头和 1 MiB 上限
- 所有协议根类型的固定 JSON 位于 `tests/IGoLibrary.Ex.Tests/Fixtures/UpdateProtocol`
- JSON 属性名、camelCase 枚举字符串、缩进、UTF-8 无 BOM、大小写不敏感读取保持不变
- 文件名、更新参数、manifest、可信哈希、事务目录、安全临时目录、UAC、恢复和回滚边界保持不变

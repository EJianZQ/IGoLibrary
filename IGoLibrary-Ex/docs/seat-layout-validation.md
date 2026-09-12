# 真实布局选座验证记录

验证日期：2026-09-11。环境：Windows、.NET SDK 10.0.401、Avalonia 11.3.12。

## 自动化测试

主测试项目全量通过：**1,356 项，零失败、零跳过**，包含架构约束、API 解析、工作流、窗口、标签、收藏和黑名单回归。

新增的布局测试覆盖：

- 完整样例的 816 个元素、356 个座位、状态和各设施类型数量。
- 旧响应、未知类型、空名称、损坏元素、缺失尺寸及原有业务可用性。
- 原始坐标、通道间隔、越界修正、重复标识、重叠座位和规模降级。
- 筛选不重排、不取消选择；地图／列表共享选择；定位不勾选。
- 刷新失败保留地图和草稿；旧请求／旧筛选不能覆盖新场馆。
- 实际 Headless 鼠标、键盘、滚轮、右键菜单和拖动；过滤后禁用操作；模态层保护。
- 明暗主题、小窗口布局和结构化诊断日志。

执行命令：

```powershell
dotnet test tests/IGoLibrary.Ex.Tests/IGoLibrary.Ex.Tests.csproj --no-restore --verbosity minimal -m:1 -p:UsedAvaloniaProducts= --logger 'trx;LogFileName=seat-layout-final.trx'
```

`UsedAvaloniaProducts` 仅在验证命令中清空，以避免 Avalonia 构建统计任务向沙箱外写日志，没有修改项目的生产构建配置。第一次沙箱测试有一个原有命名管道用例因访问受限失败；最终全量测试在允许该访问的环境中通过。

## 发布目标

以下三个目标均完成 Release 发布编译：`win-x64`、`osx-x64`、`osx-arm64`。

```powershell
dotnet publish src/IGoLibrary.Ex.Desktop/IGoLibrary.Ex.Desktop.csproj -c Release -r win-x64 --self-contained false -o .verify-build/seat-layout-publish/win-x64 -m:1 -p:UsedAvaloniaProducts=
```

macOS 使用相同命令切换 RID。NuGet 漏洞数据源在该环境下不可访问，macOS 编译验证额外使用 `-p:NuGetAudit=false`；没有改动依赖版本或仓库审计设置。这些产物用于验证，不是完整安装包或正式发布。

## 渲染与性能

使用实际 Avalonia 控件、项目主题和 Skia Headless 渲染，在 Release 模式测量。首次展示包括创建窗口和渲染首帧；刷新为同一几何布局的数据更新。结果是本机观测值，不是跨设备性能保证。

| 样例 | 首次展示 | 筛选 | 同布局刷新 |
|---|---:|---:|---:|
| 816 元素／356 座位，明色 | 1,936 ms | 107 ms | 191 ms |
| 816 元素／356 座位，暗色 | 1,293 ms | 91 ms | 132 ms |
| 3,000 座位压力样例 | 5,582 ms | 597 ms | 866 ms |

同一几何布局刷新复用座位控件，只更换数据绑定。首次创建极大地图仍有明显成本，超过约定的元素数量或坐标跨度限制时使用列表降级。

本地生成的预览、测量程序和指标位于 `.verify-build/seat-layout-preview/`，最终测试报告位于 `tests/IGoLibrary.Ex.Tests/TestResults/seat-layout-final.trx`；这些生成产物不纳入版本控制。

## 尚需实机验证

明暗主题图片已经人工检查，Headless 交互及跨平台编译已经通过。当前环境没有完成 Windows／macOS 的人工触控板、高 DPI、多显示器及 macOS 原生窗口验收；这些不以 Headless 结果替代。

## 收藏与标签角标修正（2026-09-11）

将随地图缩小的文字星号／菱形改为带对比底色的矢量星星／书签，补偿缩放后的屏幕尺寸；低于 47% 时简化为圆点／方块。座位号、顶部状态条、下角角标和上角勾选标记分别保留显示空间。使用说明和图例同步更新。

- 新增 `SeatMapBadgeTests` 的 9 项 Headless 测试，覆盖 10%、20%、46%、47%、48%、100%、133%、300% 的尺寸与边界，以及角标点击、动态收藏／标签更新、筛选禁止点击。
- 本次 Release 构建成功，相关回归共 114 项通过，涵盖地图、标签、黑名单窗口、明日预约和架构约束；本次没有重新执行全量测试与各 RID 发布。
- 使用实际主题生成并检查了 48%、133%、20% 的明暗主题截图。生成文件位于 `.verify-build/seat-layout-preview/output/badges-*.png`，不纳入版本控制；不能替代实机验收。

## 官方设施图片接入（2026-09-11）

五个 PNG 来自用户桌面提供的官方图标，复制到 `Assets/SeatLayout/`；哈希校验确认与原文件一致。通过 Avalonia 资源嵌入并按需共享解码，无运行时下载。

- 新增 7 项资源与设施渲染测试，验证五种嵌入图片、重复控件共享图片、文字与图片柱子的区别，以及未知类型保留中性图形。
- 完整样例包含 178 个桌子图片、4 个入口图片、83 个柱子图片、92 个窗图片、1 个书架图片；文字保留 94 个“柱”、1 个“柱柱”、方向及“服／务／台”，共 102 个文字元素。业务座位仍为 356 个。
- 本次 Debug 相关测试 135 项全部通过，未构建 Release 或运行发布编译。此前截图与性能数据属于原矢量设施版本，本次未重新进行实机视觉验收。

## 视图下拉选择与持久化（2026-09-11）

视图切换移到筛选输入框一行，使用“场馆布局视图／列表视图”下拉框。两个入口共享现有应用设置中的偏好，启动恢复，手动切换自动保存；异常布局的列表降级不会写入用户偏好。“仅看空闲座位”仅在列表模式显示和生效。

本次 Debug 相关测试 210 项全部通过，涵盖旧设置默认值、SQLite 往返保存、重启恢复、共享偏好、快速切换保存顺序、保存失败、自动降级、地图筛选与选择保留，以及两种窗口尺寸下的下拉框与空闲筛选显示；同时通过相关工作流和架构约束测试。未构建 Release。

## Code review 问题修复（2026-09-12）

- 绑定场馆期间通过命令可用性禁用刷新，并在命令内部拦截直接调用，防止刷新取消已经更新底层场馆的绑定。绑定结束、失败或会话清理后恢复刷新；旧请求迟到结束不能解除新绑定的保护。新绑定仍会取消旧刷新。
- 手动缩放下限包含低于 10% 的全图比例，并保留视口放大或布局刷新前更小的当前比例；缩小不会反向放大。达到下限时保持原比例，放大仍按按钮或滚轮的倍率递增。
- 新增 9 项回归：绑定期间直接刷新、绑定清理后旧结果迟到、缩小按钮／Ctrl 滚轮／Command 滚轮，以及长图、宽图、最大坐标跨度和视口尺寸变化时的缩放范围。
- 定向测试 22 项通过；合并运行相关地图、选座、黑名单、主窗口 ViewModel、设置及架构回归，共 **483 项通过，零失败、零跳过**。Debug 编译和 `git diff --check` 通过；本次未运行全量测试、Release 发布或 macOS 实机验收。

```powershell
dotnet test tests/IGoLibrary.Ex.Tests/IGoLibrary.Ex.Tests.csproj --no-build --no-restore --verbosity minimal --filter 'FullyQualifiedName~Seat|FullyQualifiedName~GlobalLeakBlacklist|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~Settings|FullyQualifiedName~ArchitectureClosure|FullyQualifiedName~TraceIntGraphQlResponseMapperTests'
```

# IGoLibrary Mac 版本

基于 Avalonia UI 的跨平台图书馆座位预约系统 Mac 版本。

## 目录

- [功能特性](#功能特性)
- [系统要求](#系统要求)
- [安装说明](#安装说明)
- [使用指南](#使用指南)
- [技术架构](#技术架构)
- [核心功能详解](#核心功能详解)
- [常见问题](#常见问题)

## 功能特性

### 1. 自动预约系统
- **全自动运行**：系统在 19:59:50 自动进入准备状态，20:00:00 准时开始抢座
- **北京时间同步**：使用 UTC+8 时区确保时间准确性
- **无需手动操作**：设置好座位后系统自动运行，无需人工干预

### 2. 智能备选座位
- **主选+备选机制**：支持设置多个座位，第一个为主选，后续为备选
- **优先级显示**：主选座位显示金色标签，备选座位显示蓝色标签
- **自动切换**：主选座位预约失败时自动尝试备选座位

### 3. 座位管理
- **自动保存**：添加或清空座位时自动保存设置
- **自动恢复**：启动时自动加载上次的座位设置
- **收藏功能**：手动收藏常用座位，随时恢复使用

### 4. 楼层切换
- **便捷切换**：在明日预约页面直接切换图书馆楼层
- **自动刷新**：切换楼层后自动刷新座位列表
- **实时信息**：显示当前绑定的图书馆和余座信息

### 5. 系统自检
- **8项检查**：北京时间、Cookie、图书馆绑定、查询语法、网络连接、预约座位、定时设置、运行状态
- **详细报告**：输出完整的自检日志和建议
- **状态评估**：自动评估系统状态（完美/良好/一般/异常）

### 6. 实时日志
- **运行监控**：实时显示系统运行状态和预约进度
- **详细记录**：记录每次查询、预约尝试和结果
- **自动限制**：保留最近 500 条日志，防止内存泄漏

## 系统要求

- **操作系统**：macOS 10.15 (Catalina) 或更高版本
- **.NET 版本**：.NET 10 SDK
- **内存**：至少 512MB 可用内存
- **网络**：稳定的互联网连接

## 安装说明

### 1. 安装 .NET 10 SDK

```bash
# 使用 Homebrew 安装
brew install dotnet

# 验证安装
dotnet --version
```

### 2. 克隆项目

```bash
git clone https://github.com/EJianZQ/IGoLibrary.git
cd IGoLibrary
```

### 3. 构建项目

```bash
# 清理项目
dotnet clean IGoLibrary.Mac/IGoLibrary.Mac.csproj

# 恢复依赖
dotnet restore IGoLibrary.Mac/IGoLibrary.Mac.csproj

# 构建项目
dotnet build IGoLibrary.Mac/IGoLibrary.Mac.csproj
```

### 4. 运行应用

```bash
dotnet run --project IGoLibrary.Mac/IGoLibrary.Mac.csproj
```

## 使用指南

### 第一步：登录获取 Cookie

1. 打开应用，默认显示登录页面
2. 点击"获取二维码"按钮
3. 使用微信扫描二维码登录
4. 系统自动获取并保存 Cookie

### 第二步：绑定图书馆

1. 在登录页面点击"加载图书馆列表"
2. 从下拉列表中选择目标图书馆楼层
3. 点击"绑定图书馆"按钮
4. 系统自动保存绑定信息

### 第三步：设置预约座位

1. 切换到"明日预约"页面
2. 点击"刷新座位"获取最新座位信息
3. 从座位列表中选择心仪的座位
4. 点击"添加到预约列表"
   - 第一个添加的座位为主选（金色标签）
   - 后续添加的座位为备选（蓝色标签）
5. 系统自动保存座位设置

### 第四步：系统自动运行

- 设置完座位后，系统会在 19:59:50 自动进入准备状态
- 20:00:00 准时开始抢座
- 按照主选→备选1→备选2的顺序尝试预约
- 预约成功后自动停止

### 可选功能

#### 收藏座位
1. 设置好预约座位后，点击"收藏选中座位"
2. 下次使用时点击"加载收藏座位"即可恢复

#### 切换楼层
1. 在明日预约页面点击"加载楼层列表"
2. 选择新的楼层
3. 点击"切换楼层"
4. 系统自动刷新座位列表

#### 系统自检
1. 点击"系统自检"按钮
2. 查看右侧日志区的自检报告
3. 根据建议修复问题

## 技术架构

### 前端框架
- **Avalonia UI 11.2.2**：跨平台 XAML UI 框架
- **MVVM 模式**：使用 CommunityToolkit.Mvvm 实现数据绑定
- **依赖注入**：Microsoft.Extensions.DependencyInjection

### 核心服务
- **IGetCookieService**：获取登录 Cookie
- **IGetLibInfoService**：获取图书馆座位信息
- **IGetAllLibsSummaryService**：获取所有图书馆列表
- **IReserveSeatService**：预约座位（当日）
- **IPrereserveSeatService**：预约座位（明日）
- **ISessionService**：会话管理和状态保持
- **INotificationService**：系统通知
- **IStorageService**：数据持久化

### 数据模型
- **SeatKeyData**：座位数据模型，包含优先级信息
- **LibraryData**：图书馆数据模型
- **LibSummary**：图书馆摘要信息

### API 通信
- **GraphQL**：使用 GraphQL 查询和变更操作
- **HTTP Client**：基于 HttpClient 的网络请求
- **JSON 序列化**：使用 Newtonsoft.Json

## 核心功能详解

### 1. 自动初始化流程

```
应用启动
  ↓
自动加载 Cookie（LoginViewModel.AutoLoadCookieOnStartupAsync）
  ↓
切换到明日预约页面
  ↓
自动初始化（GrabSeatViewModel.AutoInitializeAsync）
  ↓
刷新座位信息
  ↓
恢复上次的座位设置
  ↓
自动启动监控
  ↓
等待到 19:59:50
  ↓
进入准备状态
  ↓
20:00:00 开始抢座
```

### 2. 优先级预约逻辑

```csharp
// 按顺序检查所有座位（主选→备选1→备选2...）
for (int i = 0; i < WaitingGrabSeats.Count; i++)
{
    var targetSeat = WaitingGrabSeats[i];
    string seatPosition = i == 0 ? "主选" : $"备选{i}";

    // 检查座位是否可用
    if (!currentSeatStatus.status) // status == false 表示无人
    {
        // 尝试预约
        bool prereserveSuccess = _prereserveSeatService.PrereserveSeat(
            _sessionService.Cookie!,
            targetSeat.Key,
            _sessionService.CurrentLibrary?.LibID ?? 0);

        if (prereserveSuccess)
        {
            // 预约成功，退出监控
            AddLog($"【预约成功】🎉 {seatPosition}座位 {targetSeat.Name} 号预约成功！");
            return;
        }
        else
        {
            // 预约失败，尝试下一个备选座位
            AddLog($"【预约失败】❌ {seatPosition}座位 {targetSeat.Name} 号预约失败，尝试下一个备选座位");
        }
    }
}
```

### 3. 自动保存/恢复机制

**保存位置**：`~/Library/Application Support/IGoLibrary/Favorites/{LibID}.json`

**数据格式**：
```json
{
  "LibID": 123,
  "Seats": [
    {
      "Key": "seat_key_1",
      "Name": "101",
      "Priority": 0
    },
    {
      "Key": "seat_key_2",
      "Name": "102",
      "Priority": 1
    }
  ]
}
```

**触发时机**：
- 添加座位到预约列表时自动保存
- 清空预约列表时自动保存
- 应用启动时自动恢复

### 4. 北京时间处理

```csharp
// 获取北京时间（UTC+8）
var beijingTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
var beijingNow = TimeZoneInfo.ConvertTime(DateTime.Now, beijingTimeZone);

// 计算距离目标时间的间隔
var targetTime = new TimeSpan(19, 59, 50);
var nowTime = beijingNow.TimeOfDay;
var interval = targetTime - nowTime;

// 处理跨天情况
if (interval.TotalSeconds < 0)
{
    interval = interval.Add(TimeSpan.FromDays(1));
}
```

### 5. 抢座模式

- **激进模式（1秒）**：每秒查询一次，适合网络良好的情况
- **随机模式（4-8秒）**：随机间隔，模拟人工操作
- **保守模式（5秒）**：默认模式，平衡速度和稳定性

### 6. 系统自检项目

1. **北京时间获取**：验证时区设置是否正确
2. **Cookie 检查**：验证 Cookie 是否设置且格式正确
3. **图书馆绑定**：验证是否已绑定图书馆
4. **查询语法**：验证 GraphQL 查询语法是否配置
5. **网络连接**：测试服务器连通性
6. **预约座位**：检查是否设置了预约座位
7. **定时设置**：验证定时配置和倒计时
8. **自动运行**：检查系统运行状态

## 常见问题

### Q1: 应用启动后没有自动运行怎么办？

**A**: 检查以下几点：
1. 是否已登录并获取 Cookie
2. 是否已绑定图书馆
3. 是否已设置预约座位
4. 运行"系统自检"查看详细状态

### Q2: 提示"Cookie 未设置"怎么办？

**A**:
1. 切换到登录页面
2. 点击"获取二维码"
3. 使用微信扫码登录
4. 系统会自动保存 Cookie

### Q3: 提示"未绑定图书馆"怎么办？

**A**:
1. 在登录页面点击"加载图书馆列表"
2. 选择目标图书馆楼层
3. 点击"绑定图书馆"

或者：
1. 在明日预约页面点击"加载楼层列表"
2. 选择目标楼层
3. 点击"切换楼层"

### Q4: 如何查看系统是否正常运行？

**A**:
1. 查看右侧运行日志区
2. 观察系统状态指示器（绿色=运行中，灰色=等待中）
3. 运行"系统自检"查看详细报告

### Q5: 预约失败怎么办？

**A**: 可能的原因：
1. Cookie 已过期：重新登录获取 Cookie
2. 座位已被预约：添加更多备选座位
3. 网络问题：检查网络连接
4. 服务器维护：等待服务器恢复

### Q6: 如何添加备选座位？

**A**:
1. 从座位列表中选择座位
2. 点击"添加到预约列表"
3. 重复以上步骤添加多个座位
4. 第一个为主选（金色），后续为备选（蓝色）

### Q7: 如何切换到其他楼层？

**A**:
1. 在明日预约页面点击"加载楼层列表"
2. 从下拉列表选择新楼层
3. 点击"切换楼层"
4. 系统自动刷新座位列表

### Q8: 座位设置会保存吗？

**A**:
- 会自动保存，下次启动时自动恢复
- 也可以手动点击"收藏选中座位"保存
- 使用"加载收藏座位"恢复

### Q9: 系统什么时候开始抢座？

**A**:
- 准备时间：19:59:50（进入准备状态）
- 开始时间：20:00:00（准时开始抢座）
- 使用北京时间（UTC+8）确保准确性

### Q10: 如何停止自动运行？

**A**:
- 系统预约成功后会自动停止
- 如需手动停止，关闭应用即可
- 下次启动会继续自动运行

## 项目结构

```
IGoLibrary/
├── IGoLibrary.Core/              # 核心业务逻辑
│   ├── Data/                     # 数据模型
│   │   ├── SeatKeyData.cs       # 座位数据（含优先级）
│   │   ├── LibraryData.cs       # 图书馆数据
│   │   └── LibSummary.cs        # 图书馆摘要
│   ├── Interfaces/               # 服务接口
│   ├── Services/                 # 服务实现
│   └── Exceptions/               # 自定义异常
├── IGoLibrary.Mac/               # Mac 版本
│   ├── ViewModels/               # 视图模型
│   │   ├── MainViewModel.cs     # 主视图模型
│   │   ├── LoginViewModel.cs    # 登录视图模型
│   │   ├── GrabSeatViewModel.cs # 明日预约视图模型
│   │   └── OccupySeatViewModel.cs # 当日占座视图模型
│   ├── Views/                    # 视图
│   │   ├── MainWindow.axaml     # 主窗口
│   │   ├── LoginView.axaml      # 登录页面
│   │   ├── GrabSeatView.axaml   # 明日预约页面
│   │   └── OccupySeatView.axaml # 当日占座页面
│   ├── Services/                 # Mac 特定服务
│   ├── App.axaml                 # 应用程序
│   └── Program.cs                # 入口点
└── README_MAC.md                 # 本文档
```

## 开发说明

### 构建配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
</Project>
```

### 主要依赖

- Avalonia 11.2.2
- Avalonia.Desktop 11.2.2
- CommunityToolkit.Mvvm 8.4.0
- Microsoft.Extensions.DependencyInjection 9.0.0
- Newtonsoft.Json 13.0.3

### 调试运行

```bash
# 开发模式运行
dotnet run --project IGoLibrary.Mac/IGoLibrary.Mac.csproj

# 发布版本
dotnet publish IGoLibrary.Mac/IGoLibrary.Mac.csproj -c Release -r osx-x64 --self-contained
```

## 许可证

本项目遵循原项目的许可证。

## 贡献

欢迎提交 Issue 和 Pull Request。

## 联系方式

- 原项目：https://github.com/EJianZQ/IGoLibrary
- Gitee 参考：https://gitee.com/suchongyuan/I_Goto_Library

## 更新日志

### v1.0.0 (2025-12-07)
- 实现基于 Avalonia UI 的 Mac 版本
- 添加自动预约系统（19:59:50 准备，20:00:00 开始）
- 实现智能备选座位机制（主选+备选）
- 添加优先级显示（金色主选，蓝色备选）
- 实现自动保存/恢复座位设置
- 添加楼层切换功能
- 实现系统自检模块（8项检查）
- 添加实时运行日志（最多500条）
- 使用北京时间（UTC+8）确保时间准确性
- 实现自动初始化和自动运行

---

**注意**：本应用仅供学习交流使用，请遵守图书馆相关规定，合理使用座位资源。

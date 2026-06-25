<p align="center">
  <img src="docs/images/ex/软件图标-大.png" alt="IGoLibrary Icon" width="120" />
</p>

<h1 align="center">我去图书馆助手 - IGoLibrary 📖</h1>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Avalonia-11-8B5CF6?logo=avaloniaui&logoColor=white" alt="Avalonia 11" />
  <img src="https://img.shields.io/badge/SQLite-3-003B57?logo=sqlite&logoColor=white" alt="SQLite" />
  <img src="https://img.shields.io/badge/xUnit-Test-5C2D91?logo=xunit&logoColor=white" alt="xUnit" />
  <img src="https://img.shields.io/github/v/release/EJianZQ/IGoLibrary?logo=github" alt="Latest Release" />
  <img src="https://img.shields.io/github/license/EJianZQ/IGoLibrary" alt="License" />
</p>

<p align="center">
  IGoLibrary-Ex 是基于 <code>Avalonia</code> 重构的新一代跨平台桌面端实现。可运行在 <code>Windows 10 22H2</code> 及以上与 <code>macOS 15 Sequoia</code> 及以上。已实现 <strong>扫码获取 Cookie</strong>、<strong>座位实时监控并抢座</strong>、<strong>跨场馆全域捡漏</strong>、<strong>明日预约</strong>、<strong>利用退座机制进行占座</strong> 和 <strong>Cookie 过期提醒</strong> 等实用功能
</p>

<p align="center">
  <img src="docs/images/ex/主页.png" alt="IGoLibrary-Ex 首页截图" width="960" />
</p>

## ✨ 核心功能

- 🔐 基于微信扫码链接来获取 Cookie 完成登录，并支持恢复本地保存的会话
- 🏛️ 自动加载账号下可用场馆，支持选择、预览并锁定当前作业场馆
- ⚡ 支持多目标座位监控，实现退座监控秒抢、定时抢座
- 🧭 支持全域捡漏，可多选账号下的场馆并按间隔扫描空座，发现可预约座位后自动尝试预约
- 📅 支持明日预约，可设置触发时间到点执行
- ♻️ 支持占座流程，在预约即将到期时自动取消并重新预约
- ⭐ 支持收藏常用座位，并为每个场馆分别持久化座位收藏
- 📊 首页面板展示当前场馆、预约状态、累计成功次数和守护时长
- 🔔 支持 Cookie 失效、抢座成功、全域捡漏成功、占座成功、明日预约成功和任务失败提醒，可通过右下角 Toast 弹窗、提示音、SMTP 邮件和 Telegram Bot 通知用户
- 🧩 支持自定义 API 地址覆盖，便于在接口地址或 GraphQL 模板变化时快速调整

## 🧱 项目结构

```text
IGoLibrary-Ex/
  src/
    IGoLibrary.Ex.Domain/           领域模型
    IGoLibrary.Ex.Application/      用例编排、任务协调器、应用服务
    IGoLibrary.Ex.Infrastructure/   API、持久化、GraphQL模板、远程通知
    IGoLibrary.Ex.Desktop/          Avalonia UI、ViewModel、桌面交互
  tests/
    IGoLibrary.Ex.Tests/            单元测试与界面逻辑测试
  build/                            发布脚本
```

## 🚀 使用说明
> [!TIP]
> 当前以 `IGoLibrary-Ex` 作为主项目说明，旧版 WinForm 项目的历史 README 已迁移至 [README_Winform.md](README_Winform.md)
> `IGoLibrary-Ex` 目前仍处于测试阶段，可能会有未知BUG。旧版 WinForm 项目仍正常稳定工作，如需使用旧版请移步 [Winform -1.3版本](https://github.com/EJianZQ/IGoLibrary/releases/tag/Public1.3) 进行下载

### 1️⃣ 获取 Cookie
1. 在 `账户与场馆` 页面使用已经在微信绑定好"我去图书馆"学校等信息的**微信**扫描软件内的二维码
2. 点击页面右上角的"..."，然后点击复制链接
3. 将复制后的链接通过任意方式(手机QQ/微信等等)发到**电脑上**然后
4. 在电脑上复制该链接，回到软件的 `账户与场馆` 页面，将会自动识别并填写好 Cookie

### 2️⃣ 锁定场馆
1. 在 `账户与场馆` 页面，如果 Cookie 正确获取了，会显示✅绿色对勾和**已授权**字样
2. 在页面右半边，点击 `更换当前场馆...` 按钮即可选择场馆
3. 选择好场馆后点击 `保存并锁定场馆` 按钮即可正式锁定该场馆

### 3️⃣ 抢座
1. 在 `抢座` 页面点击 `选择目标座位` 按钮，选择欲监控座位。座位以边框颜色区分是否已经被占，收藏座位会在该座位右上角添加⭐标
2. 选择 `抢座执行策略`，默认直接发送预约请求，不懂可直接默认。`抢座模式` 分为极限速度、随机延迟和延迟 5 秒，区别如下：
   - 极限速度：以最快速度抢，适合临时秒杀。例如 早上8点开始抢，`7:59:55` 的时候使用极限速度模式开始监控
   - 随机延迟：每次监控一轮后，随机延迟 N 秒进入下一轮，适合想要的座位已经被人占了，挂着软件监控该座位
   - 延迟 5 秒(默认)：每次监控一轮后，延迟 5 秒进入下一轮，最平衡的模式，推荐使用
3. 点击 `开始监控` 按钮，确认任务开始后把软件挂在后台即可。若监控的座位空出会立即预约并在右下角弹出 Toast 弹窗提醒

### 4️⃣ 全域捡漏
1. 在 `全域捡漏` 页面点击 `选择扫描场馆` 按钮，勾选希望参与扫描的一个或多个场馆。该功能会扫描场馆内所有可预约空座，不需要提前指定座位
2. 设置 `扫描间隔`，默认是 `10` 秒。间隔越短扫描越频繁，请根据网络状况和学校接口限制谨慎调整
3. 点击 `开始捡漏` 后把软件挂在后台即可。软件会按轮次扫描已选场馆，发现空座后逐个尝试预约，任意座位预约成功后任务会自动完成
4. 捡漏成功后会写入全域捡漏实时日志，并按通知设置发送本地弹窗、提示音、SMTP 邮件或 Telegram Bot 提醒

### 5️⃣ 明日预约
1. 在 `明日预约` 页面点击 `选择目标座位` 按钮，选择一个明天要预约的目标座位。明日预约暂只支持单目标座位
2. 设置 `执行控制` 中的触发时间，默认是 `20:00:00`。到达触发时间后，软件会进入明日预约排队通道、预热场馆并提交预约
3. 点击 `开始定时预约` 后把软件挂在后台即可。也可以先点击 `立即执行一次`，用于验证当前场馆、座位和接口配置是否可用
4. 预约成功后会写入明日预约实时日志，并按通知设置发送本地弹窗、提示音、SMTP 邮件或 Telegram Bot 提醒

### 6️⃣ 占座
> [!TIP]
> 使用此功能，你必须要已经预约好了座位！
1. 在 `页面` 你会看到当前的预约信息，如果没有则点击 `手动刷新` 按钮
2. 设置 `重新预约间隔`。每个学校都不一样，需要你自己事先看一下。随便预约一个座位，然后取消。取消之后再立刻去预约座位，会提示**取消预约后 N 分钟不可再次预约**之类的文案，把 N 分钟折算成秒数填入软件
3. 点击 `开始占座` 按钮，确认任务开始后把软件挂在后台即可

### 7️⃣ 系统提醒
系统提醒目前包含 Cookie 过期提醒、抢座成功提醒、全域捡漏成功提醒、占座成功提醒、明日预约成功提醒和任务失败提醒，可按需开启本地弹窗、提示音、SMTP 邮件和 Telegram Bot 通知
####  邮件提醒配置
参见 [SMTP 邮件提醒配置指南](docs/smtp-email-alert-configuration.md)
#### Telegram Bot 提醒配置
1. 在 `通知设置` 页面切换到 `Telegram` 配置页
2. 开启 `Telegram Bot 提醒`
3. 填写 `API 基础地址`、`Bot Token` 和 `Chat ID`
   - `API 基础地址` 默认使用 `https://api.telegram.org`
   - 如果当前网络无法直连 Telegram，可以改成自己可访问的 Telegram Bot API 代理地址
4. 点击 `测试 Telegram`，确认能够收到测试消息后即可保存使用

<details>
<summary>如何获取 Bot Token 和 Chat ID</summary>

##### 获取 Bot Token
1. 在 Telegram 中搜索并打开 `@BotFather`
2. 发送 `/newbot`
3. 按提示填写机器人名称和用户名，用户名通常需要以 `bot` 结尾
4. 创建成功后，`@BotFather` 会返回一段 `Bot Token`，复制后填入软件

> [!IMPORTANT]
> `Bot Token` 相当于机器人的密码，不要公开给别人

##### 获取 Chat ID
1. 在 Telegram 中打开刚创建的机器人，点击 `Start` 或发送任意消息
2. 在浏览器访问下面的地址，将 `<BotToken>` 替换成你的 `Bot Token`

```text
https://api.telegram.org/bot<BotToken>/getUpdates
```

3. 在返回内容中找到 `message.chat.id`，这个数字就是 `Chat ID`
4. 如果返回内容为空，先给机器人再发一条消息，然后刷新上面的地址

如果要把提醒发到群组，请先把机器人加入目标群组，并在群里发送一条消息，再通过 `getUpdates` 查找对应群组的 `chat.id`。群组的 `Chat ID` 通常是负数。

如果使用的是 Telegram Bot API 代理地址，请把上面地址中的 `https://api.telegram.org` 替换成你自己的 `API 基础地址`

</details>

开启后，Cookie 失效、抢座成功、全域捡漏成功、占座成功、明日预约成功和任务失败都会通过 Telegram Bot 发送提醒。Telegram 发送失败时只会写入应用日志，不会阻塞本地弹窗、邮件或任务执行流程
#### 本地弹窗提醒
当监测到 Cookie 过期、抢座成功、全域捡漏成功、占座成功、明日预约成功或任务失败时，会在屏幕右下角弹出 Toast 弹窗提醒。如果打开了提示音，还会有相应的提示音

### 🍎 macOS 首次运行方法

macOS 用户请先按自己的电脑芯片选择对应压缩包，下载后按下面步骤运行：

1. 双击 zip 解压
2. 解压后会看到 `IGoLibrary-Ex.app`、`macOS首次运行说明.txt` 和 `首次运行.command`
3. 优先尝试右键点击 `IGoLibrary-Ex.app`，选择“打开”
4. 如果系统提示“已损坏，无法打开”，双击同目录下的 `首次运行.command`
5. 如果 `首次运行.command` 被系统拦截，打开“终端”，输入下面命令后再按一次空格：

```bash
xattr -dr com.apple.quarantine
```

然后把 `IGoLibrary-Ex.app` 拖入终端窗口，按回车执行。执行完成后，再双击或右键打开 `IGoLibrary-Ex.app`。

> [!IMPORTANT]
> 请打开 `IGoLibrary-Ex.app`，不要进入应用包内部，也不要直接双击 `Contents/MacOS/IGoLibrary.Ex.Desktop`。
> 当前 macOS 版本未签名、未公证，因此首次运行可能需要解除系统隔离标记。这不代表压缩包损坏。

## 🛠️ 开发与构建

### 环境要求

- 建议使用 `IGoLibrary-Ex/global.json` 指定的 `.NET SDK 10.0.201`
- Windows 开发环境可直接使用 Visual Studio 2022 或 `dotnet CLI`
- 仓库已提供 Windows 与 macOS 的发布脚本

### 本地运行

```powershell
cd .\IGoLibrary-Ex
dotnet restore
dotnet run --project .\src\IGoLibrary.Ex.Desktop\IGoLibrary.Ex.Desktop.csproj
```

### 运行测试

```powershell
cd .\IGoLibrary-Ex
dotnet test .\tests\IGoLibrary.Ex.Tests\IGoLibrary.Ex.Tests.csproj
```

### 发布示例

Windows

```powershell
cd .\IGoLibrary-Ex
.\build\publish-windows.ps1 -Configuration Release -AppVersion 1.0.1
```

macOS

如果在 Windows 上同时构建并打包 Apple Silicon 与 Intel 两个 macOS 版本，使用 PowerShell 脚本：

```powershell
cd .\IGoLibrary-Ex
.\build\publish-macos-all.ps1 -Configuration Release -AppVersion 1.0.1
```

脚本会分别生成 `artifacts\publish\osx-arm64\` 与 `artifacts\publish\osx-x64\` 原始发布目录，然后组装为标准 macOS 应用包并输出两个最终 zip：

```text
artifacts\macos\osx-arm64\IGoLibrary-Ex-v1.0.1-macOS-Apple-Silicon-arm64.zip
artifacts\macos\osx-x64\IGoLibrary-Ex-v1.0.1-macOS-Intel-x64.zip
```

发布给 M 芯片用户时发送 `IGoLibrary-Ex-v1.0.1-macOS-Apple-Silicon-arm64.zip`，发布给 Intel 芯片用户时发送 `IGoLibrary-Ex-v1.0.1-macOS-Intel-x64.zip`。用户解压后打开 `IGoLibrary-Ex.app`，不要直接双击应用包内部的 `IGoLibrary.Ex.Desktop` 可执行文件。

如果只想单独发布某一个架构，也可以直接调用单架构脚本：

```powershell
.\build\publish-macos.ps1 -Configuration Release -Runtime osx-arm64 -AppVersion 1.0.1
.\build\publish-macos.ps1 -Configuration Release -Runtime osx-x64 -AppVersion 1.0.1
```

如果在 macOS 发布机上构建，也可以使用 Bash 脚本：

```bash
cd ./IGoLibrary-Ex
APP_VERSION=1.0.1 ./build/publish-macos.sh Release osx-arm64
```

未签名、未公证的 macOS 包首次运行时，系统可能会拦截并提示“已损坏，无法打开”。zip 内已包含 `macOS首次运行说明.txt` 和 `首次运行.command`，用户可按说明解除隔离标记后再打开 `IGoLibrary-Ex.app`。

## 💾 本地数据说明

应用会在本地使用 `SQLite` 保存配置、收藏座位、自定义API接口以及必要的会话信息

- Windows 默认数据目录：`%LOCALAPPDATA%\IGoLibrary-Ex`
- macOS 默认数据目录：`~/Library/Application Support/IGoLibrary-Ex`
- 可通过环境变量 `IGOLIBRARY_EX_DATA_DIR` 覆盖默认数据目录

## ⚠️ 免责声明

本项目以学习、研究与交流为目的，用于桌面应用架构、协议交互、任务调度与本地通知等方向的实践探索。

- 本项目与“我去图书馆”系统及其所属组织不存在从属或官方合作关系
- 请在遵守所在学校、场馆及相关平台规则的前提下了解和使用本项目
- 如果你发现问题，欢迎提交 Issue 或继续完善 `IGoLibrary-Ex`

## 📄 License

本项目基于 [MIT License](LICENSE.txt) 开源。

# SMTP 邮件提醒配置指南

本文档说明如何在 IGoLibrary-Ex 中配置 SMTP 邮件提醒，配置完成后，应用可以在 Cookie 失效、抢座成功、占座成功、明日预约成功、空座出现、签到提醒、错过签到、签到补约成功或任务失败时，通过邮箱发送提醒

## 🚀 配置入口

1. 打开 IGoLibrary-Ex
2. 进入 `通知设置` 页面
3. 切换到 `邮件` 配置页
4. 按下文填写 SMTP 参数
5. 点击 `发送测试邮件`
6. 确认收件邮箱收到测试邮件后，打开 `开启邮件提醒`


## 🧩 字段说明

### SMTP 服务器地址（主机名）

填写邮箱服务商提供的 SMTP 服务器主机名，例如：

- QQ 邮箱：`smtp.qq.com`
- 163 邮箱：`smtp.163.com`
- Gmail：`smtp.gmail.com`
- Resend：`smtp.resend.com`

企业邮箱、自建邮箱或学校邮箱通常会有独立的 SMTP 主机名，请在邮箱后台或管理员文档中查询

### 端口

常见端口如下：

| 端口 | 安全性 | 说明 |
| --- | --- | --- |
| `587` | `TLS` | 推荐优先尝试，应用会使用 STARTTLS 连接 |
| `465` | `TLS` | 适合要求 SSL/TLS 直连的邮箱 |
| `25` | `无` 或 `TLS` | 部分网络会封禁 25 端口，不推荐作为首选 |

如果服务商文档明确要求某个端口，请以服务商文档为准

### 安全性

提供两个选项：

- `TLS`：推荐，端口为 `587` 时使用 STARTTLS，端口为 `465` 时使用 SSL/TLS 直连
- `无`：仅在你的 SMTP 服务明确允许无加密连接时使用

大多数公网邮箱都要求 TLS，若不确定，请选择 `TLS`

### 用户名

通常填写完整邮箱地址，例如：

```text
yourname@qq.com
yourname@163.com
yourname@gmail.com
```

有些企业邮箱可能要求只填写邮箱前缀，或填写独立的 SMTP 登录账号，遇到登录失败时，请检查服务商文档中的 "SMTP 用户名" 要求

### 邮箱授权码/密码

多数邮箱不允许第三方客户端直接使用网页登录密码，而是要求生成"授权码"、"应用专用密码"或"客户端密码"

常见规则：

- QQ 邮箱、163 邮箱通常需要在邮箱网页端开启 SMTP/POP3/IMAP 服务后生成授权码
- Gmail 通常需要开启两步验证后创建 App Password
- Outlook.com / Microsoft 个人邮箱通常要求 OAuth2/Modern Auth，当前 IGoLibrary-Ex 暂不支持 OAuth2 登录，因此可能无法通过普通 SMTP 密码方式使用
- 企业邮箱可能由管理员统一开启 SMTP，并分配独立密码

### 发信人邮箱地址

填写邮件发送方地址，通常应与 `用户名` 相同

示例：

```text
yourname@qq.com
```

某些邮箱服务商禁止使用与登录账号不同的发信人地址，如果测试邮件提示发件人无权限，请把发信人改成 SMTP 登录邮箱

### 收信人邮箱地址

填写接收提醒的邮箱地址

你可以填写：

- 自己的同一个邮箱
- 另一个常用邮箱
- 手机邮件客户端已登录的邮箱

当前应用界面按单个收件地址设计，如果需要多人接收，建议先使用邮箱服务商的自动转发、邮件规则或邮件组

## 📋 常见邮箱配置示例

以下示例仅为当前可用，服务商随时可能会调整策略，请以其官方帮助页面为准
没有列举在下面表格中的邮箱，只要你有对应参数即可接入

| 邮箱类型 | SMTP 主机 | 常用端口 | 安全性 | 用户名 | 密码字段 |
| --- | --- | --- | --- | --- | --- |
| QQ 邮箱 | `smtp.qq.com` | `587` 或 `465` | `TLS` | 完整 QQ 邮箱地址 | SMTP 授权码 |
| 163 邮箱 | `smtp.163.com` | `465` 或服务商指定端口 | `TLS` | 完整 163 邮箱地址 | 客户端授权码 |
| Gmail | `smtp.gmail.com` | `587` 或 `465` | `TLS` | 完整 Gmail 地址 | App Password |
| Resend | `smtp.resend.com` | `587` 或 `465` | `TLS` | `resend` | Resend API Key |

## 🛠️ 常用邮箱配置流程

### QQ 邮箱

#### 1. 开启 SMTP 服务并获取授权码

1. 登录 [QQ 邮箱网页版](https://mail.qq.com/)
2. 进入 `设置`，找到 `账号` 或 `POP3/IMAP/SMTP/Exchange/CardDAV/CalDAV服务`
3. 开启 `POP3/SMTP服务` 或 `IMAP/SMTP服务`
4. 按页面提示完成短信或密保验证
5. 复制生成的 `授权码`

#### 2. 在 IGoLibrary-Ex 中填写

```text
SMTP 服务器地址：smtp.qq.com
端口：587
安全性：TLS
用户名：yourname@qq.com
邮箱授权码/密码：QQ 邮箱生成的授权码
发信人邮箱地址：yourname@qq.com
收信人邮箱地址：你的接收邮箱
```

如果 `587 + TLS` 测试失败，可以改用：

```text
端口：465
安全性：TLS
```

#### 3. 测试并启用

1. 点击 `发送测试邮件`
2. 收到测试邮件后，打开 `开启邮件提醒`
3. 如果提示认证失败，优先重新生成授权码并确认 `用户名` 是完整邮箱地址

### 网易邮箱

#### 1. 开启客户端授权密码

1. 登录网易邮箱网页版
   - 163 邮箱：[mail.163.com](https://mail.163.com/)
   - 126 邮箱：[mail.126.com](https://mail.126.com/)
   - yeah.net 邮箱：[mail.yeah.net](https://mail.yeah.net/)
2. 进入 `设置`
3. 找到 `POP3/SMTP/IMAP` 或 `客户端授权密码`
4. 开启 `IMAP/SMTP服务` 或 `POP3/SMTP服务`
5. 按页面提示完成手机验证
6. 生成并复制 `客户端授权密码`

#### 2. 在 IGoLibrary-Ex 中填写

163 邮箱示例：

```text
SMTP 服务器地址：smtp.163.com
端口：465
安全性：TLS
用户名：yourname@163.com
邮箱授权码/密码：网易邮箱生成的客户端授权密码
发信人邮箱地址：yourname@163.com
收信人邮箱地址：你的接收邮箱
```

126 邮箱或 yeah.net 邮箱通常可以先按对应域名填写：

```text
126 邮箱 SMTP：smtp.126.com
yeah.net 邮箱 SMTP：smtp.yeah.net
端口：465
安全性：TLS
```

如果对应域名测试失败，可以在网易邮箱网页版的 `POP3/SMTP/IMAP` 页面查看当前账号展示的 SMTP 参数，并按页面提示填写

#### 3. 测试并启用

1. 点击 `发送测试邮件`
2. 收到测试邮件后，打开 `开启邮件提醒`
3. 如果提示密码错误，重新检查是否填入 `客户端授权密码`，不是邮箱网页登录密码

### Gmail 邮箱

#### 1. 创建 App Password

1. 登录 [Google Account](https://myaccount.google.com/)
2. 打开 `Security`
3. 开启 `2-Step Verification`
4. 进入 [App passwords](https://myaccount.google.com/apppasswords)
5. 为邮件发送场景创建一个新的 App Password
6. 复制生成的 16 位 App Password

> Google 账号只有开启两步验证后，才通常能创建 App Password；部分工作、学校或组织账号可能被管理员策略限制

#### 2. 在 IGoLibrary-Ex 中填写

```text
SMTP 服务器地址：smtp.gmail.com
端口：587
安全性：TLS
用户名：yourname@gmail.com
邮箱授权码/密码：Google 生成的 App Password
发信人邮箱地址：yourname@gmail.com
收信人邮箱地址：你的接收邮箱
```

如果 `587 + TLS` 测试失败，可以改用：

```text
端口：465
安全性：TLS
```

#### 3. 测试并启用

1. 点击 `发送测试邮件`
2. 收到测试邮件后，打开 `开启邮件提醒`
3. 如果提示账号密码错误，确认 App Password 没有复制错，并确认账号允许通过 App Password 访问



### Resend（自有域名）

Resend 更适合已经拥有域名的用户，例如你有 `example.com`，想用 `notify@example.com` 或 `notice@notify.example.com` 作为发信人

#### 1. 添加并验证域名

1. 登录 [Resend Dashboard](https://resend.com/)
2. 进入 `Domains`
3. 添加你拥有的域名或子域名
4. 推荐优先使用子域名，例如 `notify.example.com`、`mail.example.com` 或 `updates.example.com`
5. 按 Resend 页面提示，到域名 DNS 服务商处添加 SPF 和 DKIM 记录
6. 回到 Resend 点击 `Verify DNS Records`
7. 等待域名状态变为 `verified`


#### 2. 创建 API Key

1. 进入 Resend 的 `API Keys`
2. 点击 `Create API Key`
3. 名称可以填写 `IGoLibrary-Ex`
4. 权限建议选择 `Sending access`
5. 如果页面允许选择域名，建议限制到刚刚验证好的发信域名
6. 创建后复制 API Key，通常以 `re_` 开头

#### 3. 在 IGoLibrary-Ex 中填写

```text
SMTP 服务器地址：smtp.resend.com
端口：587
安全性：TLS
用户名：resend
邮箱授权码/密码：Resend API Key，例如 re_xxxxxxxxx
发信人邮箱地址：notify@example.com
收信人邮箱地址：你的接收邮箱
```

如果你验证的是子域名，例如 `notify.example.com`，发信人可以填写：

```text
发信人邮箱地址：notice@notify.example.com
```

如果 `587 + TLS` 测试失败，可以改用：

```text
端口：465
安全性：TLS
```

Resend 也支持 `2465` 和 `2587`，但在 IGoLibrary-Ex 里优先使用常见的 `587 + TLS` 或 `465 + TLS` 即可

#### 4. 测试并启用

1. 点击 `发送测试邮件`
2. 收到测试邮件后，打开 `开启邮件提醒`
3. 如果提示发信人无权限，确认 `发信人邮箱地址` 使用的是已在 Resend 验证过的域名
4. 如果提示认证失败，确认 `用户名` 固定填写 `resend`，密码字段填写完整 API Key

## 🔔 触发哪些邮件

开启后，以下事件会尝试发送邮件：

- Cookie 失效提醒
- 抢座成功提醒
- 占座重新预约成功提醒
- 明日预约成功提醒
- 场馆空座提醒
- 签到提醒、错过签到提醒和签到补约成功提醒
- 抢座、明日预约、占座、空座追踪或签到守护任务失败提醒

如果邮件发送失败，应用会把失败原因写入日志，不会阻塞本地弹窗、远程推送或任务本身

## 🧯 故障排查

### 提示“SMTP 用户名和邮箱授权码/密码需要同时填写，或同时留空”

应用要求 `用户名` 和 `邮箱授权码/密码` 同时填写，或同时留空

公网邮箱通常需要认证，请同时填写：

```text
用户名：完整邮箱地址
邮箱授权码/密码：授权码或应用专用密码
```

### 测试邮件提示认证失败

常见原因：

- 使用了网页登录密码，而不是授权码或应用专用密码
- 邮箱网页端没有开启 SMTP 服务
- 用户名格式不对，应改为完整邮箱地址
- 授权码复制时多了空格
- 邮箱服务商触发安全风控，需要在网页端确认登录或重新生成授权码
- Outlook.com / Microsoft 个人邮箱要求 OAuth2/Modern Auth，当前版本不支持这种登录方式

### 测试邮件提示连接超时

常见原因：

- SMTP 主机名填错
- 当前网络无法访问邮箱服务商 SMTP 端口
- 端口被校园网、公司网或系统防火墙拦截
- 端口和安全性不匹配

可以尝试：

- 将 `587 + TLS` 改为服务商支持的 `465 + TLS`
- 换一个网络后测试
- 检查代理、防火墙或安全软件策略

### 测试邮件发送成功，但收件箱没有邮件

请检查：

- 垃圾邮件箱
- 邮箱服务商的拦截记录
- 收信人邮箱地址是否填错
- 发信频率是否过高导致被临时限流

## 🔗 官方帮助入口

以下链接可用于查找服务商最新 SMTP、授权码和认证方式说明：

- QQ 邮箱帮助中心：https://service.mail.qq.com/
- QQ 邮箱 SMTP 参数参考：https://main.qcloudimg.com/raw/document/product/pdf/1270_46586_cn.pdf
- 网易邮箱帮助中心：https://help.mail.163.com/
- 网易邮箱客户端授权密码说明：https://help.mail.126.com/faqDetail.do?code=d7a5dc8471cd0c0e8b4b8f4f8e49998b374173cfe9171305fa1ce630d7f67ac2f7273b721cc829cb
- Gmail App Passwords：https://support.google.com/accounts/answer/185833
- Gmail SMTP settings：https://support.google.com/a/answer/176600
- Outlook.com POP/IMAP/SMTP 设置：https://support.microsoft.com/en-us/office/pop-imap-and-smtp-settings-for-outlook-com-d088b986-291d-42b8-9564-9c414e2aa040
- Outlook.com Modern Auth 说明：https://support.microsoft.com/en-us/office/modern-authentication-methods-now-needed-to-continue-syncing-outlook-email-in-non-microsoft-email-apps-c5d65390-9676-4763-b41f-d7986499a90d
- Resend SMTP 文档：https://resend.com/docs/send-with-smtp
- Resend 域名管理文档：https://resend.com/docs/dashboard/domains/introduction
- Resend API Key 文档：https://resend.com/docs/dashboard/api-keys/introduction

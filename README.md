# 兔U Windows 桌面播放器 · 简洁列表版 0.4.1

C# / .NET 10 / WPF，播放引擎为 LibVLCSharp + LibVLC。

## 运行

打开 `artifacts/win-x64-accessible/BunnyU.exe`，或双击项目根目录的 `启动兔U.cmd`。发布包包含 .NET 运行时和 VLC 播放组件，不需要另外安装 VLC。

请保留整个运行目录，不能只复制 EXE；播放器插件在 `libvlc/win-x64` 内。

## 使用

界面参考本机 Maoer-FM：顶部菜单、搜索框、中间表格列表、底部状态栏。

1. 输入作品名并按 Enter 搜索，或点击搜索按钮。
2. 双击作品或按 Enter，当前列表进入章节；点击“返回搜索结果”或在章节表中按 Backspace 返回。
3. 双击章节或点击“播放”，打开简洁播放器，提供暂停、进度、音量、倍速和切章。
4. 通过“账号 → 验证码登录”打开登录对话框。
5. Ctrl+F 定位搜索；Alt+A 打开账号菜单；Alt+P 打开播放菜单。
6. 关闭播放器会停止声音；返回主列表不停止播放。关闭主窗口退出程序。

仅保留基础登录、搜索和播放，没有加入下载、充值、会员、收藏等菜单。使用 WPF 系统控件和表格，不再使用卡片与大型封面布局。

验证码只会在你点击按钮时发送，不会自动重试或自动发送。登录凭据使用 Windows DPAPI 加密，保存在当前 Windows 用户的 `%LOCALAPPDATA%/BunnyU.Windows/session.dat`。不会保存验证码；“退出本机登录”会清除本机凭据，不调用服务端注销账户接口。

## 支持内容与限制

- 验证码请求与登录、作品搜索、章节列表、音频播放、播放控制。
- 支持 MP3、HLS/m3u8；可解析原客户端正常播放流程使用的音频地址封装。
- 作品权限以服务响应为准。VIP/购买标记不当作授权；无法确认完整播放权限时仅试听或保持锁定，试听长度同时在 UI 和播放引擎中限制。
- 当前尚未完整对接会员到期、购买订单和跨端收听记录；已付费账号的部分章节可能仍显示试听。此版本不提供购买、下载和付费限制绕过功能。
- 服务接口为现有客户端接口，后续服务变更可能需要更新适配。

## 开发

```powershell
.\build.ps1
.\build.ps1 -Publish
.\build.ps1 -Publish -LiveTests
```

脚本优先使用工作区 `../tools/dotnet10/dotnet.exe`，其他机器需安装 .NET 10 SDK。`LiveTests` 只调用匿名搜索和公开章节 GET，不发送短信、不登录账号。

| 项目 | 内容 |
|---|---|
| BunnyU.Core | API 协议、返回解析、音频地址解析、权限标记 |
| BunnyU.Desktop | 菜单、搜索和表格主窗口，登录与播放器对话框 |
| BunnyU.Tests | 可运行的离线协议/解析测试及可选匿名联网检查 |

## 协议依据

- 登录短信：`POST https://api.zuidie.net/v1/user/send/msgcode`，multipart：`account`、`scene=1`、`sendType=1`。`scene=3` 属于密码恢复，不用于本流程。
- 验证码登录：`POST https://api.zuidie.net/v1/user/login/pwdOrCode`，multipart：`mobile`、`code`、`password`（空）、`type=android`、`channel=1`、`deviceId`。
- 搜索：`GET https://bunny.zuidie.net/v1/search/all`。
- 章节：`GET https://bunny.zuidie.net/v1/album/sound/index`，`sort=0`。
- 登录 deviceId 是本地生成的随机标识；不读取电脑硬件标识。

接口及场景参数来自本工作区的 APK 静态调用证据。已通过真实匿名搜索、章节和公开音频解码测试；**真实短信发送及账号登录仍需使用者自己的手机号/验证码完成验证**。

详细验证结果见项目内 `VALIDATION.md`。

## 读屏和键盘

列表使用标准 ListView，保留多列表头。F6 聚焦当前列表，Tab 进入列表，上下方向键选项，Enter 打开作品或播放章节，Backspace 返回搜索结果。搜索完成与进入章节时聚焦实际第一条列表项，条目名称包含作品/章节名称及必要信息。

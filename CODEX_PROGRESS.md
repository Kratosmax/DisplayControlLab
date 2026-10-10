# DisplayControlLab 接续记录

最后校准：2026-10-10。版本唯一来源：Directory.Build.props（0.2.0）。

## 接手校准

```powershell
git status --short
git branch --show-current
git log -5 --oneline
git remote -v
git tag --list
Get-Content Directory.Build.props
```

先读本文件、README.md、当前相关源码；用 Git 和版本源校准，不把历史测试当成本轮通过。

## 当前快照与授权

- 用户已要求记录进度、上传本人 Git、先发布读取器，并明确引用 desktop-tool-ui-release 技能。
- GitHub 登录账号核验为 Kratosmax；项目起初没有 Git 仓库或 remote，账号下未发现 DisplayControlLab 仓库。
- 用户已确认创建公开 Kratosmax/DisplayControlLab，main 分支、MIT 许可证，并上传源码与发布读取器 0.2.0。该授权仅用于本轮，不扩大为后续自行发布。
- 拟首次发布 v0.2.0：读取器 Full Setup、Lite Setup、Full ZIP、Lite ZIP 和 SHA256SUMS.txt。控制器骨架随源码上传，不包含在读取器包中。
- 用户已确认首版手动下载更新，不加入自动更新器或更新清单。没有已发布旧客户端，不声称验证过自动升级。
- 公开仓库 https://github.com/Kratosmax/DisplayControlLab 已创建并核验。main 首次提交、标签和 Release 正在执行；完成后必须更新线上核验状态。

## 功能现状

读取器识别活动显示器、显卡、接口、桌面分辨率、刷新率、Windows HDR、EDID；查询声明的 DDC 功能和少量常用项。首次建立内存基线，后续比较当前值，记录前后值及备注。

logs/current.jsonl 正常启动时清空，记录立即刷新；子读取不清空它。observations 按连接自动保存最新资料，启动保留。controls 只收录返回数值的项；失败、跳过和完整证据留在 raw。资料只标 observed，不冒充 verified。

Controller 是独立配置检查骨架，没有显示器写入。用户选择“先搭建双进程项目和配置格式，确认设计后再做控制”。自动采样、完整字段比较、扩展探测、候选推断、验证及真实控制未实现。读取比较不等于通信抓包。

## 关键入口

- src/Reader/Program.cs：启动、日志、限时子读取和诊断入口。
- Native.cs / Models.cs：Windows GET、EDID/VCP 和模型。
- ReadSession.cs：按连接隔离基线、失败保留有效值。
- AuditLog.cs / ObservationStore.cs：日志、设备资料保存。
- MainWindow.xaml / .xaml.cs：WPF UI；Tests.cs：解析、内存、日志和资料测试。
- src/Controller/Program.cs、config/observation.schema.json：配置检查与契约。
- build/package-reader.ps1 / reader.iss：四种包和 SHA256。
- .github/workflows/build.yml：云端构建测试；RELEASE_NOTES_CURRENT.md：当前发布说明。

## 约束与协作

只读阶段不引入写入、恢复出厂或默认暴力探测。数值关联不是因果验证；控制时核对设备身份、值域、效果和恢复。没有唯一身份时不承诺跨接口自动匹配。

密钥、令牌、日志、设备资料和测试缓存不提交，不记录设备专属绝对路径。不得未经授权安装或复制全局工具到私有目录。

版本从 Directory.Build.props 推导；使用仓库级身份“小火车 <kratosthemax@gmail.com>”，不改全局 Git。README 保留使用、自编译、AI 接续三个入口。

## 可重跑验证

```powershell
dotnet build DisplayControlLab.slnx -c Release -m:1
New-Item -ItemType Directory -Force temp/validation | Out-Null
$p = Start-Process src/Reader/bin/Release/net10.0-windows/DisplayControl.Reader.exe -ArgumentList '--self-test temp/validation/tests.txt' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Tests failed' }
Get-Content temp/validation/tests.txt
./build/package-reader.ps1
git diff --check
```

沿用全局 .NET 10 SDK / Inno Setup 6，不创建工具副本。2026-10-10 历史验证：18 项测试、Dell DP/HDMI 26 项读取、活跃日志及子读取不清空、Controller 检查和拒绝异常资料、真实 WPF 渲染。发布前复查本轮包。

AOC、MSI、RX 7900 XT、HDR 实机、其他 DPI、安装包实际安装和自动更新未验证。禁止把这些写成兼容保证。

## 当前待办与维护

用户已确认建仓和首版手动更新方案。四种包已构建；Lite/Full 各 18 项测试、ZIP 内容检查和 WPF 渲染通过。首次提交推送、发布和线上校验尚在进行。安装器编译通过，实际安装未测试。

每次交付同步版本、待办、修改文件、实际测试、线上状态；删除失效内容，不记逐轮聊天。未推送不能称跨设备接续可用。当前授权不等于后续自动发布授权。

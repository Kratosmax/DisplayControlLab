# DisplayControlLab

Windows 显示器控制器与读取器，使用 .NET 8 / WPF。标准 DDC 内置，私有协议与专用驱动插件由用户按需安装。当前版本 0.3.0。控制器与读取器共用工作区和用户资料。

## 下载与升级

[最新版本与四种下载包](https://github.com/Kratosmax/DisplayControlLab/releases/latest)

| 包 | 如何选择 |
|---|---|
| Full Setup | 自带 .NET 8，当前用户安装与卸载 |
| Lite Setup | 已安装 .NET 8 Desktop Runtime x64，安装前检查依赖 |
| Full ZIP | 自带运行库，解压即用 |
| Lite ZIP | 已有运行库，体积更小，解压运行 |

0.2.0 没有自动更新器，需手动升级一次；旧版记录可在读取器只读导入。新版本在“设置与帮助”检查更新，启动后也可后台检查。显示当前/目标版本、经签名的公告、大小、下载阶段与线路，支持稍后、跳过、取消及手动下载。更新前退出另一工具并等待显示器操作完成；更新器不会强制关闭程序。用户资料及手动安装的适配包不会随软件更新被覆盖。

网络设置提供 GitHub URL 前缀线路和独立 HTTP 网络代理：优先级 0 禁用、10 最高，同级按列表顺序；直连不可删除但可禁用。支持 HTTPS/HTTP 前缀及无账号 HTTP 代理，不支持 SOCKS。可用未保存配置检查一次，保存后才影响后台检查。前缀代理会看到完整 GitHub URL；签名、哈希和包验证不会因为使用代理而跳过。

## 开始使用

打开 `DisplayControl.Controller.exe`，启动会预读所有显示器，切换使用缓存。亮度与对比度拖动后合并应用；枚举直接选择，结果以回读为准。顶部可切换详细/紧凑视图，记住选择；设置页可改成手动应用。读取异常会有限重试并提供单项“重试读取”；内置标准亮度SET报错时先回读，仅在原值/范围未变且设备/HDR复核通过后补发一次，其他设置不自动重发。旧值明确标记为上次确认值。

特殊功能在“适配包”导入 JSON 或 ZIP，再“应用到当前设备”。安装本身不执行指令。主屏完整包位于 `examples/aoc-q27g4zmn/adapter.json`，副屏包位于 `examples/msi-g273q`。每台只需一个型号包，包内各功能可选择不同通信通道，不会自动安装；首次应用型号包可默认采用其声明的标准功能定义；已有明确来源选择保留。用户仍可按需组合第三方扩展，非默认来源的重名冲突需明确选择。

“我的方案”选择项目保存、预检、应用。“操作历史”查看结果、重新核查和恢复已确认修改项；恢复冲突时拒绝覆盖外部调整。只恢复采集过的范围，不承诺撤销所有 OSD 联动。

`DisplayControl.Reader.exe` 提供设备检查、快照比较、分段实验与协议工作台。控制器“在读取器研究”自动携带设备和功能上下文；检查、记录变化及协议创作在读取器完成。两个入口分别单实例，共用资料。

[使用教程](docs/USER_GUIDE.md) · [适配包格式与开发](docs/ADAPTERS.md) · [本地验收与边界](docs/PRODUCT_VALIDATION.md) · [本轮12项改进验证](docs/LIVE_CONTROL_VALIDATION.md) · [两屏适配研究](docs/MONITOR_ADAPTER_VALIDATION.md) · [实机证据](docs/CAPABILITY_VALIDATION.md) · [AI 接续](CODEX_PROGRESS.md)

## 运行与资料

Windows x64；Lite 需要 .NET 8 Desktop Runtime x64，Full 包可包含运行环境。AMD 私有后端需要系统 AMD ADL 驱动。封闭驱动通过独立插件接入，前提是厂商接口可调用；进程隔离不是安全沙盒。

资料统一保存到 `%LOCALAPPDATA%/DisplayControlLab`：adapters、state.json、snapshots、experiments、presets、operations、drafts、protocol-tests 和 logs。两个入口不在安装目录写资料。旧记录可从快照页只读导入，原文件不删除；测试通过 DISPLAYCONTROL_DATA 使用项目 temp 隔离目录。

Windows HDR 与显示器 HDR 预设是不同状态。AOC/B411在显示器HDR开启或未知时阻止色温、对比度等冲突写入；软件进入前保存色温，退出确认后恢复，有记录才恢复。其他型号可由适配包声明HDR互锁。控制器提供系统 HDR 开关（需要 Windows/驱动支持专用 API），切换后等待连接稳定并自动重读设备状态和模式选项，最长约20秒有界恢复，期间暂停目标调节，不自动重发预设。没有自动抓取未知私有协议的承诺。区域调光等未取得有效协议的项目显示待适配；读取失败、条件锁定、只写和来源冲突分别处理。

## 自行构建与验证

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/platform
$p = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/ui' -WindowStyle Hidden -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'UI tests failed' }
./build/package.ps1 -LiteOnly -PreviewOnly
```

UI 验证使用模拟设备和真实 WPF 离屏渲染，不显示窗口、不移动鼠标、不写硬件。预览包输出到 temp/preview。完整四包构建需要 Inno Setup 6：`./build/package.ps1`。签名发布工具读取 `DISPLAYCONTROL_SIGNING_KEY`，私钥只存受控环境，禁止进入源码、日志或包。

两个入口共用 CLI：`--devices`、`--report 文件`、`--install 包`、`--bind Windows设备名 包id`、`--read Windows设备名 功能id`、`--write Windows设备名 功能id 值`。CLI 绑定为追加协议包，不会静默信任代码插件。HDR 推荐传稳定选项 id `game` / `off`，兼容包中的中文显示名；不固定传 3/4。

开发结构：Core 为设备/传输、能力/路由、资料、方案/恢复及进程接口；UI 为两入口共享的工作区和页面；Reader/Controller 仅为入口。examples 是可选示例，tests/Platform 是模拟与接口验证。旧窗口、内存 ReadSession 和旧日志重置流程已删除。

## 更新开发与发布

自动更新模块在 `src/Core/Updates`，统一设置页在 `src/UI/UpdatePages.cs`，独立替换器在 `src/Updater`。参考 PinNote 的模块职责，并独立使用本产品公钥；公告本身参与签名。开发目录缺少安装身份时仅允许手动下载；Full 与 Lite 使用不同清单且禁止跨通道。

```powershell
dotnet run --project tests/Updates/DisplayControl.Update.Tests.csproj -c Release -- . temp/validation/updates temp/preview/DisplayControlLab-0.3.0-Lite.zip Lite
# 完整发行构建（需要 Inno Setup 6）
./build/package.ps1
# 仅在受控签名环境执行；私钥由环境注入
dotnet run --project build/ReleaseTools/ReleaseTools.csproj -c Release -- temp/release RELEASE_NOTES_CURRENT.md
```

标签触发 `.github/workflows/release.yml`，从唯一版本源构建四包、执行验证、签名两通道清单、生成 SHA256，并只使用当前版本说明发布。GitHub Secret `DISPLAYCONTROL_SIGNING_KEY` 为发布私钥。客户端 CLI `--update-inspect ZIP 清单` 复用正式包预检，`--update-check` 只检查，`--update-prepare 清单` 仅下载/预检、不启动替换器。

AI 接手先读 [CODEX_PROGRESS.md](CODEX_PROGRESS.md)，再核对 Git、版本源、适配包、更新公钥及测试。更改更新协议须覆盖真实 ZIP、不可定位压缩流、文件占用与回滚、后台/手动网络线路和两通道；收尾同步接续文档。普通测试不写硬件，不启动窗口；受控实机脚本含 SET，不当只读测试运行。

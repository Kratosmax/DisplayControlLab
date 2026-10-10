# DisplayControlLab

显示器控制研究项目：读取器与控制器为两个独立进程，通过落盘资料协作。第一阶段是项目骨架和配置契约，不实现控制写入。

[下载读取器](https://github.com/Kratosmax/DisplayControlLab/releases/latest) · [AI 接续进度](CODEX_PROGRESS.md) · [MIT 许可证](LICENSE)

## 现有入口

- `DisplayControl.Reader.exe`：基于现有 MonitorInspector 的只读 Windows UI。第一次读取记住当前值，后续读取自动比较；每次正常启动清空 `logs/current.jsonl`，采样完成立即落盘完整读数和变化；自动保存 `observations/设备键.json`。读取进程使用 `--ddc` 子模式，绝不清空主进程日志。
- `DisplayControl.Controller.exe --inspect observations/设备键.json`：独立配置检查进程；检查格式、身份字段和验证级别，并列出控制项，不加载读取器，不发送控制命令。
- 两个程序发布到同一个目录，共享其中的资料目录。控制器启动不会清空读取器日志。

读取源码已收纳到 `src/Reader`，克隆本仓库即可编译，不依赖相邻目录。当前控制器为命令行骨架，图形控制界面尚未开发。

## 资料生命周期

| 目录 | 内容 | 读取器启动时 |
|---|---|---|
| logs/current.jsonl | 本轮时间、设备、操作备注、原始值、变化、错误 | 清空旧日志 |
| observations | 每个连接最新读取结果，自动保存 | 保留 |
| profiles | 后续经过验证的功能映射和值域 | 保留；第一阶段尚不生成 |

JSONL 每行一条完整 JSON，写入后立即刷新，AI 可一边运行一边读取。启动失败或日志被另一实例占用会明确报错，不能用第二实例悄悄清空正在使用的日志。目录需可写。

## 识别与控制边界

`observed` 表示读到数值；`candidate` 表示有关联操作和重复变化线索；`verified` 表示后续核验了设备、码、值、效果和恢复方法。现阶段生成的资料只标记 observed，不会把用户备注或通用码名称自动提升为 verified。

自动识别先产生候选，不自动执行未知码。一次操作可能联动多个值，只有多次 A→B→A 的稳定变化，结合实际 OSD/画面和受控验证，才能收敛映射。

控制器未来直接向显示器发送已验证命令，无须保持读取器运行。但至少需要重新枚举当前连接、核对设备身份；实际写入应带失败处理和必要回读，而不是仅相信磁盘旧数据。设备没有唯一序列号时，不能保证跨接口自动匹配。

## 后续设计（未实现）

1. 补全比较当前值、最大值、类型、读取状态和新增 / 消失控制项，避免仅比较当前值。
2. 可启停的自动采样：默认每轮完成后等待 3 秒，绝不并发读取同一显示器；记录开始、结束、耗时，允许停止。首次读取能力字符串，常规轮次尽量只查询已知项。
3. 候选发现：按单一操作记录重复变化、反向恢复、无操作对照，生成待验证映射；未声明码探测作为独立、明确范围的诊断模式，不默认全范围扫描。
4. 控制 UI：先做亮度、对比度、RGB；只启用已验证控制项，其余显示待验证。HDR OSD 预设与 Windows HDR 开关分别建模。
5. 真正通信抓包另设后端：截取官方软件发出的 DDC/驱动请求，或硬件 I2C 分析。当前读取比较不等于该能力，也不能发现所有只写接口。

## 使用者教程

首次发布版本：0.2.0。下载包只包含读取器；控制器尚未提供屏幕控制功能。

| 下载 | 适用情况 |
|---|---|
| Full Setup | 希望安装、创建开始菜单入口，不另装运行库 |
| Lite Setup | 已有 .NET 10 Desktop Runtime x64；安装器检查依赖 |
| Full ZIP | 免安装，包含运行库 |
| Lite ZIP | 免安装，已有运行库，体积最小 |

Windows x64 桌面环境。最低验证环境为 Windows 11；Windows 10 未实测。Lite 依赖可从 <https://dotnet.microsoft.com/en-us/download/dotnet/10.0> 下载 **Desktop Runtime x64**。

便携版解压到可写目录，双击 `DisplayControl.Reader.exe`；安装版为当前用户安装，不需要管理员权限。选择显示器，点击“读取并比较”建立基线，手动操作显示器后再次读取。“刚才的操作”是可选备注。日志在程序目录 logs/current.jsonl，每次正常启动清空；observations 中的设备资料保留。需要保留上一轮调试日志时，在重新启动前复制它。

读取器只读，不写入显示器设置。DDC/CI 未读到值不等于硬件不支持；请检查显示器 DDC/CI 开关和直连方式。HDR 状态不是显示器 HDR 预设；本版无法保证获取所有菜单功能。日志被另一实例占用时，关闭同目录的旧实例再启动。

首版采用手动下载更新，没有自动更新器。安装版可从 Windows 应用列表卸载，卸载保留运行中生成的日志和资料；便携版关闭后删除解压目录即可。自生成资料如需删除，请另行确认并处理。

## 自行编译

需要现有 .NET 10 SDK / Desktop Runtime。

```powershell
dotnet build src/Reader/DisplayControl.Reader.csproj -c Release
dotnet build src/Controller/DisplayControl.Controller.csproj -c Release
dotnet publish src/Reader/DisplayControl.Reader.csproj -c Release --no-restore --self-contained false -o temp/Lite-preview
dotnet publish src/Controller/DisplayControl.Controller.csproj -c Release --no-restore --self-contained false -o temp/Lite-preview
```

`config/observation.schema.json` 定义资料格式。后续控制实现前需确定验证流程、设备身份和允许的功能范围。

本地打包使用已有 Inno Setup 6，不自动安装或创建工具副本：

```powershell
./build/package-reader.ps1
```

四种包输出到 temp/release，SHA256SUMS.txt 覆盖全部包。正式包无日志、设备资料、测试缓存或控制器二进制。需要离线验证时运行读取器 `--self-test 输出路径`；EXE 为图形程序，脚本应使用 Start-Process -Wait 并检查退出码。完整复跑命令和本轮结果见 CODEX_PROGRESS.md。

## AI 继续开发入口

先完整读取 [CODEX_PROGRESS.md](CODEX_PROGRESS.md)，再检查 Git 状态、remote、版本源 Directory.Build.props 和当前相关源码。不要把旧测试记录当成本轮测试结果。

- src/Reader：Windows 只读接口、EDID/VCP 解析、UI、内存对比、日志及资料落盘。
- src/Controller：配置检查骨架，没有写入接口。
- config：数据契约；build：四种读取器包；.github/workflows：云端构建与测试。
- 不得把 observed/candidate 自动升级为 verified；不得默认暴力探测厂商控制码；读取失败不得推断不支持。
- 日志、原始设备资料、密钥、临时截图、bin/obj/temp 不提交。收尾同步 CODEX_PROGRESS.md。
- Git 身份使用仓库级配置，不修改全局；发布授权只记录当前会话的用户指示，不作为后续自行发布的永久授权。

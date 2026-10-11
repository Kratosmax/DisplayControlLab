# DisplayControlLab 接续状态

最后校准：2026-10-11。版本源 Directory.Build.props：0.3.0，.NET 8 / WPF / Windows x64。origin 为 https://github.com/Kratosmax/DisplayControlLab.git，main；本次重构基线 fbc8b878434aa20684ea7de60d35be2a39623c2e。用户明确授权本轮提交、推送、标签、四种包发布，以及参考 PinNote 实现更新和网络代理。最终本地候选验证已完成，尚未发布/核验线上新版本。没有使用子代理。

## 接手校准

先读本页、README、版本源、相关源码和 docs/UPDATE_VALIDATION.md，再核对 git status --short、git log -3 --oneline、git remote -v、gh release list --repo Kratosmax/DisplayControlLab。原厂商研究、实验及回滚资料在项目 temp，不提交或删除。仓库级身份按技能使用小火车 <kratosthemax@gmail.com>，不改全局配置。

## 当前产品

控制器/读取器两个薄入口，共享 Core 与 UI。控制器负责操作、定向回读、方案和历史；读取器负责设备检查、快照、实验与协议工作台。通用 DDC 内置，协议/驱动插件由用户编辑、生成、安装；每台默认一个型号包，按功能选择通道。详细视图与两列单行紧凑视图共存，偏好持久化；两设备启动预热、写队列与意图合并、稳定顺序和滚动、功能帮助均已实现。

主屏 AOC Q27G4ZMN / AOCB411 包 1.5.0；副屏 MSI G273Q / MSI3CA8 包 1.1.0。标准功能走 Windows DDC，已核实私有功能目前走 AMD ADL，未实现 NVIDIA/Intel 私有通道。协议包不随软件更新强制覆盖。实际范围与缺口见 docs/OSD_CONTROL_VALIDATION.md：帧计数左侧两档 SET3 回读4、SET4 回读3，暂禁用；区域调光/色域/Adaptive-Sync 缺有效协议，游戏模式/比例缺完整联动恢复验证，保持只读。不得猜未知 SET。

HDR 开启/未知时禁止已知冲突功能；进入前保存色温，确认退出后恢复。系统 HDR 变化等待连接稳定与重载，不重发预设。写入只回读目标与声明联动，禁止退回全量扫描。MSI 内置标准亮度允许最多两次有条件 SET，其他操作不套用。普通更新测试不得写实机或切 HDR。

## 更新与发布模块

src/Core/Updates：安装身份、文件租约、签名清单（公告也参与签名）、双层网络路由、大小/停滞限制、下载暂存、真实程序集/ZIP/路径/通道预检、事务备份替换与启动恢复。src/Updater 在安装目录外运行，核实主进程创建时间、最多等待60秒及两工具文件租约，无强杀。软件旧文件按安装清单清理，用户非软件文件保留。用户资料仍在 LOCALAPPDATA/DisplayControlLab，测试通过 DISPLAYCONTROL_DATA 隔离到项目 temp。

src/UI/UpdatePages.cs 复用原主题、卡片、按钮与下拉，位于统一设置页。后台检查不激活窗口，未保存网络值可临时手动检查，保存后才影响后台。提供当前/目标版本、签名公告、大小、阶段/线路、进度、稍后/跳过/取消和手动下载。显示器操作未完成拒绝更新交接，草稿保存后退出。Full/Lite 禁止跨通道；缺安装身份的开发目录只允许手动下载。0.2.0 正式版无更新器，必须手动过渡一次，不能冒称旧版自动升级成功。

build/package.ps1 输出四包及身份；build/ReleaseTools 在受控签名环境生成 update.json、update-lite.json、update-full.json 并核验实际 ZIP。公钥在 UpdateTrust.cs。私钥仅存 GitHub 仓库 Secret，本机临时恢复副本用 Windows 当前用户保护，不得输出、提交或入包。release.yml 标签触发四包构建、核心/UI/两通道验证、签名与发布；build.yml 负责 main/PR。当前版本说明仅 RELEASE_NOTES_CURRENT.md，历史多版本说明不可直接发布。

## 验证与证据

此前主屏14次真实 SET、19项状态恢复证据在 temp/validation/game-controls，不当本轮更新测试。此前核心172项、UI68项；本轮各阶段核心172项、更新专项每通道51项与 UI81项已执行。正式候选最终结果以 temp/validation/updates 与 docs/UPDATE_VALIDATION.md 收尾核准，不混作云端产物的结果。

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/platform
$p = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/ui' -WindowStyle Hidden -Wait -PassThru
./build/package.ps1 -LiteOnly -PreviewOnly
./build/package.ps1
dotnet run --project tests/Updates/DisplayControl.Update.Tests.csproj -c Release -- . temp/validation/updates temp/release/DisplayControlLab-0.3.0-Lite.zip Lite
```

WPF 使用原生离屏宿主，不能用 Playwright 驱动；不 Show、不抢焦点。打包需要 Inno Setup 6，已找到当前用户安装。下载与生成缓存放项目 temp。实际鼠标下拉、跨屏 DPI、其他 Windows 版本及全部硬件固件未全面验证。用户没有自行运行测试命令记录。

## 当前待办

最终双通道包/签名/外部更新器/安装器验收已通过（172核心、51×2更新、81打包UI）；完整交接与静默安装/卸载均成功，用户资料哨兵保留。现核查源码无秘密与旧失效入口，提交推送 main、创建0.3.0标签，等待 Actions 发布四包及签名清单，独立下载线上包验签/验哈希/通道/入口版本，并同步本页。没有额外用户审批阻塞。线上核验前不得宣称发布完成。后续协议缺口不属本轮扩展范围。

## 维护

收尾更新真实提交/标签/Release/Actions状态、完整修改清单、重跑命令与测试缺口。历史实验、截图、临时受保护密钥缓存和用户日志不入 Git。跨设备接续仅在文档与源码提交并推送后成立。未来发布权限按届时用户请求核对，本页不自动扩张授权。

本轮实际变更：src/Core/Updates 下7个模块、src/Updater、src/UI/UpdatePages.cs、WorkspaceWindow.cs、ToolHost.cs；tests/Updates；build/package.ps1、suite.iss、ReleaseTools、verify-updates.py、verify-installers.py；Directory.Build.props、DisplayControlLab.sln、两工作流、README、USER_GUIDE、UPDATE_VALIDATION、RELEASE_NOTES_CURRENT、docs/releases/v0.3.0.md及本页。累计重构清单以最终Git提交为准，temp/validation/updates/package-check.json与changed-files.json保存本机哈希。主屏包与协议研究本轮未改；更新测试无硬件SET。

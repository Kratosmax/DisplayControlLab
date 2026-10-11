# DisplayControlLab 接续状态

最后校准：2026-10-11。版本源 Directory.Build.props：0.3.1，.NET 8 / WPF / Windows x64。origin 为 https://github.com/Kratosmax/DisplayControlLab.git，main；本次重构基线 fbc8b878434aa20684ea7de60d35be2a39623c2e。此前用户授权的四包发布、更新与网络代理工作已完成。0.3.1已发布并完成线上独立验签、哈希和真实主程序更新检查/下载预检，发行代码提交f0c147c3b9daff388c83f7b06eb84aefb8a9615f，标签v0.3.1。本轮从49d28becb2b006bd98c9f3e33c0c06bace92532e接入用户确认的图标；用户明确要求推送、不重新打包。版本与既有Release保持不变，没有使用子代理。

## 已确认图标与本轮验证

用户确认前屏A2六色色环、后屏B2五色色谱及黑白分区、B双屏叠层。唯一资产在assets/branding：displaycontrol.svg、displaycontrol.png、displaycontrol.ico，说明在该目录README.md。ICO含16/20/24/32/40/48/64/96/128/256十档。

本轮完整变更：上述四个资产/说明；src/Controller/DisplayControl.Controller.csproj、src/Reader/DisplayControl.Reader.csproj、src/Updater/DisplayControl.Updater.csproj接入exe图标；src/UI/DisplayControl.UI.csproj嵌入共享图像资源；src/UI/WorkspaceWindow.cs为窗口、弹窗及页头接入图标；build/suite.iss为下一次安装器构建指定同一ICO；README.md展示图标；本页更新接续信息。

2026-10-11实际运行dotnet build DisplayControlLab.sln -c Release -m:1，0警告/0错误；控制器--ui-test temp/validation/branding/controller，81项通过；读取器--capture temp/validation/branding/reader.png check --simulation，真实WPF离屏渲染成功。已查看页面渲染，页头图标无裁切。测试使用模拟数据、隔离资料，不Show、不抢焦点、不写硬件。Windows资源只读验证确认三个exe的十个图标帧与源ICO逐字节一致，证据在本机temp/validation/branding/icon-verification.json；ICO SHA256：3cc0a7bc540605bd1315ec97fdbcb8d851c404b06d797b3c54de7fc52ea1e0be。

按用户要求未执行打包、未改版本、未创建标签或Release；安装器此次仅校准源码引用，未重新编译或安装验收。main工作流会生成预览包，因此本轮提交使用[skip ci]避免云端重新打包，本地构建及UI验证已完成。实际系统标题栏/任务栏和Explorer图标缓存未前台验证。后续正式包将在下一次经用户授权的发布中纳入图标，本轮不覆盖既有0.3.1资产。推送完成状态以Git远程main与本地HEAD相等为准。

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
dotnet run --project tests/Updates/DisplayControl.Update.Tests.csproj -c Release -- . temp/validation/updates temp/release/DisplayControlLab-0.3.1-Lite.zip Lite
```

WPF 使用原生离屏宿主，不能用 Playwright 驱动；不 Show、不抢焦点。打包需要 Inno Setup 6，已找到当前用户安装。下载与生成缓存放项目 temp。实际鼠标下拉、跨屏 DPI、其他 Windows 版本及全部硬件固件未全面验证。用户没有自行运行测试命令记录。

## 发行与当前待办

0.3.1已完成本次目标。Release：https://github.com/Kratosmax/DisplayControlLab/releases/tag/v0.3.1 。发行Actions 38139976095成功；main CI 38139973071成功。Full/Lite Setup与ZIP、update.json/update-lite.json/update-full.json、SHA256SUMS共八资产齐全，latest指向v0.3.1。

本地与云端：172核心、两通道各51更新专项、81离屏UI，签名实际ZIP，外部更新器交接/替换/CLI重启/启动清理，两个安装器静默安装/卸载均通过。线上八资产已独立下载，大小与GitHub digest/SHA256SUMS一致；两个通道的发行控制器入口真实获取latest清单、验签/版本与通道、下载更新包及主程序预检均成功。验证仅使用temp隔离资料，未执行用户安装的软件替换，无硬件SET。直连较慢时，隔离验收采用用户PinNote已有前缀线路；未修改默认用户网络资料。证据在temp/validation/updates/online/verification.json、release-api.json与package-check.json。

0.2.0实际正式ZIP已检查，确无更新器，必须手动过渡一次。未来有更新器的上一正式版发布验证须使用其原始二进制，不以重编旧版或模拟旧身份替代。当前不存在其他已授权未完成事项。协议缺口继续按上方边界保留；等待用户后续需求，不从未适配清单自行扩大范围。

## 维护

收尾更新真实提交/标签/Release/Actions状态、完整修改清单、重跑命令与测试缺口。历史实验、截图、临时受保护密钥缓存和用户日志不入 Git。跨设备接续仅在文档与源码提交并推送后成立。未来发布权限按届时用户请求核对，本页不自动扩张授权。

本轮实际变更：src/Core/Updates 下6个模块、src/Updater、src/UI/UpdatePages.cs、WorkspaceWindow.cs、ToolHost.cs；tests/Updates；build/package.ps1、suite.iss、ReleaseTools、verify-updates.py、verify-installers.py；Directory.Build.props、DisplayControlLab.sln、两工作流、README、USER_GUIDE、UPDATE_VALIDATION、RELEASE_NOTES_CURRENT、docs/releases/v0.3.1.md及本页。累计重构清单以最终Git提交为准，temp/validation/updates/package-check.json与changed-files.json保存本机哈希。主屏包与协议研究本轮未改；更新测试无硬件SET。

v0.3.0候选Actions 38139324153在交接检验脚本初始化目录时报错，发布步骤被跳过，无Release。已补输出目录并递增0.3.1；保留原标签，不重打。已推送重构提交b7dce437a191e52fcd63089f130757a55cfd01ca；main CI 38139150103成功。0.3.1发布与线上复核已通过。

线上Lite ZIP大小571094字节，SHA256 084e201052dd047e4f1be8ea9b6c64987751c1d77a64bc2cf2559e3b54a0f9a8；其他资产校验和以Release的SHA256SUMS为准。v0.3.0失败候选无Release，旧标签保留作审计。当前源码/文档已推送，可以跨设备按本页继续。

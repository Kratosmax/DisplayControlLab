# 12项控制体验改进验证

2026-10-11，本地未发布预览，.NET 8 / Windows x64 / WPF。

| 用户问题 | 当前实现与证据 |
|---|---|
| 1 单次修改全量重读 | TargetedControl.SetFeature 只处理目标、条件及联动项；worker 前值/SET/回读结果直接更新缓存。模拟请求计数确认亮度无其他项读取 |
| 2 启动预读两台 | LiveControls.WarmDevices 并行预热每台设备，常用项优先；切换已加载设备零额外请求 |
| 3 30回跳100 | NumericEditor 销毁时废弃旧草稿；同项队列合并、缓存修订防旧读取覆盖新设置。快速100→80→60→30模拟通过；真实设备曾回100原因未确定，回读不符明确显示未确认 |
| 4 应用按钮 | 自动模式隐藏，手动模式才显示；输入/点击发送次数已测 |
| 5 两工具职责 | 控制器日常控制与包安装管理；读取器检查、实验、适配创作/导出。上下文传设备/功能快捷导航 |
| 6 HDR切换同步 | 2秒元数据轮询不做DDC；发现系统HDR改变，作废旧选项，等650ms后重读该显示器。读当前预设及新范围，不自动重发预设 |
| 7 预检速度 | 普通控制不重复全量Capture；仅条件缺失/变化核查所需项。身份、内容哈希、来源、条件和设备锁仍保留 |
| 8 HDR/SDR区别 | 紫色●HDR、青色○SDR徽标；未知状态独立文案 |
| 9 软件系统HDR开关 | Windows专用GetAdvancedColorInfo2/SetHdrState接口；能力不支持则禁用。结构与模拟路径通过，未自动执行真实SET |
| 10 色温文字与设置 | 内置MCCS标准语义选项，设置后核查RGB；具体型号定义由包覆盖。真实色温SET未自动验证 |
| 11 数字状态转文字 | 标准输入、静音、电源、OSD、语言、色温及MCCS版本文本；示例帧计数位置已核实。未知编码不伪造解释，旧安装包未改写 |
| 12 刷新速度 | BatchReader同进程复用枚举/能力上下文，逐项GET；双设备并行。真实旧包副本预热约2847ms，不作为所有设备速度承诺 |

## 源码与重跑

关键源码：src/Core/TargetedControl.cs、BatchReader.cs、BatchClient.cs、DeviceControl.cs、WindowsHdr.cs、StandardOptions.cs；src/UI/LiveControls.cs、NumericEditor.cs、WorkspaceWindow.cs、PackagePages.cs、ToolHost.cs。

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/live-control/core
$p = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/live-control/ui' -WindowStyle Hidden -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'UI tests failed' }
./build/package.ps1 -LiteOnly -PreviewOnly
```

105项核心测试与36项真实WPF离屏测试通过，证据temp/validation/live-control/core/platform-tests.txt、ui/ui-tests.json。界面宿主从未Show，不抢焦点；原生WPF没有浏览器页面，以WPF渲染核验替代Playwright。实机只读截图与计时在real子目录；打包后验证在packaged-ui及package-check.json。用户本轮未提供自行运行测试命令。

微软API结构证据：win32metadata的wingdi.h、Microsoft Learn DISPLAYCONFIG_DEVICE_INFO_TYPE和DisplayConfigSetDeviceInfo文档；标准编码参照ddcutil vcp_feature_codes.c。原始资料在temp/research/hdr-api，不随产品分发。

## 验收边界

自动验证没有发送任何真实显示器设置或系统HDR切换。亮度旧值重发/缓存覆盖已有软件防护，但设备本身拒绝或延迟处理仍可能返回100；不把未确认结果说成成功。系统HDR真实黑屏恢复、驱动时序、新版亮度与色温写入仍需用户主动验收；DDC固有延迟和私有协议等待无法保证瞬时。

启动新版前关闭旧控制器和读取器，避免单实例转发到旧exe。不覆盖运行中的旧包、不自动安装协议、不修改用户原包或记录。未知私有功能与封闭SDK仍需相应包/插件，接口格式不能创造未知协议。

## 读取失败恢复修复（2026-10-11）

实机记录：14:55:11亮度目标94，写前GET失败，sent=false且前值/范围均空；前后的70/100/83操作成功。14:55:58系统HDR开启确认成功；14:56:00私有hdr-preset只读返回“响应格式或状态不符”，14:56:16重读成功（范围4），14:56:37游戏预设写入回读4成功。只能确认一次读取未成功及后续恢复，不能由此确定设备/驱动根因。

修复：写入流程中的标准GET遇Win32传输异常最多三次（120/250ms退避），SET不重发；普通全量扫描不增加逐项重试，避免不支持项拖慢启动；失败后自动只核查目标项；持续失败保留禁用的上次确认值、明确当前未知，并提供单项“重试读取”。系统HDR变化后初读失败项再等500/1000ms分别重读，成功项不扫描；旧模式的值/选项不得沿用。协议校验仍严格拒绝坏回包，不把异常响应解释成有效状态。

源码新增src/Core/ReadRetry.cs；修改DeviceControl.cs、SimulatedDisplayBackend.cs、src/UI/LiveControls.cs、WorkspaceWindow.cs、ToolHost.cs、tests/Platform/ProductTests.cs，以及本文件和USER_GUIDE/README/CODEX_PROGRESS。Release build零警告错误、110项核心测试、40项后台WPF测试通过。证据temp/validation/read-recovery/core、ui-final，截图brightness-read-failure.png，HDR请求计数hdr-recovery-requests.json。测试注入暂时/持续故障，不发送实机设置；真实故障再次出现时的效果仍需用户主动验收。原生WPF离屏渲染，不用Playwright，不Show窗口/抢焦点。

重跑沿用上方命令，将证据输出目录替换为temp/validation/read-recovery/core与ui-final。打包后结果见temp/validation/read-recovery/package-check.json，完整变更清单changed-files.json。当前Lite路径见temp/preview/preview-path.txt，大小/哈希见package-check.json及temp/preview/SHA256SUMS.txt；本地预览、未提交推送发布，先关闭旧两工具再用新版。没有用户自行运行测试命令记录。

## 显示器 HDR 互锁与退出色温恢复（2026-10-11）

用户报告开启显示器HDR后写色温/对比度会使设备异常。代码核对：旧版只检查部分包的系统WindowsHDR条件，标准色温/对比度没有显示器HDR互锁；宿主也未保存进入前色温。不能据此证明硬件根因，但不能继续开放已报告的冲突操作。

新增src/Core/HdrSafety.cs，协议hdrSafety字段（模式功能、关闭值、禁止功能、退出恢复）。AOC/B411提供旧包兼容保护底线，不改用户安装包；其他型号使用各自适配包声明，不把所有显示器套同一禁用表。AOC保护亮度、对比度、色温、RGB、Gamma、低蓝光及相关图像项；显示器HDR开或未知均阻止。UI禁用、方案预检、单项控制、CLI/草稿/插件宿主发送前都有校验；标准VCP改名别名仍识别。代码插件不是沙盒，无法约束插件私下自行发送未声明的其他指令。

进入显示器HDR前保存原色温；用户自定义色温同时保存RGB。读不到或不能恢复时拒绝本次开启，不在已进入HDR后采假基线。HDR模式内部切换不覆盖基线。关闭必须回读确认，才依次恢复色温及自定义RGB，每项重用设备/来源/哈希/模式/范围检查，失败停止并提示，基线与结果保存hdr-color-state。没有原记录、身份或来源变化时不猜恢复值。恢复记录一次消费，避免之后覆盖用户手动调整。该流程适用于由软件切换显示器HDR，单独切系统HDR或外部OSD切换不自动发送色温恢复。

126项核心、45项WPF离屏测试通过；证据temp/validation/hdr-safety/core-final、ui-final；截图display-hdr-locked.png。覆盖HDR开/未知拒绝、旧SDR缓存拒绝、不同型号不强套、标准别名、先恢复用户色温后RGB、消费一次、来源改变拒绝，以及控制器模式往返恢复。实机HDR/色温/对比度写入均未执行，异常时序和物理效果未做危险实机复现。原生WPF用离屏宿主替代Playwright，不Show窗口、不抢焦点。

本轮修改：新增HdrSafety.cs；修改src/Core/Packages.cs、DeviceControl.cs、TargetedControl.cs、Workspace.cs、SimulatedDisplayBackend.cs，src/UI/WorkspaceWindow.cs、LiveControls.cs、ToolHost.cs，tests/Platform/ProductTests.cs，examples/aoc-q27g4zmn/adapter.json（示例包1.2.0，非主程序版本），README、docs/USER_GUIDE.md、ADAPTERS.md、LIVE_CONTROL_VALIDATION.md及CODEX_PROGRESS。无提交推送发布，不改用户记录/包、不关闭旧程序。

重跑命令沿用README：dotnet build DisplayControlLab.sln -c Release -m:1；Platform测试输出temp/validation/hdr-safety/core-final；--ui-test输出temp/validation/hdr-safety/ui-final；./build/package.ps1 -LiteOnly -PreviewOnly。最终打包测试/大小/哈希/路径见temp/validation/hdr-safety/package-check.json，完整变更清单changed-files.json。当前预览路径见temp/preview/preview-path.txt。下一步用户主动验收安全恢复；真实HDR黑屏切换与物理效果未由代理自动验证。用户本轮未提供自行运行测试命令。

## 系统HDR过渡恢复（2026-10-11，当前增量）

用户15:14两次系统HDR切换结果为ok，随后标准色温/对比度GET及私有预设读取失败；实机日志只查到初次读取，未查到后续失败项重读，不能确认截图所运行版本。源码现有重试窗口较短，切换结束文案覆盖读取恢复结果。修复集中src/UI/LiveControls.cs、WorkspaceWindow.cs、ToolHost.cs，README、USER_GUIDE、LIVE_CONTROL_VALIDATION及本文件。

系统HDR按钮与外部模式检测共用恢复流程：立即作废旧读数/范围/选项，取消旧意图；等待1200ms，重新枚举并间隔250ms核查身份/模式稳定，再读当前显示器。失败项按1000/2000/3000/5000ms有界重读，总期限20秒，最多5轮。暂时断连继续等待；模式又变或修订变化则废弃旧恢复任务。一次过渡出现失败后，失败项恢复时完整复核一次，更新初期成功但可能未稳定的读数；普通单项调节不走全量复核。

恢复期间显示“正在恢复显示状态”、禁用目标控制及重复系统HDR按钮，不发送显示器设置。最终提示区分已恢复/未完全恢复，不被系统切换成功文案盖住。保留单项重试与刷新状态，mode-reloads保存逐轮范围/失败/时间证据。关闭窗口取消恢复；20秒并非保证所有固件都已恢复，超时仍按未知/失败处理，保护互锁继续有效。

当前Release build零警告错误，49项WPF后台测试通过（含连续三轮模式/色温失败后恢复、过渡文案/禁用、零亮度SET、完整复核请求计数）；核心逻辑未改，沿用上一阶段126项通过记录，不称为本轮重跑。证据temp/validation/hdr-transition/ui、packaged-ui，hdr-recovering.png；原生WPF离屏替代Playwright，未Show/抢焦点。无真实系统HDR切换或显示器写入；设备/驱动实际过渡时序需用户主动验收，不把模拟等同于实机。

本地Lite目录/大小/SHA256/打包验证见temp/validation/hdr-transition/package-check.json，修改清单见changed-files.json；入口位置见temp/preview/preview-path.txt。未提交推送发布，旧目录保留，运行新版前关闭旧两个工具避免单实例转发。用户未提供自行运行测试命令。重跑dotnet build DisplayControlLab.sln -c Release -m:1；控制器--ui-test temp/validation/hdr-transition/ui；./build/package.ps1 -LiteOnly -PreviewOnly。

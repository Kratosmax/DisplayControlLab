# 副屏亮度、稳定顺序与紧凑视图验证

2026-10-11，本地未发布预览，主程序正式版本未改。

## 最新紧凑视图改版

用户指出上一版仍是一项占整行、名称/数值/更多占三行。本轮改为自适应两列控件（可用宽度不足680时单列）：健康数值项约48逻辑像素高，名称、滑块、数值、帮助/更多图标同一行；4个常用功能为2行。预设使用经过主题样式处理的下拉框，初始化不发送指令；选择发送语义id，保留未开放选项的禁用状态。手动应用模式的按钮也在编辑器同一行。重复的已知当前值隐藏，正在应用、读取失败、设置未确认、HDR互锁仍直接展示，未识别枚举原始值不会丢失。静态适配说明与来源放入“⋯”，帮助用“?”，展开状态在本次会话重新渲染时保留。

详细视图保留原卡片与模式按钮，仅紧凑视图使用新面板/下拉模板。型号包、核心协议、亮度失败恢复不变，本轮不执行任何真实硬件读写。源码及打包WPF离屏UI各68项通过，新增实际布局高度/并排检查、下拉初始化零请求/选择只写一次/恢复模式、紧凑手动应用一次发送。核心未改，上一阶段160项记录仍保留，不称为本轮重跑。

本轮证据temp/validation/compact-grid：ui、packaged-ui、rendered、package-check.json、changed-files.json。专项宿主仍检查420→420滚动和顺序，覆盖1040×700/1480×900、100/125/150/200%渲染、单列回退、长标题/选项、帮助/更多展开、未知值与写入失败。原生WPF离屏替代Playwright，不Show、不抢前台。缩放是离屏渲染，未做真实跨屏DPI与真实鼠标打开下拉弹出窗验证。

重跑布局专项：dotnet run --project temp/validation/compact-grid/harness/Harness.csproj -c Release -p:PreviewDir=<程序集目录> -- <仓库根目录>；控制器/读取器 --ui-test temp/validation/compact-grid/ui；./build/package.ps1 -LiteOnly -PreviewOnly。当前产物以本轮package-check为准，下节为上一阶段的实机亮度与初版视图证据。

## 上一阶段：亮度与初版紧凑视图

用户默认日志中MSI G273Q在17:04–17:05的亮度64/66/69/71等目标多次返回VCP写入失败，并夹杂成功操作。旧流程设置Sent后直接调用Windows SET，异常立即结束，未做失败后回读。旧日志没有原生错误码，无法据此证明驱动、固件或时序的具体根因。

内置标准亮度现在在写前等待80ms，接口报错后继续有界回读。回读目标值即确认；只有SET确实报错、回读原值及范围未变、重新核实设备身份/系统HDR/显示器HDR安全条件时，最多补发一次绝对值，补发前等待180ms。成功的SET不重复；未知读数、外部改变、范围/模式/身份变化均停止。已经是目标亮度时不发SET。其他标准项、私有协议、插件和自定义包亮度不套用该重发策略。ControlResult记录nativeErrorCode、writeAttempts，未确认但已回读当前值时继续允许调节；当前未知仍禁用。

顺序跳动原因有代码证据：TargetedControl.Merge/UpdateFromWrite先RemoveAll再Add，使被更新项移到末尾。现改为按id原位替换；已有顺序保留，新项才追加。ShowPage在同设备/页面/分组/视图下保留滚动位置，主动切换上下文回到顶部。

新增详细/紧凑视图选择并持久化到用户偏好，默认仍为原详细视图。紧凑视图使用横向短行并去掉右侧信息栏，基本数值优先；来源、菜单路径、联动核查与研究入口折入“更多”。两种视图复用FeatureCard的写入、互锁、失败恢复、NumericEditor及帮助，错误/适配限制不折叠。不新增协议包或改变已安装包。

## 上一阶段验证

- Release构建零警告错误，160项核心回归通过；涵盖原位更新、SET错误但已生效、一次有条件补发、失败次数上限、外部改动/范围变化/读数未知/模式变化停止、目标相同零SET。
- 源码和打包原生WPF离屏UI各61项通过；覆盖视图偏好、切换零请求、共用数字编辑器、顺序稳定、当前值已核实的失败后可继续调整，以及已有HDR保护/帮助测试。
- MSI真实标准DDC测试：两轮各5组设置及恢复，20次请求均回读确认，合计21次底层SET尝试。其中恢复98的一次SET返回Win32 31，回读原值后补发一次成功；最终亮度恢复测试前98。目标覆盖64/66/69/70/71和99/100。样本有限，不能保证其他驱动、线缆、固件、后台软件竞争下永不失败，也未证明错误31的底层成因。主屏未写入，未切系统HDR。
- WPF离屏专项验证顺序及滚动：写入前后偏移420→420；两种视图切换零硬件请求。截图覆盖1480×900、1040×700、100/125/150/200%渲染、长标题、当前值已知但写入未确认、读取未知。原生WPF替代Playwright；未覆盖真实多DPI跨屏拖动或其他Windows版本。

证据位于temp/validation/compact-control：core/platform-tests.txt、ui-final、packaged-ui、rendered/results.json及PNG、msi-live.json、msi-near-max-live.json、package-check.json、changed-files.json。CLI测试使用隔离资料和隐藏进程；UI测试不Show、不注入鼠标键盘。verify-msi.py含真实SET及恢复，不能当只读脚本自动运行。

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/compact-control/core
$p = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/compact-control/ui-final' -WindowStyle Hidden -Wait -PassThru
dotnet run --project temp/validation/compact-control/harness/Harness.csproj -c Release -p:PreviewDir=<程序集目录> -- <仓库根目录>
./build/package.ps1 -LiteOnly -PreviewOnly
```

本地Lite入口见temp/preview/preview-path.txt。新版无需重装型号包，先退出旧两个工具，避免单实例转发到旧程序。用户资料、已安装包未自动更新，未提交、推送或发布。旧源码与打包记录是历史证据，不覆盖本轮结果。

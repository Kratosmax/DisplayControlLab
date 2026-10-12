# 读写一致性审查

2026-10-12；main 基线 53c4953，版本 0.3.1。本轮为本地预览，未提交、推送或发布。

## 已修正的问题

| 问题与之前行为 | 修改后的行为 | 源码 |
|---|---|---|
| HDR 条件改变后，局部刷新仍合并旧模式下的其他读数 | 模式不一致时丢弃旧模式读数，只保留本次采样 | src/Core/TargetedControl.cs |
| 来源变更时仅看数值或哈希；界面可能保留旧来源数值，试调可能沿用旧通道的验证结论 | 统一核对功能 id、来源与哈希；手动设置先定向读取，自动试调跳过已变更来源 | TargetedControl.cs、ReaderSurvey.cs、WorkspaceWindow.cs |
| 自动恢复仅以差异列表判断；普通恢复仅按连接路径与哈希判断 | 另核对设备身份、采样完整性、系统 HDR、来源及原值；缺失证据不确认恢复 | ReaderSurvey.cs、Workspace.cs |
| 应用检测包先绑定、再每项单独改来源，多次保存暴露中间状态 | 包绑定和全部来源选择在一次配置变更中保存；包外路由保留 | Routing.cs、GuidedReader.cs |
| 配置与记录各有一套原子保存逻辑，配置缺少短暂占用重试和临时文件清理 | 复用 Library.SaveFile，保持原有权限拒绝及有界重试 | Workspace.cs、Packages.cs |
| 方案预检与应用自行更新 snapshot，应用后未同步设备缓存 | 复用 StoreSnapshot，同步页面、设备缓存与上次确认值 | ResearchPages.cs |
| 紧凑帮助先切换成长文字，再由第二个点击处理改回问号 | 同一帮助组件直接按紧凑/详细参数显示，删除重复点击处理 | FeatureHelp.cs、CompactControls.cs |
| 检测结果显示裸编码，与控制器的状态名称不一致 | 复用 Reading.Display，色温等显示已知名称；未知编码保持未知 | GuidedReader.cs |

未删除历史数据模型、有效插件入口或未知菜单线索。它们分别用于兼容、真实可扩展通道和说明协议缺口；本轮没有查实可安全删除的整项产品功能。现有未实现的私有协议仍为未知/只读，不新增不可执行的控制入口。

## 实际验证

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/read-write-review/platform-user
$reviewProcess = Start-Process src/Reader/bin/Release/net8.0-windows/DisplayControl.Reader.exe -ArgumentList '--ui-test','temp/validation/read-write-review/reader-final' -WindowStyle Hidden -PassThru
$reviewProcess.WaitForExit(60000)
$reviewProcess = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/read-write-review/controller-final' -WindowStyle Hidden -PassThru
$reviewProcess.WaitForExit(60000)
./build/package.ps1 -LiteOnly -PreviewOnly
```

最终源码构建 0 警告、0 错误；核心 212 项，读取器 UI 101 项、控制器 UI 99 项通过。新增回归覆盖跨模式缓存、跨来源定向读取、来源变更禁止试写、完整恢复证据、批量来源选择与包外路由保留、方案应用同步缓存、紧凑帮助及语义状态显示。

核心首次在受限上下文失败于已有跨进程导航用例，尚未跑到新增部分；相同程序和隔离输入在正常用户上下文全部通过。没有据此断言根因为产品故障或特定系统组件。

WPF 不能用 Playwright 驱动，本轮使用实际 WPF 离屏宿主，检查真实按钮事件和渲染；100%、125%、150%、200% 渲染沿用已有矩阵。已人工查看 reader-final/reader-result-1.png，最小尺寸按钮和表格列清楚。测试使用隔离数据与模拟设备，不枚举显示器、不写硬件、不修改真实剪贴板或用户配置。其他 Windows 版本、跨屏 DPI 和实机固件尚未验证。

证据位于 temp/validation/read-write-review：platform-user/platform-tests.txt、reader-final/ui-tests.json、controller-final/ui-tests.json 和对应 PNG。最终包路径、大小、哈希与包内验证见该目录 package-verification.json。

最终 Lite ZIP：temp/preview/DisplayControlLab-0.3.1-Lite.zip，940014 字节，SHA-256 为 5f316a5d333cdce9408315449c9ae8000ba860ce6c9bc5c939f4b4a11733a895。最终包入口再次运行隔离 UI 回归：package-reader 101 项、package-controller 99 项均通过；ZIP 内五个程序集哈希与已验收展开目录一致。localPreview=true，需要 .NET 8 Desktop Runtime x64。发布权限未扩大，未覆盖正式 Release。

## 验收与边界

先退出旧读取器与控制器，再运行预览目录中的 DisplayControl.Reader.exe；旧单实例可能接管新入口。已有检测结果可直接重新应用，无需为本轮界面修改重复扫描。控制器换屏后应保留最近方案结果；选择新来源后不应出现旧来源的确认值。真实写入仍以回读为准。

本轮没有实机 SET，不能保证所有显示器固件的读取、写入或恢复都符合标准；三轮稳定只证明当时读数稳定。自动试调仍只包含亮度/对比度，RGB/色温等保留手动调节。私有 HDR 菜单必须有有效适配协议，不能由读取成功推导未知写入命令。

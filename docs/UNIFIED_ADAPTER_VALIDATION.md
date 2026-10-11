# 一台显示器一个适配包

2026-10-11。用户明确目前没有其他使用者，无需兼容旧版本；本轮去掉新增的旧补充包迁移逻辑，直接使用功能级通道格式。

主屏community.aoc-q27g4zmn/1.3.0包含原11项私有功能、游戏模式/图像比例标准读取和AOC色温定义。包默认amd-ddc，后三项显式standard-ddc；基础亮度/对比度/RGB仍复用内置。副屏community.msi-g273q/1.1.0使用标准通道。主屏标准补充目录已删除，当前打包不再分发补充包。

Packages.BackendFor统一解析control.backend → package.backend → standard-ddc；Validate按有效通道检查vcpCode/raw，DeviceControl按有效通道读写。工作台可逐项选择通道，切换时清空不相容的协议字段并禁用写入，防止误发。它是扩展边界，当前没有NVIDIA/Intel私有通道实现。

overridesStandard在用户应用型号包后默认使用声明的标准项；限定同一标准VCP，已有明确来源选择保留，多个非标准提供者仍需选择。主副屏色温因此无需首次手动处理来源冲突。

HDR状态未知/开启时互锁、写入白名单、设备身份/来源/哈希、结果确认与恢复检查继续保留。新增模式和比例只读，未扩大危险写入。没有AMD时标准读取仍独立可用，写入仍受HDR状态已知等条件约束。

## 验证与边界

- Release solution build零警告错误；138项核心测试，覆盖混合通道解析/错误拒绝、标准默认来源/保留手选、原私有写入限制、HDR保护。
- WPF后台UI测试含同包不同通道工作台渲染；窗口从未Show，不抢焦点，不进行实机设置。结果见temp/validation/unified-adapter/ui和packaged-ui。
- 打包exe在隔离DISPLAYCONTROL_DATA仅安装/绑定一个主屏包、一个副屏包，逐项两轮实际读取，并生成实机离屏截图。记录在temp/validation/unified-adapter/routed-results.json、routed-summary.json和real-ui。
- 原生WPF使用离屏宿主替代Playwright。没有发送任何真实SET/系统HDR切换，未重新做实机恢复、NVIDIA或Intel验证，不把混合包等同于跨显卡私有功能已实现。

修改：src/Core/Packages.cs、DeviceControl.cs、Routing.cs，src/UI/PackagePages.cs、ToolHost.cs，tests/Platform/Program.cs、ProductTests.cs，主/副屏示例JSON/README、build/package.ps1和相关文档。删除examples/aoc-q27g4zmn-standard，未增加兼容迁移模块。

复核命令：dotnet build DisplayControlLab.sln -c Release -m:1；Platform测试以temp/validation/unified-adapter/core输出；控制器--ui-test temp/validation/unified-adapter/ui；./build/package.ps1 -LiteOnly -PreviewOnly。产物大小/路径/SHA256见temp/validation/unified-adapter/package-check.json，完整工作区变更哈希见changed-files.json。

本地预览，主程序正式版本未改、未提交推送发布。默认用户资料/包未改写，旧运行进程未关闭；启动新包前由用户关闭旧两工具。若曾手动装过试验性补充包，直接卸载它，不提供旧版迁移。历史temp实验/产物保留作证据。用户未提供自行执行的测试命令。

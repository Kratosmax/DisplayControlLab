# 读取器自动检测与界面精简验收

## 0.4.0发行状态（2026-10-12）

本页所述最新统一试写/手动重试/只导出通过项已包含在v0.4.0正式发行，提交33c8023；下方“未发布”字样是开发阶段历史。0.4.0本地核心260项、Reader121/Controller119项UI、Full/Lite各51项更新专项通过，云端四包/签名/真实0.3.1升级/安装器通过。线上八资产与独立下载哈希一致，完整证据见UPDATE_VALIDATION.md与CODEX_PROGRESS.md。未以发行验证代替实机协议验证。

## 统一试写与成功项控制包（2026-10-12，当前规则）

以用户最新要求替代下方所有“未试写标准项可写”“失败/未知只读项保留导出”规则：可靠可恢复定义的数值、枚举、布尔项进入统一SET/独立GET/恢复验证，不限图像白名单；标准/型号协议均覆盖，新增标准数值可直接草稿测试。高风险连接/电源/菜单/HDR需手动选值确认，恢复出厂等不可撤销命令禁止，缺协议/原值/合法值说明原因。每行测试/重试卡支持单独重试、选值测试和重新读取，失败→重试成功保存开放。仍未恢复不得重试写入，当前模式/来源与恢复门禁保留。

导出/应用只保留本机实际写入及恢复通过、读取有效且无未恢复联动的功能，不导出只读占位和menuHints；枚举只开放通过档位，依赖未通过则说明暂不导出；零通过不生成空包。控制器应用新成功包后只显示其通过项，避免内置/旧未知功能回流；读取器保留所有研究记录。未知写入协议和不可恢复项不能凭手动按钮强写，须先更新协议；实际断连恢复不保证能自动完成。

新增src/Core/FeatureWriteTests.cs、src/UI/FeatureTestPanel.cs；调用ReaderSurveyRunner.Retry、Package严格筛选、PackageState.VerifiedControlProfiles和控制页过滤。删除旧StandardManualAllowed与TestWrite白名单逻辑。测试覆盖音量、型号数值/枚举、失败重试→成功落盘导出、高风险自动跳过/手动选值恢复、不可撤销禁止、新标准项无需安装直接草稿试写、条件依赖、控制器名单、空包和无关恢复限制。

最终全量构建0警告/0错误；末次仅测试空数组读取假设修复后测试项目构建0警告/0错误。核心260项隔离模拟通过；最终包Reader121/Controller119项离屏UI通过，真实按钮触发失败/重试成功及未定义协议禁用/读取可用。620宽feature-test-panel.png人工查看；宿主覆盖现有小窗口/DPI，WPF不能用Playwright。不Show/写实机，私有固件实际联动/断连恢复/实际Enter/其他系统未测。失败阶段reader/ui-error.txt、platform-delivery-output.txt保留不算通过，最终以platform-delivery-verified、delivery-reader/delivery-controller为准。

重跑：dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false；tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/write-contract/<独立目录>；./build/package.ps1 -LiteOnly -PreviewOnly；包内两个入口--ui-test temp/validation/write-contract/<独立目录>。最终ZIP992891字节，SHA256 b036f639f920f54fd3098681e7a6a347edf3bb008818d77524620aa0ab8265e8，五程序集ZIP/展开匹配，Core与260项测试二进制一致；详见temp/validation/write-contract/package-verification.json。未改版本/提交/推送/发布，下方旧包数字仅为历史。

## 部分导出与画面预设（2026-10-12，最新交付）

不支持/未读到色温跳过；恢复未确认停止试写但允许导出应用部分结果。恢复项、差异、联动和依赖只读；证据不足全只读。旧色温完整操作After兼容，当前RecoveryObserved记录实际核查。只读重测继承原基线，冲突保持全只读，不清门禁。画面预设只试显示器声明的00–07常见档位，含原档位/SDR/来源/互锁有效，回读图像联动、恢复原预设及标准亮度/对比度/RGB残留；未知或外部联动仍待核查，演示/节能/私有码跳过。色温不可读不阻挡独立画面测试。

最终构建0警告/0错误；核心253项（platform-delivery，正常用户上下文），包内读取器118/控制器116项隔离UI通过。一次沙盒IPC测试失败，正常用户上下文同用例通过；不混计。旧Dell报告隔离回放亮度对比度可写、色温/RGB/音量/静音只读；不改实机与真实资料。实际WPF reader-partial-result.png已人工查看，应用/导出按钮启用；不Show，Playwright不适用WPF。实机预设恢复、其他系统及跨屏DPI未测。

证据temp/validation/partial-survey；重跑dotnet build、tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/partial-survey/<独立目录>、包内--ui-test及./build/package.ps1 -LiteOnly -PreviewOnly。最新ZIP980763字节，SHA256 5c5c9692c74cb1daa14c77f6d0ee4e048a4dcc07c27b8c2797304d41307c830c，包内Core与核心测试二进制相同，五程序集与验收展开目录相同，见package-verification.json。未提交/推送/发布，下方旧数字/门禁范围均为历史。

## 核查操作卡精简（2026-10-12，当前交付）

上一轮长说明被用户否定，现默认仅两步和两个按钮；原值编码、联动和堆栈放技术详情，默认折叠。未知原选项直说，不冒充菜单名。新“我已改回，检查一下”真实按钮只Capture并复用RecoveryMatches，匹配才保存解除门禁；未匹配、失败、取消保留门禁，不扫描/试写。核查失败技术详情不会反复累积。检测首屏/确认/恢复提示也压缩，识别统计折叠。

构建最终0警告/0错误，源码读取器116项、包内读取器116项/控制器114项真实隔离WPF UI通过。新增简短默认内容、折叠详情、忘记原选项反馈、缺基线禁用、核查不匹配/匹配和全程仅读请求回归；实际620宽离屏截图已查看，不Show/写实机。Playwright不适用WPF；实际键盘Enter、实机恢复/跨屏DPI未测；未重复此前241项核心验证，核心仅public复用旧判定。证据temp/validation/recovery-simple；重跑构建、--ui-test及Lite打包命令同下节。

最新ZIP temp/preview/DisplayControlLab-0.3.1-Lite.zip，976124字节，SHA256 f0102b061eba0c64b135529e0769711cfe4db6ce893d4c9a09d2d756b748e7c7；五程序集与验收展开目录相同，详见package-verification.json。未提交/推送/发布，下方长说明及大小/哈希均为历史。

## 待核查行引导验收（2026-10-12）

新增橙色行/选中态与点击展开步骤，使用保存的基线和操作After解释变化，不把历史读数当实时状态。色温探测优先使用Probes.OperationId。未知菜单路径、未知编码和缺原值均明确说明；引导不读写硬件，不绕过恢复限制。先实体菜单核对，再取消常见图像项试写、开始检测，复用已有只读恢复门禁。

构建0警告/0错误；源码读取器111项UI通过，Lite包内读取器111项、控制器109项通过。真实WPF离屏截图recovery-guide-620.png已人工查看；窄窗口正文换行、橙色选中状态可读。测试通过冒泡MouseUp真实触发表格处理，未Show或写实机；键盘Enter实际输入、用户菜单恢复、其他系统未测。核心行为未改，本轮未重复核心测试。

重跑：`dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false`；用读取器/控制器`--ui-test temp/validation/recovery-guide/<独立目录>`隔离UI验收；`./build/package.ps1 -LiteOnly -PreviewOnly`。证据在temp/validation/recovery-guide，首次失败的直接鼠标事件模拟记录保留；最终验收以package-reader/package-controller/ui-tests.json为准。

本地ZIP temp/preview/DisplayControlLab-0.3.1-Lite.zip：974147字节，SHA256 a6807a29267f213e3d1b1c1cbf1e362d1af578e24e90263056628fe701202b1b。五个程序集与验收展开目录哈希匹配；package-verification.json记录完整路径。未提交/推送/发布，下方预览数据为历史。

最新识别和自动试调范围已由 [VCP_RECOGNITION.md](VCP_RECOGNITION.md) 更新：增加公开标准与厂商候选识别，RGB 及声明色温对比恢复。本页下方的“仅亮度/对比度”描述与旧数量是前序验收，不是最新版本范围。

2026-10-12；源码基线 main / 53c4953，版本保持 0.3.1。已追加标准控制权限修复与提示词预览/复制。本轮为未提交的本地功能预览，不是正式 Release。

## 使用流程与删减

首页统一为：选择显示器 → 自动发现 → 三轮读取核验 → 可选图像试调并恢复 → 导出或直接应用适配包。手动菜单变化对比折叠保留；无需创建实验、命名采集段或选择快照。删除了旧快照配对、旧记录导入、分段实验、轮次边界编辑、连续采集界面及对应 UI 测试。旧数据文件、用于手动记录的 Experiment 数据结构及相关兼容测试保留。

高级入口仅保留设备详情、适配包、协议工作台；读取器不显示控制器的“控制习惯”设置，不在启动或换屏时自动预读所有功能，也不启动控制器的状态轮询。控制器的设备预热、模式同步、方案、历史与恢复继续工作。

## 自动验证边界

- 发现沿用现有独立进程扩展 GET；跳过恢复出厂等命令码。额外响应只作为未知用途的只读状态，不猜 SET。
- 连续三轮核验状态、读数、范围、来源哈希和系统 HDR 条件。已有代码插件的文本读数可核验，但不能导出为不含插件二进制的纯协议包。
- 自动试写白名单只有标准通道亮度 0x10、对比度 0x12。仅限系统 SDR、读数/范围稳定、原有约束和 HDR 互锁允许时，试调一步、回读、恢复，再核查其他已知状态。不得把这一点位测试称为全范围或画面效果验证。
- 原值和完整核查基线先保存，再 SET；取消不取消独立的 90 秒恢复核查。结果不匹配、恢复失败、外部修改或观察到联动均停止后续试写；恢复未确认禁止生成控制包，跨进程重开后也先核查原值，不直接重试 SET。
- 已读通且有已知标准写入定义的 RGB、色温、音量等允许手动调节，并保持 read-observed，不伪造试写通过；未知协议、读取失败或实际试写失败保持只读。旧生成包误设的标准只读定义可在重新应用现有结果时修复，明确第三方只读声明不覆盖。失败标准项以 declared / 只读定义覆盖，避免回退到内置可写定义。已安装声明式协议中的 write-confirmed 功能保留协议、条件与作者验证声明，显示“已有适配验证（本轮只读）”，不谎称本轮试写。
- 直接应用需要预览确认，安装生成包并显式选择对应来源；既有包不删除。未知私有协议提供提示词弹窗，直接预览、全选或一键复制；保存文本为独立可选操作，不自动构造命令。复制失败在弹窗提示并保留手动复制途径。

## 本轮实际验证

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/reader-prompt-fix/platform
$p = Start-Process src/Reader/bin/Release/net8.0-windows/DisplayControl.Reader.exe -ArgumentList '--ui-test','temp/validation/reader-prompt-fix/ui' -WindowStyle Hidden -Wait -PassThru
$p = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/reader-prompt-fix/controller-ui' -WindowStyle Hidden -Wait -PassThru
./build/package.ps1 -LiteOnly -PreviewOnly
$packagePath = (Get-Content temp/preview/preview-path.txt -Raw).Trim()
$p = Start-Process (Join-Path $packagePath 'DisplayControl.Reader.exe') -ArgumentList '--ui-test','temp/validation/reader-prompt-fix/preview-reader' -WindowStyle Hidden -Wait -PassThru
$p = Start-Process (Join-Path $packagePath 'DisplayControl.Controller.exe') -ArgumentList '--ui-test','temp/validation/reader-prompt-fix/preview-controller' -WindowStyle Hidden -Wait -PassThru
```

最终构建 0 警告 / 0 错误；核心 202 项通过；源构建和实际 Lite 包内入口分别通过读取器 97 项、控制器 95 项 UI 验证。核心覆盖未知码、多轮读取、导入与真实控制器路由、保留既有私有协议、试写无效果、恢复失败、恢复后重试门禁、联动、HDR、SET 后取消及持久化。UI 验证触发真实按钮、检查忙碌门禁与结果反馈，并渲染真实 WPF 界面。

Playwright 无法驱动原生 WPF，采用项目离屏宿主；不 Show、不抢焦点、不连接实机、不发送硬件 SET。覆盖最小宽度 1040、100% / 125% / 150% / 200% 渲染，保留长设备名、只读、失败、忙碌和空设备共享回归。已人工查看实际包的 reader-result-1.png，结果列完整可读。跨屏实际 DPI、其他 Windows 版本和显示器固件兼容性未实机验收。

证据：temp/validation/reader-prompt-fix/platform/platform-tests.txt；ui/ui-tests.json；controller-ui/ui-tests.json；preview-reader/ui-tests.json；preview-controller-user/ui-tests.json；实际包截图 preview-reader/reader-start.png、reader-result-1.png 等，均在上述验证目录下。

## 已处理的验证问题

首次构建出现 NuGet 漏洞数据源不可达警告；后续构建显式禁用本轮漏洞联网审计，编译和项目验证正常。核心跨进程导航测试在受限沙盒失败，同一隔离模拟测试在正常用户上下文通过。记录快速原子替换曾偶发被 Windows 拒绝，未确认占用方；Library.Save 对短暂占用及可写目标的短暂拒绝有界重试，真实只读拒绝仍报错、原值保留，专项测试已通过。一次验证误与构建重叠造成复制等待，最终均在构建完成后运行。默认并行 publish 在此环境无详细错误退出 1；同参数添加 -m:1 成功，正式打包脚本的三处 publish 均串行化，Lite 预览打包成功。

## 预览产物

- 项目相对路径：temp/preview/DisplayControlLab-0.3.1-Lite.zip
- 大小：938214 字节。
- SHA-256：7f0c14c35bb84f219cec0d5c08a5ada7ae1e07f51736be955aa7087e2375b1a3
- distribution.json：localPreview=true、channel=Lite；需 .NET 8 Desktop Runtime x64。
- 解压运行 DisplayControl.Reader.exe；也可从 temp/preview/preview-path.txt 定位本轮已展开的可运行目录。

未改正式版本、未提交或推送、未创建标签或 Release、未生成签名更新清单。Full / Setup / 正式升级链本轮未重测。回滚基线为 53c4953，所有源码改动仍可逐文件审阅；历史 temp 和用户资料不删除。

## 本轮用户反馈修复证据

用户实际运行了首轮读取器，并完成 Dell P2419H 检测与应用；具体启动命令未提供。只读检查其最新检测记录：45项读取通过、2项试写通过；RGB与色温原标准定义允许写入，却被首轮生成策略降为只读。修复后用同一记录离线回放，生成包的RGB/色温标为可手动调节、read-observed；亮度/对比度保留write-confirmed。声明色温选项限定为5/8/11/12，编码12名称未确认，显示编码而不猜含义。回放未写硬件、未改用户资料，结果在temp/validation/reader-prompt-fix/dell-replay.json与dell-adapter-preview.json。

新核心回归覆盖标准权限、未自动试写不伪造验证、旧生成包修复、第三方只读保留、已失败试写不放开和声明选项过滤。提示词UI覆盖完整长文本、复制与保存独立、失败反馈、最小尺寸按钮可见及真实离屏截图。剪贴板测试通过注入回调核对完整内容，不覆盖真实剪贴板；真实Clipboard.SetText由用户点击触发，失败在弹窗反馈。弹窗截图protocol-prompt-860.png、protocol-prompt-620.png已人工查看，未Show或抢焦点。默认路径不生成提示词文本文件。

零背景提示词补充：内嵌项目目标、Reader/Controller关系、Windows/.NET/WPF与AMD通道限制、apiVersion=1字段契约、raw帧/校验规则、验证语义、可解析的最小示例及当前包。三个JSON块分别为示例、当前包（未完成则null）、精简实测上下文。上下文保留所有功能原始读数/回包与来源摘要，去除重复定义和无有效响应的未声明扩展码，原记录不变。格式示例与当前包通过真实Packages.Parse；回包完整性、原记录不变及检测未完成情形均有核心回归。其他AI是否遵循、是否有足够上下文、能否找到未知私有协议无法由本地测试保证，未知必须提供来源或保持未知。


最终包验证：读取器97项通过；沙盒内控制器验证一次停在系统崩溃提示，未捕获底层异常，已按命令行确认后仅关闭本任务--ui-test子进程；另一次30秒限时中止验证等待。相同包在正常用户上下文、60秒限时内完成95项，证据preview-controller-user/ui-tests.json。此现象未定位根因，不宣称已修复底层崩溃。核心202项及源构建97/95项通过；ZIP内五个程序集与已验收展开目录逐字节哈希一致，见package-verification.json。

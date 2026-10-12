# DisplayControlLab 接续状态

最后校准：2026-10-12。0.4.0已正式发布，标签v0.4.0固定发行提交33c8023f29f2cdebfb4233ee0f2b131ceffbf8e6；仓库Kratosmax/DisplayControlLab，remote git@github.com:Kratosmax/DisplayControlLab.git，main保留发行后验收文档提交，实际HEAD以git log为准。用户已授权记录进度、推送源码并发布新版，本轮工作已完成。Directory.Build.props/包内版本/标签均0.4.0；四种包、两通道签名清单、兼容清单和SHA256SUMS共八资产齐全。正式签名在已有GitHub Secret受控工作流完成，公钥未变，私钥不落源码。

Release：https://github.com/Kratosmax/DisplayControlLab/releases/tag/v0.4.0 。发行Actions 38247357237成功，main CI 38247356964成功。已独立核验latest指向v0.4.0、正文只含当前版本、八资产下载大小/GitHub digest/SHA256SUMS匹配，update.json与update-lite.json一致。正文CRLF/LF规范化后与源码完全一致，无内容差异。

使用上一正式版0.3.1原始Full/Lite ZIP（核对官方SHA256，未修改二进制或安装版本身份），在隔离目录真实获取线上latest清单、验签、下载并预检0.4.0、交接旧版外部更新器、替换/重启/启动清理，两通道均通过，资料哨兵保留；Full在无全局DOTNET_ROOT条件验收。CI同样用真正上一正式版在受控本地HTTP代理下完成签名候选升级，并通过两安装器静默安装/卸载及注册表清理。本机发现用户已有Lite安装，安装测试安全停止；用户真实安装、资料与硬件均未修改。

线上包体：Full Setup 51443472字节、Full ZIP 73028332字节、Lite Setup 2560102字节、Lite ZIP 993902字节。Lite ZIP SHA256 2f81f9f99718fa7430b7550366d300e8e929b93f2ac8877616bf2661ffc01d25。全部资产哈希见Release/SHA256SUMS.txt；本地和云端独立构建哈希不同属预期，以线上清单/包相互匹配为准。证据temp/validation/release-0.4.0/online/{release-api,verification}.json、live-predecessor/results.json、cloud-release.log。445MB云端归档未完整下载，改取完整工作流日志核对测试结果；归档仍在成功Actions中保留，不把未下载归档当本地已有证据。

## 当前产品与本次范围

.NET 8 / WPF / Windows x64。Reader/Controller独立进程，共享资料，硬件命令由独立worker执行。读取器启动/换屏不预读；控制器定向刷新、缓存、方案与历史保留。读取器以一键检测为主，三轮读取与记录自动保存；手动菜单变化对比折叠，高级设备/适配包/协议工作台保留，旧手动快照配对与分段实验UI已删除，旧数据模型及文件兼容。

用户最终三条规则：所有可靠可恢复写入定义都进入试写；失败/未测/高风险有原因、手动测试或重试；控制包只保留实际通过项。FeatureWriteTests统一数值/枚举/布尔的SET、独立GET、恢复检查，覆盖音量及型号协议，不限图像白名单。新标准数值项有声明/版本/范围依据可直接草稿试写，不需先安装只读包。每项TestReason/Probes/TestedValues保存结果、原因、通过档位；旧作者write-confirmed声明与仅read-observed不冒充本轮实测。

FeatureTestPanel每行测试/重试入口提供自动重试、手动选值、重新读取；成功保存开放，失败撤销可写验证。输入源/电源/OSD/HDR等打断使用项需手动选值确认；恢复出厂等不可撤销命令禁止GET/SET并在列表留原因。无可靠协议/可恢复原值时需补协议，不猜SET。色温/画面预设复用ImageProbe专用联动恢复，RGB不是色度测量，不猜具体K值。未知私有码及无协议项不能因为手动按钮就强写。

Package只含本机写入/独立回读/恢复通过、读取有效、不受未恢复联动影响的Writable/write-confirmed项，无失败/未知/未测controls或menuHints；枚举只开放通过档位，未通过依赖不能夹带，零通过不生成空包。PackageState.VerifiedControlProfiles控制应用/导入成功包后的控制器展示名单，避免内置或旧包未知项回流，Reader保留所有研究记录；Detach取消名单。adapter apiVersion=1保持兼容。

恢复失败停止后续试写但允许应用无关通过项；恢复项、差异、联动/依赖受限，证据不足不开放。RecoveryBaseline/RecoveryObserved保留，旧色温完整Operation.After可补证据。只读重测继承旧原基线，不把测试值当新原值，多基线冲突保持限制；恢复确认前不再试写。橙色行点击后为简短操作卡及就地只读检查，编码/堆栈默认折叠。AI提示词自包含项目/契约/证据，直接查看复制，保存可选且脱敏。

80条扩展标准及4条品牌候选位于config/vcp-recognition.json并嵌入Core。型号/第三方定义优先；厂商候选保持只读。Dell复合输入源仅解释回读，AOC色温候选不猜K值，MSI缺资料不伪造。能力声明支持ushort枚举（Dell EA FE00 FE01），控制码仍byte。新增试写功能不绕过HDR互锁、白名单、来源Hash、设备身份、当前模式和取消时限时恢复。

## 本次累计修改文件（40项）

README.md、CODEX_PROGRESS.md、Directory.Build.props、RELEASE_NOTES_CURRENT.md、build/package.ps1、config/vcp-recognition.json、docs/USER_GUIDE.md、docs/READER_WORKFLOW_VALIDATION.md、docs/READ_WRITE_REVIEW.md、docs/VCP_RECOGNITION.md、docs/releases/v0.4.0.md、docs/UPDATE_VALIDATION.md、.github/workflows/release.yml、build/verify-updates.py、src/Core/DisplayControl.Core.csproj、src/Core/HdrSafety.cs、src/Core/Models.cs、src/Core/Native.cs、src/Core/Packages.cs、src/Core/Routing.cs、src/Core/SimulatedDisplayBackend.cs、src/Core/TargetedControl.cs、src/Core/Workspace.cs、src/Core/ReaderSurvey.cs、src/Core/VcpRecognition.cs、src/Core/ImageProbe.cs、src/Core/FeatureWriteTests.cs、src/UI/CompactControls.cs、src/UI/FeatureHelp.cs、src/UI/ResearchPages.cs、src/UI/ToolHost.cs、src/UI/WorkspaceWindow.cs、src/UI/GuidedReader.cs、src/UI/ProtocolPromptWindow.cs、src/UI/RecoveryGuide.cs、src/UI/FeatureTestPanel.cs、tests/Platform/ProductTests.cs、tests/Platform/Program.cs、tests/Platform/ReaderSurveyTests.cs、tests/Platform/VcpRecognitionTests.cs

## 验证与待办

发布前开发预览0.3.1已完成核心260项、Reader121/Controller119项WPF离屏UI；实际验证失败重试→成功落盘导出、手动输入源恢复、型号数值/枚举逐档、新标准项直接草稿、依赖和控制名单。证据temp/validation/write-contract。预览ZIP992891字节，SHA256 b036f639f920f54fd3098681e7a6a347edf3bb008818d77524620aa0ab8265e8；仅历史开发产物，不作为0.4.0正式包。源码/包五程序集及核心测试二进制哈希一致。0.4.0本地构建0警告/0错误、核心260项、Reader121/Controller119项UI和Full/Lite各51项更新专项已通过；四种本地包已生成。本地安装器验收检测到现有Lite安装记录后安全停止，未覆盖用户安装；原生安装/卸载已在干净CI环境通过。本机与云端产物不混淆。发布工作流升级门禁改用真正上一正式版原始ZIP，不修改旧二进制或版本身份，校验其SHA256后执行两通道完整升级。

本轮发布验收已完成，无阻塞；下一步等待用户实机使用反馈，不自行扩展私有协议。用户当前授权只涵盖本轮发布，后续发行仍按届时指令核对。0.2.0无更新器需手动过渡；0.3.1到0.4.0真实原始客户端两通道更新已通过。源码与进度推送后可跨设备接续。

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/release-0.4.0/platform
$p = Start-Process src/Reader/bin/Release/net8.0-windows/DisplayControl.Reader.exe -ArgumentList '--ui-test','temp/validation/release-0.4.0/reader' -WindowStyle Hidden -Wait -PassThru
$p = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/release-0.4.0/controller' -WindowStyle Hidden -Wait -PassThru
./build/package.ps1
dotnet run --project tests/Updates/DisplayControl.Update.Tests.csproj -c Release -- . temp/validation/release-0.4.0/updates-Lite temp/release/DisplayControlLab-0.4.0-Lite.zip Lite
```

所有普通验证DISPLAYCONTROL_DATA隔离到项目temp，不Show/抢焦点、不读写实机；Playwright不驱动WPF，使用原生离屏渲染并人工查看。沙盒IPC测试历史失败，正常用户上下文同测试通过。用户已运行预览并反馈Dell记录/截图，未提供自行运行测试命令。真实固件私有协议/联动、断连后的自动恢复、所有显卡/Windows/跨屏DPI未全测；“回读通过”不是画面效果或全模式证明。

## 稳定约束与关键入口

用户资料在LOCALAPPDATA/DisplayControlLab；Library原子保存短暂占用有界重试，真实拒绝不绕过。旧数据/适配包和历史temp不删除。当前AOC Q27G4ZMN/B411包1.5.0、MSI G273Q包1.1.0随包可手动导入，私有DDC主要AMD ADL，未实现NVIDIA/Intel私有通道。既有实机缺口见docs/OSD_CONTROL_VALIDATION.md：帧计数左侧两档编码矛盾禁用；局部调光/色域/Adaptive-Sync等无可靠协议不猜写入。控制器系统HDR与显示器HDR不同，HDR未知/开启阻止冲突图像设置，有记录才恢复颜色。

Updates模块负责安装身份、签名清单、URL前缀/HTTP代理、下载暂存、真实程序集/ZIP预检和事务回滚；Updater在安装目录外等待两个工具退出，不强杀。Full/Lite不跨通道，用户非程序文件与资料保留。build/package.ps1串行publish，四包；ReleaseTools只在受控签名环境生成三清单。release.yml标签触发完整发布，build.yml负责main/PR。RELEASE_NOTES_CURRENT.md只含当前版本，docs/releases保存历史，不能把历史正文整份发布。

仓库Git身份小火车 <kratosthemax@gmail.com>，不改全局配置；ownership仅用git -c safe.directory。保留图标assets/branding及所有入口同一ICO。temp、日志、真实用户资料、密钥不入Git。不复制/安装全局工具到私有目录。回滚开发基线53c4953及此前保留预览；发布后不能覆盖同版本资产，发现发行缺陷递增版本。

## 接续规则

接手先读本页、README、相关源码、git status/log/remote、版本源及实际Release。事实以最新源码/测试/线上核验为准。后续发布授权需按当次用户请求核对，本页不扩张授权。收尾更新实际测试、提交/标签/Release/Actions、剩余缺口与完整修改清单，只有文档源码推送成功后才能声称跨设备接续。

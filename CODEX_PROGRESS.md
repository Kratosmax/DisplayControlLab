## 0.5.0 发布进行中（2026-10-13）

用户已明确授权推送GitHub并发布新版，版本源/README/当前公告已改0.5.0，新功能用minor版本。包含此前全部本地改动，完整变更27文件见git status与本页历史列表，新增Directory.Build.props、README.md、RELEASE_NOTES_CURRENT.md、docs/releases/v0.5.0.md。已核对远程main基线9bbfda0、latest v0.4.0、v0.5.0尚不存在。

本地Release构建0警告/0错误；核心271项、Reader140项/Controller138项UI、Full/Lite各51项更新专项通过。四包已生成：Full Setup51451413、FullZIP73083965、LiteSetup2577964、LiteZIP1016442字节。Lite SHA256921020fa8f9bb1ac519b7888215126ac494d9b54f0e4002078f5cd416f3e70a4。证据temp/validation/release-0.5.0；命令同前版本，目标路径改release-0.5.0及版本号0.5.0；./build/package.ps1构建四包。测试正常用户上下文隔离模拟，无实机写入或覆盖用户安装。签名只在GitHub受控Secret工作流进行，不本地生成伪正式清单。

当前状态待源码提交推送、标签、发行CI、四包/三清单/SHA256SUMS线上核验。发行工作流使用真正0.4.0原始Full/Lite包验证签名候选下载/预检/更新器交接与安装器；不修改旧二进制身份。公告明确旧版准备安装卡住需一次性手动升级，旧UI首次退出根因仍未全复现；不把CLI验证当实机UI已修复证明。

以下待验收章节为本次发行累积开发历史，不能继续解释为“最新版本0.4.0未授权发布”。发布完成将更新本节与发行验收文档；真实Dell HDMI识别码DEL/0xD0DA和DP 0xD0D9不同，未扩大适配包或写入验证范围。

---
## 最新待验收：控制页统一整包选择（2026-10-13）

本轮移除每个功能卡片的来源下拉，ControlsPage顶部唯一“协议包：名称”按钮，展开选择匹配protocol包或内置标准并使用；显示友好名称/版本。一次切换当前设备整包路由，清旧逐项来源和缓存，仅读取刷新；包没覆盖项不混入控制页。安装研究包保留多份，控制包使用一个。Reader生成包应用同步选择；已验证名单/白名单不放宽，插件信任不绕过。旧多来源状态无统一选择时保留，用户新入口选择后统一。

新增ControlPackageSelections兼容旧state，CapabilityCatalog筛选活动包，ControlPackageFeatures约束控制展示；选中包不可用返回空名单需重新选，不静默混包。切换拒绝正在写入/排队/重载，与其他设备独立。整体功能保留读取/写入验证、HDR互锁、原始数据、名称、试写恢复与缓存。本轮没有顺手删底层Choose接口，测试/兼容仍使用它，正常UI已无逐项来源入口。

本轮文件src/Core/Packages.cs、src/Core/Routing.cs、src/UI/WorkspaceWindow.cs、src/UI/CompactControls.cs、src/UI/ToolHost.cs、tests/Platform/ReaderSurveyTests.cs、docs/USER_GUIDE.md、docs/CONTROL_PACKAGE_VALIDATION.md、CODEX_PROGRESS.md。累计23项未提交：CODEX_PROGRESS.md、docs/USER_GUIDE.md、docs/OPTION_NAMES_VALIDATION.md、docs/UPDATE_COLOR_FIX_VALIDATION.md、docs/RECOVERY_FOLD_VALIDATION.md、docs/WARM_PICTURE_VALIDATION.md、docs/READ_LABEL_SYNC_VALIDATION.md、docs/CONTROL_PACKAGE_VALIDATION.md、src/Core/Workspace.cs、src/Core/ImageProbe.cs、src/Core/ReaderSurvey.cs、src/Core/FeatureWriteTests.cs、src/Core/Packages.cs、src/Core/Routing.cs、src/UI/CompactControls.cs、src/UI/FeatureTestPanel.cs、src/UI/GuidedReader.cs、src/UI/LiveControls.cs、src/UI/RecoveryGuide.cs、src/UI/ToolHost.cs、src/UI/UpdatePages.cs、src/UI/WorkspaceWindow.cs、tests/Platform/ReaderSurveyTests.cs。

Release构建0警告/0错误，核心271项、实际Lite Reader140项/Controller138项离屏UI通过。新增整包实际路由/验证名单/标准回退清路由，页面唯一入口、无提供者下拉。已看controller/compact-common.png，紧凑控件两列半行保留。证据temp/validation/whole-package、命令docs/CONTROL_PACKAGE_VALIDATION.md。WPF不适用Playwright，原生离屏渲染；正常用户上下文隔离模拟，无Show/抢焦点、实机GET/SET或用户资料/安装修改。用户未提供自行运行测试命令。

最终本地预览temp/package-staging/0.4.0-Lite-c9618c331c51480eb8ce1e76322c5c16/DisplayControl.Controller.exe（Reader同目录）；ZIP temp/preview/DisplayControlLab-0.4.0-Lite.zip，1015121字节，SHA256 0d927f5531df71c813aec704262ee895056cea809a12dc39de8f1f1fd4f7d5e0。USER_GUIDE同步后重压，仅文档更新，二进制为实测对象。先退出旧两工具，版本仍0.4.0，未提交/推送/发布；main基线9bbfda0。

待办用户实机整包切换验收。互补包先合并后用于控制；真实协议切换/固件全模式未全测。此前首次更新退出根因未复现仍待日志；本轮无构建/测试阻塞，不因此扩大正式发布授权。

---
## 最新待验收：名称/读数同步与验证资料继承（2026-10-13）

用户本轮两条反馈已处理：对比表仍显示旧名称、单项读取不移动强调且整页读丢打开面板；新检测丢名称/写入验证。源码证实快照旧Definition与检测报告标签分离，单项重读原只改反馈；Run新建Report没继承已有成功项，旧资料文件并未删除。

修复：同来源对比表/全部读数使用最新检测名称并保留数值/十六进制；不修改原始快照或变化判定。单项重读就地更新选择、强调行和时间提示，编辑输入不重建；按最近读取时间复用记录；整页重建恢复当前设备打开功能和展开状态。新Run相同设备/协议来源保留名称；HDR/稳定读数/范围/联动依赖/写入条件符合且原证明未被恢复阻塞，沿用本机写入及恢复证据、实测档位，自动检测跳过已有通过项。条件变化保留名称但明确需重试。连续单档成功Retry合并旧实测档位；失败仍撤销该项写入验证。无证据不开放，没有跨Hash猜迁移。

本轮新增改动src/UI/FeatureTestPanel.cs、src/UI/GuidedReader.cs、src/Core/ReaderSurvey.cs、src/Core/FeatureWriteTests.cs、tests/Platform/ReaderSurveyTests.cs、docs/READ_LABEL_SYNC_VALIDATION.md、CODEX_PROGRESS.md。累计19项未提交：CODEX_PROGRESS.md、docs/USER_GUIDE.md、docs/OPTION_NAMES_VALIDATION.md、docs/UPDATE_COLOR_FIX_VALIDATION.md、docs/RECOVERY_FOLD_VALIDATION.md、docs/WARM_PICTURE_VALIDATION.md、docs/READ_LABEL_SYNC_VALIDATION.md、src/Core/Workspace.cs、src/Core/ImageProbe.cs、src/Core/ReaderSurvey.cs、src/Core/FeatureWriteTests.cs、src/UI/FeatureTestPanel.cs、src/UI/GuidedReader.cs、src/UI/LiveControls.cs、src/UI/RecoveryGuide.cs、src/UI/ToolHost.cs、src/UI/UpdatePages.cs、src/UI/WorkspaceWindow.cs、tests/Platform/ReaderSurveyTests.cs。

最终编译0警告/0错误；核心269项；实际最终预览Reader138项/Controller136项WPF离屏UI通过。证据temp/validation/read-label-sync/cache-platform-final、cache-reader、cache-controller，命令详docs/READ_LABEL_SYNC_VALIDATION.md。单项真实按钮更新0x08强调、不重建body；名称暖色显示11/0x0B；打开色温面板保留；真实Run只读重新检测继承证明/名称，HDR变化不冒用旧权限，连续成功单档累计。实际查看feature-test-panel.png；WPF不适用Playwright。正常用户上下文隔离模拟，不Show/抢焦点、不实机GET/SET、不覆盖用户安装资料。早期Reader60秒等待超时后结果138项完成、进程已退出；最终最新两工具明确成功。用户未提供自行运行测试命令。

最终预览temp/package-staging/0.4.0-Lite-ddd2edabf46b4699829022d3342fe3de/DisplayControl.Reader.exe；ZIP temp/preview/DisplayControlLab-0.4.0-Lite.zip，1012322字节，SHA256 3487ac40562667847be85457d753d9b1e7eac9592d28c87a3c1f96053dada264。Core/UI包DLL与最新源码编译哈希一致。先退出旧两个工具。版本仍0.4.0，未提交/推送/发布，正式main基线9bbfda0。

待办用户实机验收完整操作链；此前已被旧版本新结果遮盖的历史记录不删除，但不可直接将旧失败项升级为已验证。真实固件/跨Hash/全模式未验证，仍保留恢复、HDR及导出门禁；旧首次更新退出根因仍待日志确认。本轮无构建/测试阻塞。

---
## 最新待验收：色温对应与手动画面联动恢复（2026-10-13）

本轮包含用户两条反馈：暖色读到11而命名面板仅0x0B无法对应，以及画面预设反复待核查。真实记录：画面0→0已恢复，色温11→5、音量/静音11→5尚有差异；声音读数不认定真实声音变化。用户菜单暖色对应11，仅该用户观察，不全局改标准标签。已查实手动画面选值走普通Trial而自动走ImageProbe，前者未恢复联动色温。

修复：未知状态显示十进制/十六进制；命名面板读取当前设备最近完整手动读数，选中并强调对应行，11/0x0B无需换算；无新读取时标为检测时数据。手选色温/画面预设复用ImageProbe，仅试所选档位；恢复画面后用原可写标准色温来源及声明档位恢复捕获原色温，然后图像增益，最终完整基线核查。互锁/白名单不变，不写无关音量/静音，不猜未知协议；外部状态改变仍停止。恢复面板列相关未恢复项目，画面已恢复也不再只要求改画面。旧未恢复记录不抹除，先按基线恢复并只读核查。

本轮修改src/Core/Workspace.cs、src/Core/ImageProbe.cs、src/Core/FeatureWriteTests.cs、src/UI/FeatureTestPanel.cs、src/UI/RecoveryGuide.cs、tests/Platform/ReaderSurveyTests.cs、docs/WARM_PICTURE_VALIDATION.md、CODEX_PROGRESS.md。累计18项未提交：CODEX_PROGRESS.md、docs/USER_GUIDE.md、docs/OPTION_NAMES_VALIDATION.md、docs/UPDATE_COLOR_FIX_VALIDATION.md、docs/RECOVERY_FOLD_VALIDATION.md、docs/WARM_PICTURE_VALIDATION.md、src/Core/Workspace.cs、src/Core/ImageProbe.cs、src/Core/ReaderSurvey.cs、src/Core/FeatureWriteTests.cs、src/UI/FeatureTestPanel.cs、src/UI/GuidedReader.cs、src/UI/LiveControls.cs、src/UI/RecoveryGuide.cs、src/UI/ToolHost.cs、src/UI/UpdatePages.cs、src/UI/WorkspaceWindow.cs、tests/Platform/ReaderSurveyTests.cs。

最终Release构建0警告/0错误；核心265项、实际Lite预览Reader135项/Controller133项离屏UI通过。新增手选画面改变色温并恢复、仅开放所测+原档位，最近11匹配0x0B强调。首次模拟原12无内置名称恢复失败，改用声明规则草稿并保留可写来源互锁后通过。查看picture-recovery-named.png；WPF不适用Playwright。命令/证据见docs/WARM_PICTURE_VALIDATION.md及temp/validation/warm-picture。正常用户上下文隔离模拟，不Show/抢焦点、不实机GET/SET、不覆盖用户安装资料。用户未提供自行运行测试命令。

预览ZIP temp/preview/DisplayControlLab-0.4.0-Lite.zip，1008325字节，SHA256 97e96b366dd4ea05c586573589eec70416730b68e8ebf3ccc065d2fbf9dc4e13；入口temp/package-staging/0.4.0-Lite-272de2dc2996406d916267dc67c364b9/DisplayControl.Reader.exe。先退出旧两个工具。仍0.4.0本地预览，无提交/推送/发布；main基线9bbfda0。

待办：用户先画面标准+色温暖色核查旧恢复基线；若音量/静音等仍不匹配，保留未知，不盲写。真实固件别名、全模式与外部干预未全测。此前首次更新退出根因仍未复现，记录在历史验收页，不宣称本轮解决。无当前构建/测试阻塞。

---
## 最新待验收：恢复提示与折叠状态（2026-10-13）

本轮用户反馈画面预设突然需恢复却无法理解、反复读取折叠菜单自动收起。真实只读证据：恢复基线画面预设3=用户命名电影，最后核查0=标准；旧基线规则为空导致原提示不认名称。停止错误为“设备、模式或外部设置改变，停止覆盖”，最终快照不足以确定触发瞬间因果，本轮不声称已修复该未知停止原因。

修复：RecoveryGuide优先使用用户保存标签，显示原来电影；核查失败直接显示当前/目标（最多三项，更多折叠），未知名称保留记录目标，设备/HDR不匹配明确说明。未绕过基线比较、未强写；完整恢复匹配才解除暂停。ShowPage按设备/页面/分组/视图/嵌套标题在内存保留手动展开及收起状态，连续读取不再自动折叠。

本轮修改：src/UI/WorkspaceWindow.cs、src/UI/RecoveryGuide.cs、src/UI/GuidedReader.cs、docs/RECOVERY_FOLD_VALIDATION.md、CODEX_PROGRESS.md。累计未提交15文件：CODEX_PROGRESS.md、docs/USER_GUIDE.md、docs/OPTION_NAMES_VALIDATION.md、docs/UPDATE_COLOR_FIX_VALIDATION.md、docs/RECOVERY_FOLD_VALIDATION.md、src/Core/ImageProbe.cs、src/Core/ReaderSurvey.cs、src/UI/FeatureTestPanel.cs、src/UI/GuidedReader.cs、src/UI/LiveControls.cs、src/UI/RecoveryGuide.cs、src/UI/ToolHost.cs、src/UI/UpdatePages.cs、src/UI/WorkspaceWindow.cs、tests/Platform/ReaderSurveyTests.cs。

实际验证：Release构建0警告/0错误；实际预览包Reader134项、Controller132项WPF离屏UI通过（正常用户上下文顺序隔离），人工查看package-reader/picture-recovery-named.png。覆盖初次/连续读取保留展开、嵌套展开、主动折叠，旧基线无标签显示已命名电影、未恢复列标准→电影、改回3后解除门禁。初次旧断言与新目标值显示冲突已更新；并行沙盒Controller60秒超时，正常上下文顺序最终通过，无完整失败结果冒充成功。核心未改，本轮未重跑，上一轮264项保留历史。命令/证据temp/validation/recovery-fold与docs/RECOVERY_FOLD_VALIDATION.md。WPF不适用Playwright，无抢焦点、用户安装/资料覆盖或实机GET/SET。用户未提供自行运行测试命令。

最新预览temp/package-staging/0.4.0-Lite-ff90768816ac4e568893bee58691c281/DisplayControl.Reader.exe；ZIP temp/preview/DisplayControlLab-0.4.0-Lite.zip，1006599字节，SHA256 1fa77f26b78284c145f4181befdb1ff870b3a97ed2eb5e8efe005668e89620c1。退出旧两个工具再运行。版本仍0.4.0，未提交/推送/发布，不覆盖线上包；回滚基线main 9bbfda0，本轮保留前轮改动。

待办：用户实机把画面预设改回电影后检查；若仍不匹配，新提示给出具体剩余项。自动恢复停止瞬间与首次更新不退出根因仍待查实；本轮只修已证实提示/状态保留缺陷，不放宽控制导出规则。

---
## 最新待验收：选项命名（2026-10-13）

用户反馈画面预设三档不能改名。本轮新增读取器测试面板“给选项命名”：展开、填写、保存；名称保存在检测记录，重新应用/导出交给控制器。仅改标签，控制码、测试结果和已验证白名单不变，无硬件通信。失败不保存且回滚内存，空白/换行/超长拒绝。测试/复查入口沿用，第三方/厂商/图像试调的既有名称保留规则不变。

本轮修改：src/Core/ReaderSurvey.cs、src/UI/FeatureTestPanel.cs、tests/Platform/ReaderSurveyTests.cs、docs/USER_GUIDE.md、docs/OPTION_NAMES_VALIDATION.md、CODEX_PROGRESS.md。累计未提交还包括前轮src/Core/ImageProbe.cs、src/UI/LiveControls.cs、src/UI/ToolHost.cs、src/UI/UpdatePages.cs、src/UI/WorkspaceWindow.cs、docs/UPDATE_COLOR_FIX_VALIDATION.md（完整累计12文件）。

Release编译0警告/0错误；核心264项通过；实际Lite预览Reader127项/Controller125项WPF离屏UI通过，真实三档保存按钮验证落盘/重开/导出/无硬件命令/测试范围不变；人工查看feature-test-panel.png。首次模拟夹具漏Writable导致导出拒绝，修复夹具后最终验收通过，产品门禁不变。命令见docs/OPTION_NAMES_VALIDATION.md，证据temp/validation/option-names。用户未提供自行运行测试命令。WPF不适用Playwright，无实机GET/SET、用户安装覆盖或抢焦点。

最新本地预览入口temp/package-staging/0.4.0-Lite-88ea3a1d66a247acb326091eebe0d127/DisplayControl.Reader.exe；ZIP temp/preview/DisplayControlLab-0.4.0-Lite.zip，1003078字节，SHA256 1ef922c115b17177bf78858f6662057ea1805245e928baae4c8df46b7125b20f。旧两个工具退出后再运行，避免单实例转发。程序包内USER_GUIDE已同步，SHA256SUMS已重算，二进制为最终UI测试对象。正式仍0.4.0，无提交/推送/发布，预览不是线上包。

待办：用户按真实菜单为三档命名并验收；本轮无阻塞。上轮首次更新未退出的精确根因仍待日志核查，不将本轮命名修复描述为解决该根因。回滚基线main 9bbfda0，保留上轮未提交修复；本轮任务不自动授权发布。

---
## 当前待验收修复（2026-10-13）

本轮处理更新首次点击停留与色温映射丢失；正式版本仍0.4.0，无提交/推送/标签/发布。下方0.4.0发行记录为历史已完成状态，本轮交付仅本地预览。

已证实：用户Dell能力声明0x14值05/08/0B/0C（四档）；ImageProbe试调临时code-*规则覆盖永久名称；用户update-error.txt于10:02:25记录更新器等待超时。更新首次点击未退出的精确因果尚未复现，不将进度回调/轮询竞争当作已证实根因。

修复：图像试调保留可靠名称；旧报告显示/测试/导出从原来源恢复名称，型号/第三方自定义优先，0x0C不猜名；声明数量、实际可写白名单与验证门禁保留。更新交接立即请求退出，交接后隐藏重复更新按钮、忽略迟到进度，失败清除过期进度；更新期间阻止新增Run/队列写/轮询，update-ui.log记录准备、启动与退出请求（有界、日志失败不阻断更新）。

完整修改文件：src/Core/ImageProbe.cs、src/Core/ReaderSurvey.cs、src/UI/LiveControls.cs、src/UI/ToolHost.cs、src/UI/UpdatePages.cs、src/UI/WorkspaceWindow.cs、src/UI/FeatureTestPanel.cs、tests/Platform/ReaderSurveyTests.cs、CODEX_PROGRESS.md、docs/UPDATE_COLOR_FIX_VALIDATION.md。

验证：Release编译0警告/0错误；核心262项、Reader126项/Controller124项原生WPF离屏UI、Lite更新专项51项通过，真实检查feature-test-panel.png与updates-handoff.png。沙盒管道/本地测试代理失败后，正常用户上下文同一隔离测试通过；无真实显示器GET/SET、无用户安装替换，无窗口抢焦点。Playwright不适用WPF，采用原生渲染。证据temp/validation/update-color-fix，命令详见docs/UPDATE_COLOR_FIX_VALIDATION.md。用户未提供自行运行测试命令。

预览：temp/preview/DisplayControlLab-0.4.0-Lite.zip，1000013字节，SHA256 a893b4e18363693e64506c0404b20356c277ebec9df5ef6fe0a4d60356f8bb14。入口由temp/preview/preview-path.txt记录；非正式Release，不改用户安装/资料。运行前退出旧Reader/Controller，避免单实例导航旧进程。

待办：用户实机验收；若首次更新仍卡住，核对新update-ui.log及更新器日志，再修确证根因。本轮模拟Prepare/Launch/Close链通过，但未验证真实首次点击的窗口关闭/旧正式客户端UI/两工具同时运行情形；旧正式客户端不能靠新服务端包修复其UI，必要时手动过渡。回滚点为本轮开始的main HEAD（9bbfda0），未改Git。正式发行仍需届时授权，不覆盖0.4.0资产。

---
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

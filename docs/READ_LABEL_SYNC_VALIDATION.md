# 读取结果名称与选项面板同步

2026-10-13，0.4.0本地预览，未发布。

问题有代码证据：ManualReaderPage直接用Snapshot里的旧Definition显示Comparison结果；SaveOptionNames修改检测报告而不修改原快照；FeatureTestPanel的“重新读取此项”仅更新feedback，强调行只在构造时判断；GuidedPage重建后details默认为折叠且未重开当前功能。

修复：对比表和所有读数采用同设备最新检测报告标签，且仅在原协议来源/Hash匹配时映射，历史原始读数/定义不改。枚举/布尔显示名称及十进制/十六进制编码，无新名称时保持原解释或未知值；Comparison判定仍使用原数据，改名不制造变化。检测报告每次渲染仅读取一次。

单项读取成功在内存保留带时间的Reading，就地更新下拉选择、强调行和读数提示，不重建面板，不丢输入框内容。重新构造时在手动整屏读数与单项读数之间选择较新记录。读失败清除本面板当前选中强调并提示失败，不伪造成功值。记录每台设备当前打开FeatureId，整页读取/刷新后重开该功能，沿用Expander状态保留；恢复未确认时打开恢复指南，不绕过门禁。

实际命令：

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
./build/package.ps1 -LiteOnly -PreviewOnly
$preview=Get-Content temp/preview/preview-path.txt -Raw
$p=Start-Process (Join-Path $preview 'DisplayControl.Reader.exe') -ArgumentList '--ui-test','temp/validation/read-label-sync/reader' -WindowStyle Hidden -PassThru
$p.WaitForExit(60000)
$p=Start-Process (Join-Path $preview 'DisplayControl.Controller.exe') -ArgumentList '--ui-test','temp/validation/read-label-sync/controller' -WindowStyle Hidden -PassThru
if(!$p.WaitForExit(60000)) { $p.WaitForExit(60000) }
```

Release构建0警告/0错误。本轮UI变更，核心上轮265项通过，本轮未重跑。读取器实际包最终138项UI通过；首次等待60秒超时，但之后读取ui-tests.json确认138项完整结果、进程已退出，未把超时当通过。控制器结果以CODEX_PROGRESS为准。新增真实单项重读按钮选中0x08、移动强调、不重建body；最新命名暖色显示11/0x0B；页面重建保留色温面板。检查feature-test-panel.png实际离屏渲染；WPF不适用Playwright，不Show/抢焦点，数据隔离项目temp，没有实机GET/SET或用户资料修改。

预览ZIP1010786字节，SHA256 71c35922a9dc9dfbe639ae22a9a5ac1468d16bdcc33ae435496f290f490c4c95；入口temp/package-staging/0.4.0-Lite-0136987a81ff44d29867dedf1bc32a4b/DisplayControl.Reader.exe。退出旧两个工具再运行。本轮仅修改src/UI/GuidedReader.cs、src/UI/FeatureTestPanel.cs、docs/READ_LABEL_SYNC_VALIDATION.md、CODEX_PROGRESS.md，保留以前全部未提交修复。用户未提供自行运行测试命令。名称不能跨来源猜测；手动赋名真实性仍需用户菜单核对。

## 同轮追加：重新检测保留已有资料

用户继续反馈重新读取后名称/写入验证丢失。源码Run每次创建新Report，原先只复制未恢复门禁，没有复制用户名称和通过证据；旧文件仍在，但新结果遮盖它们。

新增InheritSavedResults：同设备、原来源/Hash、backend及VCP相同，保留枚举/布尔映射名称。原项写入恢复通过且未受未恢复联动影响时，核对HDR、稳定读取、写入条件、该项/联动/依赖范围和来源；符合后复制测试证据、通过档位及原定义，标明沿用记录，并避免自动重复试写。条件改变仍保留名称，但不开放原写入证明，说明需重试。普通单次读取无需新检测报告；Run只读重新检测也能导出沿用通过项。

Retry连续单档测试成功会合并旧已验证档位，新失败不保留该项写入权限。历史Report和Operation文件不删除。未实现跨协议Hash迁移或自动把作者write-confirmed当本机证明。不能把此前已经被旧版本覆盖的最新记录当可靠验证；源资料不足时仍需明确重试。

新增源码变更：src/Core/ReaderSurvey.cs、src/Core/FeatureWriteTests.cs、tests/Platform/ReaderSurveyTests.cs。最终构建0警告/0错误，核心269项、实际最新Lite Reader138项/Controller136项离屏UI通过。核心证据temp/validation/read-label-sync/cache-platform-final，UI证据cache-reader/cache-controller；正常用户上下文隔离模拟，无实机控制。新增真实Run缓存保留名称/证明、HDR改变保留名称但撤销缓存权限、连续单档累计。最终包Core/UI哈希与最新编译源码一致，非旧包测试冒充新版。

最终最新ZIP1012322字节，SHA256 3487ac40562667847be85457d753d9b1e7eac9592d28c87a3c1f96053dada264；入口temp/package-staging/0.4.0-Lite-ddd2edabf46b4699829022d3342fe3de/DisplayControl.Reader.exe。未提交/推送/发布。旧前半页产物为同轮历史验收，不作为最终入口。

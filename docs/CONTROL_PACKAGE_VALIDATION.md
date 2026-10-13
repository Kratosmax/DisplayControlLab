# 控制页统一协议包入口

2026-10-13，0.4.0本地预览，未发布。

原FeatureCard在Candidates多于一个时为每项添加提供者ComboBox；控制页功能因此重复显示user.detected-*内部编号。本次移除该入口，在ControlsPage顶部复用按钮/下拉样式建立唯一“协议包：名称”入口，展开后选包并点“使用此协议包”。列表仅含匹配当前显示器的protocol包与内置标准，插件代码信任仍走原管理入口，不在此按钮绕过。

PackageState新增ControlPackageSelections，旧数据缺字段仍兼容。SelectControlPackage一次状态保存选择包，关联/启用匹配包，清除该设备旧逐项路由并整包设置；不会改其他设备。CapabilityCatalog只用选中包，标准功能可供读取研究，但控制页通过ControlPackageFeatures只展示选中包覆盖功能，用户检测包继续受VerifiedControlProfiles名单限制。切回standard清除旧逐项来源。Reader生成包应用时同步统一选择，原资料及其他已安装包不删除。

切换前拒绝当前写入/待写/HDR模式重载，切换后清设备缓存并只读刷新；不自动发送显示器设置。原读取与写入安全、单项测试与已验证过滤沿用。选中包不可用时控制名单为空，避免静默回退混包；需要重新选择。未选择统一包的旧多来源配置保留，用户点击新按钮后统一，不自动猜用户要保留哪个第三方协议。

验证命令：

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/whole-package/platform
./build/package.ps1 -LiteOnly -PreviewOnly
$preview=Get-Content temp/preview/preview-path.txt -Raw
foreach($tool in @('Reader','Controller')) {
  $dir='temp/validation/whole-package/'+$tool.ToLowerInvariant()
  $p=Start-Process (Join-Path $preview ('DisplayControl.'+$tool+'.exe')) -ArgumentList '--ui-test',$dir -WindowStyle Hidden -PassThru
  if(!$p.WaitForExit(60000)) { $p.WaitForExit(60000) }
}
```

构建0警告/0错误、核心271项、实际Lite Reader140项/Controller138项离屏UI通过。新增完整路由实际包选择/标准回退，检测包仅展示验证项；UI验证控制页一个统一入口、无功能提供者下拉。已检查controller/compact-common.png，功能两列半行不再重复来源下拉。WPF不适用Playwright，采用原生离屏渲染。正常用户上下文隔离模拟，未Show/抢焦点、不实机GET/SET或覆盖用户资料/安装。USER_GUIDE已更新并同步预览后重压ZIP，二进制不变。

最终ZIP1015121字节，SHA256 0d927f5531df71c813aec704262ee895056cea809a12dc39de8f1f1fd4f7d5e0，入口temp/package-staging/0.4.0-Lite-c9618c331c51480eb8ce1e76322c5c16/DisplayControl.Controller.exe。先退出旧两工具。本轮未提交/推送/发布。完整包统一使用；互补包需要先合并后控制，不再在日常控制页逐项混选。真实显示器协议切换仍需用户验收。

本轮修改：src/Core/Packages.cs、src/Core/Routing.cs、src/UI/WorkspaceWindow.cs、src/UI/CompactControls.cs、src/UI/ToolHost.cs、tests/Platform/ReaderSurveyTests.cs、docs/USER_GUIDE.md、docs/CONTROL_PACKAGE_VALIDATION.md、CODEX_PROGRESS.md；保留前轮未提交改动。用户未提供自行运行测试命令。

# 读取器选项命名验收

2026-10-13，0.4.0本地预览，未提交、推送或发布。

原界面FeatureTestPanel只有测试与重新读取入口，没有选项名称编辑。本次在枚举/布尔选项面板加入“给选项命名”：每个现有选项一个名称输入框，一次保存，成功后就地更新下拉名称。名称保存到检测报告，重新应用或导出后控制器使用新标签，不自动覆盖已有控制器包。

ReaderSurvey.SaveOptionNames仅修改现有选项标签；校验1至80字、不换行、存在选项ID；保存失败恢复内存定义。控制码、TestedValues、WriteStatus和白名单保留，命名不发送硬件命令。源码原有图像试调保留名称机制沿用。命名不代表已经验证功能含义，用户应按菜单实际名称填写。

实际验证：

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/option-names/platform
./build/package.ps1 -LiteOnly -PreviewOnly
$preview=Get-Content temp/preview/preview-path.txt -Raw
$p=Start-Process (Join-Path $preview 'DisplayControl.Reader.exe') -ArgumentList '--ui-test','temp/validation/option-names/reader-final' -WindowStyle Hidden -PassThru
$p.WaitForExit(60000)
$p=Start-Process (Join-Path $preview 'DisplayControl.Controller.exe') -ArgumentList '--ui-test','temp/validation/option-names/controller-final' -WindowStyle Hidden -PassThru
$p.WaitForExit(60000)
```

编译0警告/0错误，核心264项通过（正常用户上下文，仅隔离模拟），实际预览包Reader127项、Controller125项WPF离屏测试通过。三档真实保存按钮验证标准/电影/游戏名称落盘、重开和导出，未发送额外显示器命令，未改变测试白名单；空名称拒绝且原名称保留。已查看reader-final/feature-test-panel.png，折叠和展开命名区域可读。首次UI测试的模拟报告漏设Writable导致导出失败，补齐测试夹具后两工具最终验收通过；未放宽产品导出门禁。

WPF不适用Playwright，使用原生离屏渲染，无窗口焦点、用户安装修改或实机GET/SET。核心测试源码新增两项，UI新增一项；仅更新本地Lite，正式版本号和线上Release保持不变。打包后同步USER_GUIDE到预览docs并重新压缩、计算SHA256SUMS，程序二进制未变。产物大小/哈希与入口以CODEX_PROGRESS及temp/preview为准。

本轮源码变更：src/Core/ReaderSurvey.cs、src/UI/FeatureTestPanel.cs、tests/Platform/ReaderSurveyTests.cs、docs/USER_GUIDE.md、docs/OPTION_NAMES_VALIDATION.md、CODEX_PROGRESS.md；保留前轮更新/色温修复全部未提交改动。用户未提供自行运行测试命令。命名真实性依赖用户菜单观察，本轮未实机验证三种画面实际效果。

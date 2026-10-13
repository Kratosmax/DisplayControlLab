# 更新交接与色温名称修复验收

日期：2026-10-13，版本0.4.0本地预览，未发布。

色温问题有源码证据：ImageProbe将临时code-*未知名称写回永久定义。现保留原有可靠标签，旧报告以原来源补回名称；保留自定义名称、条件规则、可写白名单。Dell声明四档05/08/0B/0C，标准名称6500K/9300K/用户自定义，0x0C不命名。

更新器真实日志记录等待退出超时，但无法由现有日志确定首次点击未退出的根因。UI交接改为启动后立即请求关闭，阻止重复启动和迟到进度覆盖，失败清空进度；增加有界update-ui.log。模拟下载→交接→退出调用及失败重试通过，不能据此宣称已复现或彻底解决用户真实首次关闭问题。

实际命令（从项目根目录执行）：

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/update-color-fix/final-platform
$p=Start-Process src/Reader/bin/Release/net8.0-windows/DisplayControl.Reader.exe -ArgumentList '--ui-test','temp/validation/update-color-fix/final-reader' -WindowStyle Hidden -PassThru
$p.WaitForExit(60000)
$p=Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/update-color-fix/final-controller' -WindowStyle Hidden -PassThru
$p.WaitForExit(60000)
./build/package.ps1 -LiteOnly -PreviewOnly
& tests/Updates/bin/Release/net8.0-windows/DisplayControl.Update.Tests.exe . temp/validation/update-color-fix/updates-Lite-user temp/preview/DisplayControlLab-0.4.0-Lite.zip Lite
```

编译0警告/0错误；核心262项；Reader126项、Controller124项；更新专项51项通过。核心和更新专项在正常用户上下文隔离temp运行，沙盒首次测试分别被进程管道和本地HTTP测试代理限制；正常上下文复跑通过。未操作真实显示器、用户资料或安装。

已查看离屏真实渲染：final-reader/feature-test-panel.png、updates-handoff.png。下拉控件真实Items确认四项、三个已知名称、一个未知；UI首次完成触发一次Launch和一次Close，重复点击不重复交接，迟到进度不覆盖终态，失败允许重试。WPF不适用Playwright。

预览ZIP1000013字节，SHA256 a893b4e18363693e64506c0404b20356c277ebec9df5ef6fe0a4d60356f8bb14。包入口由temp/preview/preview-path.txt记录。实机验证前退出旧两个工具；检查旧记录色温测试下拉；旧正式版UI升级若仍卡住需手动过渡。此预览与正式同版本，不能用于验证线上新版本发现；用户首次卡住的真实关闭根因待新日志核查。

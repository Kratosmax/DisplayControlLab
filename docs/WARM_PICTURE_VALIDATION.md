# 色温对应与画面预设联动恢复

2026-10-13，0.4.0本地预览，未发布。

用户证据：菜单“暖色”读取11，命名列表0x0B（十进制11）原标签用户自定义。标准标签不保证厂商菜单一致。本轮不自动将所有显示器11命名暖色。显示未知状态统一十进制/十六进制；命名面板采用当前设备最近完整手动读取，自动选中同值并强调命名行。检测历史读数明确标为“检测时读取”，非实时；用户按菜单改名，重新应用后进入控制器。

真实恢复记录显示画面预设0→0已恢复，但色温11→5、音量/静音11→5；不能认定声音实际变化。源码FeatureWriteTests只在未手选档位时走ImageProbe，手选进入普通Trial，缺少图像联动恢复。本轮手选与自动图像预设统一专用流程，手选只试该档位；画面预设恢复后，按已声明、原可写标准色温规则恢复捕获的原档位，再处理原RGB/亮度/对比度。实际来源/模式/设备互锁、AllowedWriteValues和最终完整基线核查保留，不写音量/静音等无关通道，不猜未知协议。检测到外部状态变化仍停止；若无法可靠恢复仍要求核查，不把SET成功当完整恢复。

恢复标题改为核查该项及相关设置，并在打开提示时列最近核查仍有差异的功能，避免画面预设已恢复却反复要求只改它。现有未恢复记录仍需先恢复原基线，不能用新试写覆盖。

验证命令：

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/warm-picture/platform-final
./build/package.ps1 -LiteOnly -PreviewOnly
$preview=Get-Content temp/preview/preview-path.txt -Raw
foreach($tool in @('Reader','Controller')) {
  $dir='temp/validation/warm-picture/'+$tool.ToLowerInvariant()
  $p=Start-Process (Join-Path $preview ('DisplayControl.'+$tool+'.exe')) -ArgumentList '--ui-test',$dir -WindowStyle Hidden -PassThru
  $p.WaitForExit(60000)
}
```

编译0警告/0错误、核心265项通过。新增模拟手动画面预设改变色温后单档恢复原色温/原画面，仅保留已测0/1；UI验证最近手动读取11自动选中code-0b并标出11/0x0B。首次模拟恢复失败暴露原12不在内置色温规则，修为声明档位草稿并保留原来源/可写互锁，最终回归通过。两工具最终结果见CODEX_PROGRESS，测试与用户资料隔离，未进行实机GET/SET或替换用户安装。WPF不适用Playwright，使用原生离屏截图。

ZIP1008325字节，SHA256 97e96b366dd4ea05c586573589eec70416730b68e8ebf3ccc065d2fbf9dc4e13；入口temp/package-staging/0.4.0-Lite-272de2dc2996406d916267dc67c364b9/DisplayControl.Reader.exe。本轮未提交/推送/发布。固件声音别名、真实画面效果、外部干预未全测，完整基线不匹配不会解除恢复门禁。

# 恢复提示与折叠状态验收

2026-10-13，0.4.0本地预览，未发布。

用户真实记录显示：画面预设恢复基线为3，最后核查为0；用户已命名3=电影、0=标准，但恢复基线的旧定义没有标签。原RecoveryGuide优先用旧基线显示名称，导致“没认出原选项”。现优先使用检测结果里的选项名称；未知名称至少显示已记录目标值。检查不匹配时直接列最多三项当前/目标名称，其他细节折叠。设备/HDR不匹配也有明确反馈。

停止自动覆盖的真实错误为“设备、模式或外部设置改变，停止覆盖”。最后快照不能证明触发瞬间是哪一项改变，本轮不猜测原因、不删除恢复门禁、不强写用户屏幕。手动改回原菜单后只读核查；全部基线匹配才解除暂停。命名不改变协议来源或测试范围。

ShowPage每次重建body前保存Expander状态，构建后恢复，按设备/页面/功能分组/视图与嵌套标题区分。本轮只保留同进程状态，不新增用户设置文件；用户主动折叠同样保留。手动查找菜单、所有读数等不再因一次读取收起。

实际命令：

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
./build/package.ps1 -LiteOnly -PreviewOnly
$preview=Get-Content temp/preview/preview-path.txt -Raw
foreach($tool in @('Reader','Controller')) {
  $dir='temp/validation/recovery-fold/package-'+$tool.ToLowerInvariant()
  $p=Start-Process (Join-Path $preview ('DisplayControl.'+$tool+'.exe')) -ArgumentList '--ui-test',$dir -WindowStyle Hidden -PassThru
  $p.WaitForExit(60000)
}
```

Release构建0警告/0错误。本轮仅UI代码，核心验证沿用上一轮264项，未冒充本轮重跑。首次UI测试断言仍要求隐藏全部编码，与新增未知目标值显示冲突，更新断言；补齐画面测试夹具默认值读取。Reader最终134项通过；Controller并行沙盒测试60秒超时、无完整结果，改为正常用户上下文顺序验收实际预览包。最终两工具结果见CODEX_PROGRESS。测试始终隔离项目temp，无窗口显示/焦点、实机GET/SET或真实用户资料修改。

新增回归：手动展开后首次/连续读取保持展开，嵌套展开保持，主动收起保持；基线无标签但用户命名存在时显示电影；未恢复提示现在标准/目标电影；改回3后解除恢复。真实离屏截图package-reader/picture-recovery-named.png已检查；WPF不适用Playwright。

本轮修改：src/UI/WorkspaceWindow.cs、src/UI/RecoveryGuide.cs、src/UI/GuidedReader.cs、docs/RECOVERY_FOLD_VALIDATION.md、CODEX_PROGRESS.md，保留前两轮全部未提交改动。用户未提供自行运行测试命令。预览ZIP1006599字节，SHA256 1fa77f26b78284c145f4181befdb1ff870b3a97ed2eb5e8efe005668e89620c1，入口temp/preview/preview-path.txt。用户应退出旧两个工具再打开；恢复提示真实性和自动恢复停止瞬间仍需实机反馈。

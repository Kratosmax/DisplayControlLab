# 主屏菜单与可写范围核验

2026-10-11，AOC Q27G4ZMN / AOCB411，本机 AMD DisplayPort、系统SDR、显示器HDR关闭。型号包1.4.0，主程序正式版本未改，非正式Release。

## 本轮结果

| 功能 | 1.4.0行为 | 实机证据 |
|---|---|---|
| 生态调节 | 八档可选；最后一档“均匀亮补偿” | 1–7逐档写入、回读、恢复0 |
| DCR | 关闭/开启可选 | 0→1→0 |
| 伽马 | 2.0/2.2/2.4可选 | 编码0/1/2已确认；厂商源码提供数字标签 |
| Overdrive | 关闭/弱/中/强可选 | 1–3逐档往返 |
| 低蓝光 | 关闭及四个模式可选 | 1–4逐档往返 |
| 游戏色调、暗场 | 设备当前范围0–20可调 | 边界与中间样本，不宣称穷举每个整数 |
| 区域调光 | 待适配，说明菜单四档 | 早期E2A0查询返回result=1，无有效值 |
| 色彩空间 | 待适配，说明标准/sRGB | 早期E2A0查询返回result=1，无有效值 |

照片只证明菜单可用项，不证明对应DDC编码。伽马标签来自本机G-Menu的sources/062.txt：Gamma1=2.2、Gamma2=2.0、Gamma3=2.4、Gamma4=1.8、Gamma5=2.6；设备编码来自Entity.Lib的Gamma_E枚举。额外测试已知Gamma4编码3，发送后仍回读0，未确认成功，因此不开放1.8。Gamma5编码4未继续测试，不宣称它一定不能写。maximum=2不能单独作为枚举编码上限证明；当前实际可写范围以往返测试为准。未验证物理伽马曲线或OSD视觉同步。

此前低输入延迟、准星、帧计数、游戏模式、图像比例只读；当前新增结果见下方1.5.0。区域调光、色彩空间、Adaptive-Sync仍缺有效控制协议。包改称“型号适配”，不再称“完整适配”，删除重复的图像比例菜单占位。通用description用于显示/编辑限制说明，不新增任何未知SET报文。

## 证据与重跑

temp/validation/osd-controls/live-verification.json：25次不同目标测试，均写入回读确认并恢复；50次SET，最终17项观察值与基线一致。gamma-extended-verification.json：额外1次目标测试未确认，恢复成功，2次SET，最终基线一致。测试记录不证明所有未观察硬件内部状态均未变化。验证期间不切系统HDR、不Show工具窗口、不注入输入。用户默认安装包与资料未改，测试安装仅在temp隔离资料。

核心回归148项通过；UI使用原生WPF离屏宿主代替Playwright，真实窗口不显示。打包产物、UI结果、截图和哈希见同目录package-check.json和ui。验证命令：

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/osd-controls/core
$p = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/osd-controls/ui' -WindowStyle Hidden -Wait -PassThru
./build/package.ps1 -LiteOnly -PreviewOnly
```

verify-controls.py包含真实SET，会修改屏幕并恢复，只适用于明确需要重复硬件验证时；不要把它当只读测试。测试用的候选包不会分发。安装本轮正式示例须使用新宿主，导入1.4.0包、替换同id安装、应用到主屏。

## 边界

HDR开启/未知时的保护保持有效；本轮未做危险HDR冲突写入。仅本机型号/固件/显卡通道核验，其他固件和显卡可能拒绝同一报文；新增档位不代表已经支持其他显示器。没有提交、推送、发布或自动修改默认用户资料。

## 1.5.0 后续研究（2026-10-11）

保留用户认可的界面，仅更新原型号包：低输入延迟和准星开放开关；帧计数器开放关闭、右上、右下。三项均要求系统 SDR、显示器 HDR 已确认关闭，HDR开启或未知仍禁用。

厂商静态证据：Entity.Lib 的 ExternVCPOpCode_E 将低输入延迟/准星/帧计数定义为0x55/0x5A/0x5B；Option.Lib 的 SetValue(ExternVCPOpCode_E,int) 经 F8E1.Pre2/Cmd 发送枚举低字节和值。准星开关及帧计数器枚举另有专门设置入口。没有复制或分发厂商DLL。

| 项目 | 实测 | 当前开放 |
|---|---|---|
| 低输入延迟 | 1→0→1，回读确认 | 关闭/开启 |
| 准星 | 0→1→0，回读确认 | 关闭/开启 |
| 帧计数器 | 0→1→0、0→2→0，回读确认 | 关闭/右上/右下 |
| 帧计数器左侧两档 | SET 3回读4；SET 4回读3；恢复0均确认 | 不开放；保留原始读取值 |

左侧编码具有写入/读取不对称的证据，但尚未人工确认实际OSD位置，不能简单交换标签就称适配完成。后续可研究分别声明读取编码与写入编码；现有包用allowedWriteValues=[0,1,2]限制，UI与worker共同拒绝其他值。

受控实测共14次SET（首次低输入延迟2次，第二轮10次，编码4专项2次）。首次在目标已确认后，标准亮度/对比度/RGB快照出现传输错误；立即恢复原值并确认，不继续该轮。后续测试只对失败项做500/1000ms有界GET复核，不重发目标SET。记录每次目标与恢复快照，最终19项协议观察值与基线一致，无其他观察项联动。不能由这些记录确定传输错误的根因，或证明全部未观察内部状态均恢复；没有人工确认OSD位置、物理延迟或画面效果。

证据：temp/validation/game-controls/first-pass-read-transient.json、live-verification.json、frame-four-verification.json；可重跑脚本verify-controls.py包含真实SET，不是只读测试。运行环境为本机AMD/DisplayPort、240Hz，未切系统HDR，不启动GUI、不注入鼠标键盘、不更改默认用户包。游戏模式/比例仍缺完整联动恢复验证；区域调光/色域/Adaptive-Sync仍无有效协议，不发送猜测指令。

本轮Release构建零警告错误，172项核心测试通过，源码控制器与打包读取器各68项WPF离屏UI检查通过。专项宿主另验证16项：三项SDR可操作、帧计数左侧禁用、游戏模式只读、系统HDR/显示器HDR/未知状态锁定、渲染零请求及不显示窗口。截图与结果在game-controls/rendered。原生WPF不使用Playwright；未抢焦点，未做真实鼠标下拉操作或跨DPI测试，本轮没有布局改动。

可重跑：Platform.Tests.exe <仓库> temp/validation/game-controls/core；Controller.exe --ui-test temp/validation/game-controls/ui；Reader.exe --ui-test temp/validation/game-controls/packaged-ui；dotnet run --project temp/validation/game-controls/harness/Harness.csproj -c Release -p:PreviewDir=<打包目录> -- <仓库>。常规测试不写硬件。产物大小/哈希/完整变更清单见同目录package-check.json和changed-files.json。

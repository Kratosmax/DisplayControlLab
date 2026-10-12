# 标准识别、厂商候选与图像对比

2026-10-12，版本仍为 0.3.1，本地预览，未发布。

## 如何使用

已有检测记录打开后即可重新识别名称、用途来源和状态，不用为此重扫；“直接应用到控制器”会生成包含这些定义的包。若需要新增试写证据，展开“重新检测 / 修改检测选项”，启用图像验证后重新检测。保持显示器菜单和其他控制软件不动。

结果将用途与通信分开：公开标准、厂商候选、适配包定义、用途未知；三轮读取一致只表示读数稳定。可以在结果页直接展开某项“查看试调对比”，看到尝试编码、回读、差值和恢复情况，不要求导出给 AI。

## 数据规则与优先级

识别目录是 config/vcp-recognition.json（1.0.0），嵌入 Core，离线工作；当前含 80 条扩展标准码和 4 条品牌规则，原有 14 项标准控制目录保持不变。规则的厂商选择只看 EDID 厂商码，不维护每个型号的硬编码分支。

优先级：用户选定的型号/第三方协议定义 → 内置标准与品牌候选 → 用途未知。识别只补充程序生成的 vcp.xx 或内置标准项，不覆盖型号包专用映射、明确第三方只读限制、HDR 互锁或写入白名单。导出仍是原有 apiVersion=1，不要求控制器认识新的包格式；名称、规则和来源通过既有字段携带。

目录是源码中的事实数据，不是 adapter.json，也不是运行时热更新文件。追加品牌规则改这份数据即可，不必在执行代码中增加品牌/型号分支；更新程序后生效。若要用 AI 安装新私有协议，仍使用型号适配包入口，不让未知品牌规则静默执行 SET。

能力声明解析支持一字节控制码和两字节枚举取值，兼容 Dell 的 EA(FE00 FE01) 等声明，不再因为四位取值丢失整份声明；越界码/取值仍拒绝。生成包保留复合输入源的观察标签，并复用已有相同编码，避免重复取值导致导入失败。

## 标准码识别

名称根据公开 MCCS 实现核对，包括色温步进/请求、几何与采样调节、六轴饱和度/色相、RGB 黑电平、背光、锐度、Gamma、频率、方向、面板技术、使用时间和固件等。复杂编码仅显示状态，不伪造成连续滑块。例如：0x0C 是色温步数，必须结合 0x0B；0x72 是复合 Gamma，不猜 SET；0xAC 在实机中有不同频率编码，不直接把原值当成精确 Hz；0xAE 按标准显示百分之一 Hz，并注明标准解释。

新增数值图像项只有在显示器声明该项、MCCS 版本满足定义且多轮读数范围有效时才开放手动控制，验证等级仍 read-observed。未声明、版本未知或范围异常仍可识别名称，但保持只读。已观测的试写失败不会当作“尚未试写”放开。

## 品牌规则及证据

| 厂商 / 编码 | 处理 | 边界 |
|---|---|---|
| Dell / 0xE0 | 节能锁定候选，0/1 只读标签 | 部分系列公开记录；不保证每台 Dell 同义 |
| Dell / 0xE1 | 电源候选，0=开、1=关，只读 | 与标准电源码不同；不会发送电源试写 |
| Dell / 0x60 | 观察到 0x0Fxx 或 0x11xx 时解释已知输入端口 | 只解释复合回读；不将其直接作为 SET 值 |
| AOC / 0x14 | sRGB、暖色、正常、冷色、用户候选标签 | 来自共用 LCD 定义，不推测具体 K 值，型号包优先 |

MSI 继续使用标准目录及已安装的型号包。本次查询的 MSI 公开记录没有足以推广到品牌的私有码映射，不填猜测规则。

公开标准依据：https://github.com/rockowitz/ddcutil/blob/master/src/vcp/vcp_feature_codes.c 。本次成功下载源码作为查证资料；获取文件提交号的公共 API 遇到限流，未虚构精确提交号。原始文件与 SHA-256 保留在 temp/research/vcp-catalog/source-evidence.json。

厂商依据固定到 ddccontrol-db 提交 e684f17166d4e360fdbb356964fd62d0fb648e4f：db/monitor/DELA19A.xml、DEL41D9.xml、DEL4245.xml、U3223QE.xml、AOClcd.xml。资料同时给出反例：不同 Dell 系列的 0x14 编码 11 可以表示用户预设、暖色或其他含义，所以没有建立“一套 Dell 色温名称适用于所有型号”的规则。这里只整理协议事实与中文名称，未将第三方实现源码放进分发包。

## 对比验证与恢复

开启图像验证后，在系统 SDR、来源稳定、条件与显示器 HDR 互锁允许时，小幅试调标准亮度、对比度、RGB；已进入当前功能路由的标准数值图像项也可试调。新发现且尚未应用到路由的扩展数值项先识别/生成包，应用后再次检测可试调。不会自动试写未知私有码、输入源、电源、恢复出厂、Gamma 复合值或 HDR 预设。

标准色温只测试能力声明里的合法编码，最多 16 档，原档位必须在其中，原始 RGB 读数与标准通道必须完整。逐档保存原状态 → 写入 → 读取 RGB → 比较 → 恢复原档位；若恢复档位后 RGB 没有恢复，只在当前条件允许时恢复已知标准增益，随后核对完整基线。型号/第三方专用色温规则不改成宽泛试调规则。

记录了真实回读的档位才算通信验证。RGB 无变化、缺失或读取失败不推断名称；红/蓝归一化增益比改变超过 5% 可给“相对偏暖/偏冷线索”，这不是色度测量，也不能证明 6500K、sRGB 或画面视觉效果。实际厂商增益算法与隐藏色彩矩阵可能使这条线索失效。

取消后使用独立 90 秒上限核查恢复；设备/模式/来源改变、外部修改、RGB 以外的联动或恢复未确认时停止后续档位，保留恢复门禁并拒绝生成可控包。全部声明色温档位与恢复完成才标 write-confirmed，并携带本次确认编码白名单；失败保持未知/只读。

## 验证与回放

```powershell
dotnet build DisplayControlLab.sln -c Release -m:1 -p:NuGetAudit=false
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe . temp/validation/vcp-recognition/platform-final-model-priority
$validationProcess = Start-Process src/Reader/bin/Release/net8.0-windows/DisplayControl.Reader.exe -ArgumentList '--ui-test','temp/validation/vcp-recognition/reader-final-verified' -WindowStyle Hidden -PassThru
$validationProcess.WaitForExit(60000)
$validationProcess = Start-Process src/Controller/bin/Release/net8.0-windows/DisplayControl.Controller.exe -ArgumentList '--ui-test','temp/validation/vcp-recognition/controller-final-verified' -WindowStyle Hidden -PassThru
$validationProcess.WaitForExit(60000)
./build/package.ps1 -LiteOnly -PreviewOnly
# 已保存报告的纯离线回放，不安装输出包、不读写硬件；报告路径与输出目录由调用方明确传入。
& tests/Platform/bin/Release/net8.0-windows/DisplayControl.Platform.Tests.exe --replay-survey 原报告.json temp/validation/vcp-recognition/replay
```

核心 241 项通过，源构建读取器 UI 104 项、控制器 UI 102 项通过，构建 0 警告/0错误。覆盖标准版本/范围/声明门禁、厂商候选、型号优先（含生成包后再检测）、HDR保护、RGB小幅恢复、色温逐档原值恢复、无色值差异、相对冷暖、粘滞RGB恢复、失败、外部联动、SET后取消、恢复期间来源变更、两字节声明和候选标签导出。所有自动试写测试使用模拟后端与隔离资料。最终品牌标签优先级修正后的 UI 以包内同数量测试为最终验收，结果见 package-verification.json。

实际 Dell P2419H 最新报告只读回放：33 项公开标准、3 项厂商候选、11 项用途未知。原始记录和用户配置未改，也没有新实机 SET。0x6C/6E/70 没有被该屏能力声明，保持只读；不是识别到名称就保证可控。回放输出在 temp/validation/vcp-recognition/dell-replay，包已由真实 Packages.Parse 校验。

WPF 不能用 Playwright 驱动，使用真实离屏宿主、实际按钮事件及截图，覆盖小窗口与100/125/150/200%渲染。结果表按内容自动行高，已人工确认最小窗口下长状态不截断。展开色温证据第一次离屏截图被原父容器裁剪为空白；验收宿主改为将同一实际控件移到独立主题容器，加入非空像素检查，最终展开截图已人工查看，不声称空白图验证通过。实际固件的档位联动与恢复、跨屏 DPI 和其他 Windows 版本未实机验证。本地预览的最终 UI 数量、大小、哈希和包内验证记录在 temp/validation/vcp-recognition/package-verification.json；正式版本、Git 与发布状态不变。

最终包内入口再次验证：读取器104项、控制器102项UI通过，证据package-reader/ui-tests.json、package-controller/ui-tests.json。Lite ZIP为temp/preview/DisplayControlLab-0.3.1-Lite.zip，966049字节，SHA256 64e50e33d318071bd71f5574fdcc4d090365ad9869eb2190a19516e096f922cb；五个程序集与已验收展开目录一致，Core也与运行241项测试的二进制一致。需要.NET8 Desktop Runtime x64。本轮未提交、推送、发布，正式0.3.1资产保持。

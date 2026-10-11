# 主屏与副屏适配研究

2026-10-11。主屏指 AOC Q27G4ZMN / AOC B411；副屏指 MSI G273Q / MSI 3CA8。两台均为当前 DisplayPort / AMD 显卡连接。本轮全程后台，只读查询，没有 SET、系统 HDR 切换、厂商软件启动或焦点操作。

## 通用接口的实际边界

| 功能 | 标准接口 | 当前结论 |
|---|---|---|
| 亮度、对比度 | 0x10 / 0x12 | 两台均能读取，基础控制已内置，无须每个型号复制一个包 |
| 色温 / RGB | 0x14 / 0x16 / 0x18 / 0x1A | 两台能读取。标准编码不等于厂商菜单标签，色温还可能锁定 RGB |
| 画面 / 游戏预设 | 0xDC | 两台能读，含义不同。AOC 厂商枚举与 MCCS 通用枚举不同，不能跨型号套标签 |
| 伽马 | 0x72 | 标准存在，但有通道/调整方式编码，不能直接当普通0–100滑块。MSI两轮失败；AOC读到2/251，不把它当作私有Gamma1/2/3 |
| 图像比例、锐度 | 0x86 / 0x87 | 主屏比例两轮2/20成功；副屏比例、两台锐度均未读到。失败只说明此接口/状态没有有效读数 |

## 当前交付包

主屏完整包examples/aoc-q27g4zmn/adapter.json（1.3.0）已合并11项私有功能与3项标准补充定义，副屏包examples/msi-g273q/adapter.json（1.1.0）独立。每台只需一个型号包，功能级backend选择标准/私有通道。完整格式与最新验证见[UNIFIED_ADAPTER_VALIDATION.md](UNIFIED_ADAPTER_VALIDATION.md)。

首次应用自动采用包声明的色温定义，不要求重复手选来源；已有明确选择保留。游戏模式/图像比例仍只读。副屏启动批读曾有色温瞬时失败，原截图保留；已验证标准项有界GET重试后real-ui-final正常。

本轮没有实机SET。`writable=true`是标准协议控制声明，`verification=read-observed`只表示读取证据，不认证实际写入、画面效果和所有选项均支持。副屏5曾读取成功，用户档11按MCCS定义提供但未做副屏SET验证；主屏新增只读选项来自厂商定义，不表示每一档已实测。

## 实机只读结果

副屏两轮：亮度75/100、对比度70/100、色温5/11、应用预设2/11、音量62/100、静音2/2、OSD2/2、语言13/255、电源1/4；标准伽马/锐度/比例未读到。常规扫描还读取RGB50/100、输入源15/3、MCCS2.1。显示器没有返回可用能力字符串，不能用maximum字段推导枚举选项全集。

主屏两轮：游戏模式0/1536、比例2/20；读取到标准Gamma2/251，这不是实际2.51或Gamma2模式的证明，未添加误导控件。新的游戏模式0按厂商定义显示“标准/自定义”，与用户截图“标准”相容；未通过本轮OSD切换逐值确认，所以不开放写入。

证据：`temp/validation/monitor-adapters/devices.json`、`msi-ddc.json`、`standard-probes.json`、`additional-probes.json`、`routed-results.json`、`real-ui`。有设备身份的原始资料不随包分发。失败也保留，不用上一次成功值冒充本次读取。

## 主屏三张菜单照片对照

| 照片中的功能 | 当前适配 |
|---|---|
| 亮度、对比度、色温/RGB | 标准控制；补充包采用AOC色温文字。HDR开/未知时维持互锁 |
| 游戏模式、图像比例 | 本轮发现可用标准读取路径，新增只读控件 |
| 暗场、游戏色调、Overdrive | 原私有包已有受限可写值，未扩大其写入范围 |
| 伽马、低蓝光、HDR Mode | 原私有包已接入；标准Gamma与私有Gamma不混淆 |
| 低输入延迟、DCR、准星、帧计数 | 原包可读，写入未验证，保持只读；不是没有协议或设备不支持 |
| 区域调光、色彩空间、Adaptive-Sync | 已知E2A0候选GET各两轮返回 `6E880201E201FFFF000056`，result=1错误，仍待型号可用协议，禁止猜SET |
| 分辨率、刷新率、输入源 | Windows元数据/标准输入源状态可读，不擅自切换分辨率/信号源 |

故而“未开放写入”“没有有效读取协议”“标准GET暂时失败”是不同情况。未验证项不会靠改验证字段变成支持。区域调光等菜单存在，不代表当前候选接口可用。

## 资料依据

- 已安装 G-Menu 静态定义：`Zeasn.Equipment.Entity.Lib.cs` 中 StandardGameModes_E、StandardDisplayScaling_E、StandardAOCColorTemplate_E；`Zeasn.Equipment.Option.Lib.cs` 确认E2A0字节和子码计算。源码仅用于研究，不分发厂商DLL。
- [ddcutil标准VCP定义](https://github.com/rockowitz/ddcutil/blob/master/src/vcp/vcp_feature_codes.c)：0x72伽马编码、0xDC通用应用预设。
- [ddccontrol设备库](https://github.com/ddccontrol/ddccontrol-db/tree/master/db/monitor)：本次目录未找到MSI3CA8；MSI3FA8是另一台OLED型号，未套用。
- [msigd](https://github.com/couriersud/msigd)：支持表未找到G273Q，不向它套其他型号USB报文。
- MSI官网候选型号页面遇403/404，未取得精确型号的官方协议/手册。没有把G273QF等近似名字的资料当作本机证明。

下载记录在 `temp/research/monitor-adapters/sources.json`、`source-followup.json`、`additional-sources.json`；未成功的下载也有记录。

## 复核与交付

`dotnet build DisplayControlLab.sln -c Release -m:1` 零警告错误；Platform 132项通过，含精确型号匹配、包定义/标签/只读/色温限制；证据 `temp/validation/monitor-adapters/core/platform-tests.txt`。以隔离DISPLAYCONTROL_DATA安装/绑定新包并通过真实worker两轮读取，结果在routed-results.json。离屏WPF截图核对两个实机页面，无Show/鼠标/键盘；原生WPF不使用Playwright。未改UI业务代码，无理由重跑上一轮全部49项UI模拟测试；本轮以包路由和真实页面渲染验证。

本地Lite和两个独立适配ZIP在temp/preview，包内目录与大小/SHA256见 `temp/validation/monitor-adapters/package-check.json`。未提交推送发布，没有改用户现有资料或已安装包。当前主屏稳定版本保留。用户本轮未提供自行运行测试命令。

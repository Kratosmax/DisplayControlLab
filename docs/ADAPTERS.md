# 适配包开发与分享

本地预览格式，接口 apiVersion=1。宿主拒绝未知 JSON 字段；新增字段不能假设旧宿主会接受。正式格式升级将在发布前确定，当前主程序版本未变。

## 包与生命周期

包清单 adapter.json 位于 ZIP 根目录。声明 id、name、author、version、kind=protocol/plugin、match（EDID manufacturer/productCode，name 可空）、dependencies（外部依赖说明）、controls、可选 menuHints。id 小写字母开头，允许数字/点/短横线。

协议 JSON 可独立安装；插件 ZIP 需要 exe 及允许分发的依赖。安装不运行，应用到设备才参与路由。同一设备可组合多个包；同功能冲突需用户选择来源。标准目录内置，不要为标准亮度制造几十个冗余型号包。

升级有清单预览、暂存与旧目录回退；失败保留原版本。包文件操作与执行有跨进程锁。用户副本得到新 id，导出完整 ZIP；停用/卸载不删资料。旧 state.json 的 bindings 字段只作为兼容读入，新配置用 devicePackages/featureRoutes。

代码插件信任关联完整包内容 SHA256。内容变化旧信任失效，重绑需确认。dependencies 不是自动安装脚本；AMD DLL 做缺失提示，其他驱动由插件 probe/实际调用提供诊断，不把说明文字当依赖已满足。

## 功能定义

每项：id、name、group、可选 menuPath/unit/description（直接显示的适配限制）、helpText（可折叠的用途和使用建议）、valueType、readable（默认 true）、writable、verification（declared/read-observed/write-confirmed，为作者声明）。

控制器与读取器在功能名称旁提供“? 说明”（紧凑视图为“?”），默认折叠，悬停可预览，点击展开或收起；本次会话内重新渲染保留展开状态。标准和常见型号功能由宿主提供通俗解释，适配包的非空helpText优先，可在工作台编辑。未知功能没有自带说明时提示联系包作者，不根据名称猜作用。安全互锁和错误不随帮助折叠；静态description在详细视图直接显示，紧凑视图收进“⋯”适配信息。

- number：minimum、step，动态 maximum 来自读取。
- enum / boolean：rules 定义选项。设备编码允许大于响应 maximum。
- status：只读数值；text：文本状态或只写文本；action：无返回值动作。
- 文本、动作、只写能力通过代码插件；声明式解释器只实现有回读的已知 DDC 帧。

rules 每项含 maximum、可选 windowsHdr、values（稳定 id → 设备编码）、labels（稳定 id → 显示名）。例如 values.game=3 / labels.game=游戏；另一个系统 HDR 条件可以 game=4。方案保存 game，不保存中文或偶然的 3。旧包没有 labels 时使用原键名。条件重复/矛盾/未知拒绝操作。

allowedWriteValues 限制设备编码白名单；writeWhenWindowsHdr 限制系统条件。读到选项不代表都已验证可写。visibleWhen/readableWhen/writableWhen 是条件列表：featureId、equalsValue（依赖项原始值）、reason。条件未知保留明确原因；包内依赖/写顺序图拒绝环路，跨包递归有深度/循环检查。

affectedFeatures 描述可能联动范围，writeAfter 描述前置顺序。操作后重新读取，不自动补写联动项。menuHints 仅声明已知菜单 id/name/group/menuPath及可选description/helpText，没有指令，不变成可写控件。厂商功能可用命名空间，例如 vendor.feature。

## 声明式通信

包根backend是默认通道，功能可用backend覆盖它。有效通道为standard-ddc时填vcpCode数字，为amd-ddc时填raw：readPayload、writePrefix、replyLength、replyPrefix、currentOffset、maximumOffset、checksumSeed。固定 DDC/CI 帧 6E 51 长度 payload XOR；响应字段两字节大端。长度/前缀/状态/XOR 全部通过才接受。其他帧、握手、校验或专用 SDK 使用代码插件。

AOC 示例只匹配 AOC/0xB411/Q27G4ZMN，依赖 Windows x64 AMD ADL。11 项私有读取及已测试写入范围见 CAPABILITY_VALIDATION.md；五项菜单线索仍未适配。MSI 标准测试包在 tests/fixtures，不分发。

## 进程接口

stdin 一个 JSON 请求直到 EOF，stdout 一个 JSON 结果；诊断用 stderr。字段 apiVersion、requestId、action、device、packageId、controlId、value；宿主还可能携带 expectedHash、draftPackage、dependencyTrail，插件应忽略不需要的可选请求字段。

read/write 用于读/设；action 类型用 write 执行。probe/describe 可返回运行依赖诊断、text 和可选 capabilities（ControlDefinition 列表）。描述经过格式校验，但不自动覆盖已安装声明；当前控件路由使用用户安装的清单。无返回值动作不要求 current/maximum。

结果：apiVersion、requestId、status（ok/error/mismatch/unsupported/unknown）、message；可选 current/maximum/before/text/capabilities、sent、confirmed、raw。数字/枚举/布尔读取必须包含 current/maximum；文本可以只有 text；只写/动作可以只含状态。confirmed 仅在实际回读后设 true；无回读写入显示已发送，不能假装已确认。宿主不能替插件证明屏幕效果。

进程输出上限 1 MB；插件限时 15 秒，外层工作进程默认 20 秒；超时/取消终止进程树。每次请求独立进程，需要持久资料时自行使用用户数据目录。独立进程不是权限沙盒，不能擅自分发厂商受限 DLL。

## 作者操作

工作台有表单、条件映射和高级 JSON；自动保存草稿。离线回放验证解析与规则；主动读取无需先安装协议草稿。写入另行确认目标与原值，成功后可选择恢复，原始测试报告保存 protocol-tests（含草稿内容哈希、设备、请求与结果）。视觉效果仍需人工确认；不自动提升 verification。

源码模板在 examples/plugin-template，包含数字/文本/动作/只写示例。插件编译后导入完整 ZIP 并信任，再在控制页进行真实驱动验证。未知私有协议仍需厂商资料或受控研究，宿主不自动生成报文。

旧 v1 包未填写 valueType 且存在命名 rules 时，宿主按 enum 兼容解析；没有命名规则的旧数值项仍为 number。不会改写已安装文件，也不会因此放开以裸数字写模式。预检错误显示功能名称与原始原因。

## 定向读取与标准文本（本轮预览）

普通写入复用已读状态预检，工作进程仍核对设备身份、内容哈希、条件、原值和回读。成功后只补读 affectedFeatures，不做全量扫描。缺少该字段的旧 hdr-preset 包会核查色温与 RGB 四项；其他旧包不会凭空推测联动，作者应补齐受影响功能。

stabilizationMs 可设置写后等待 0–5000ms；0 使用后端默认（标准120ms、私有800ms）。回读不一致时额外只读重试，不重复 SET。matchAnyMaximum=true 用于不按响应 maximum 区分的离散选项；硬件不保证支持标准定义里的每个选项，拒绝时明确显示未确认。

内置色温/输入/电源/静音/OSD/语言提供标准文字，型号包可按实测规则覆盖。未知值仍标明编码，禁止猜含义。系统 HDR 为宿主 Windows 专用操作，不混入显示器私有 HDR 预设协议。两者状态分别保存，外部系统模式变化会重新加载设备范围/选项。

控制器管理安装、绑定、启停、卸载；读取器创建、编辑副本、测试、导出和插件模板。共享资料和上下文导航，不要求重复选设备。

## 显示器HDR安全规则

包根级可声明hdrSafety：modeFeatureId（默认hdr-preset）、offValue（默认0）、blockedFeatures（禁止写入的功能id列表）、restoreColorOnExit（默认true）。多个包规则合并，模式/关闭值冲突停止写入。它判断显示器预设，不用系统HDR替代；未知时拒绝。标准色温/对比度等VCP别名也按标准功能检查。

AOC/B411旧包也有宿主保护底线，不能靠旧包缺字段避开；其他型号由包声明。色温恢复使用color-preset标准语义，user选项保存RGB。宿主在开启前采集可恢复的原值，关闭回读确认后恢复；无基线或来源变化不写。两工具、CLI和代码插件宿主入口共用检查，独立插件自身执行任意代码不受权限沙盒限制。包作者须实测各型号锁定范围，不把AOC规则复制为全品牌事实。

## 一屏一包与功能级通道

同一个协议包可同时包含标准VCP与私有报文。controls[].backend可取standard-ddc/amd-ddc；省略时继承包默认backend。校验和执行都使用同一BackendFor解析，不允许协议结构与实际通道不符。未知通道明确拒绝，当前没有NVIDIA/Intel私有实现，不能把格式扩展当作已有支持。

根overridesStandard列出首次应用时默认采用型号定义的标准功能id；只允许相同标准VCP、standard-ddc协议项，不覆盖用户已明确选择的来源，多包竞争仍需明确选择。根包的依赖说明不能代表每个功能都依赖该通道。读取器工作台可逐功能选择通道；切换时清空不匹配报文字段并关闭写入，作者填完再重新声明写入。

主屏当前community.aoc-q27g4zmn/1.5.0含14项型号定义，不再分发标准补充包；副屏community.msi-g273q/1.1.0独立。1.5.0新增低输入延迟、准星及帧计数器关闭/右上/右下写入；左侧两档编码不一致，使用allowedWriteValues阻止发送。仅开放经回读验证的写入档位，并用description说明剩余限制。当前无旧补充包迁移逻辑。新包需要支持新字段的宿主。历史temp产物仅保留作证据，不作为当前安装说明。

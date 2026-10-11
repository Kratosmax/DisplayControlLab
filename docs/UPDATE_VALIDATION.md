# 更新与发行验证

0.3.1 参考 PinNote 的模块职责，使用独立产品公钥，包含签名公告、双层代理、两通道更新和外部事务替换。旧正式版0.2.0无自动更新器，提供一次性手动过渡，不声称旧客户端可自动升级。

## 验证范围

- 核心172项，WPF UI81项，更新专项每通道51项：签名/公告篡改、版本/通道/哈希、ZIP路径、规范化与优先级、直连/前缀/HTTP代理、失败回退、取消、真实压缩流、文件占用与回滚、启动恢复、旧软件清理和用户文件保留。
- 原生 WPF 离屏截图包含统一设置、公告滚动、确定/不确定进度、下载/检查失败、跳过、取消，以及1040宽与100/125/150/200%渲染。未 Show，不使用 Playwright、不注入输入；实际鼠标下拉与跨屏 DPI 未覆盖。
- 双通道签名包由控制器入口下载/预检，外部更新器从安装目录外等待已核实的安全子进程退出，替换后以CLI重启核实启动恢复，不打开GUI。前代基线是隔离目录的模拟旧安装身份，不能冒称上一正式客户端自动升级实测。
- 安装器在项目 temp 专用目录静默安装/卸载，不覆盖用户运行的软件，不启动硬件控制。
- 线上包必须独立下载，核对 Actions/Release 资产、签名、SHA256、通道、程序集及必需文件；本地与云端哈希不要求相同。

完整结果、命令及产物哈希在 temp/validation/updates。更新仅覆盖软件文件清单，保留用户适配包/方案/实验/记录。Full 更新器须在无外部运行库路径配置下完成交接；Lite依赖 .NET8 Desktop Runtime。有限测试不证明所有断电、驱动和系统版本均兼容。

## 重跑

README 包含构建、UI、核心、两通道和签名命令。tests/Updates 是无硬件专项；build/verify-updates.py 与 build/verify-installers.py 保存本次发行工具验收命令。含真实 SET 的旧研究脚本不得作为更新测试。标签发布由 release.yml 执行，私钥仅由受控环境注入，客户端只含公钥。

最终本地验证：Release构建零警告错误；核心172项、两通道更新专项各51项、打包读取器81项UI检查通过。两通道控制器入口已完成签名包下载预检、外部更新器等待/替换/CLI重启/清理，资料哨兵保留；Full设置无外部DOTNET_ROOT路径仍成功。两安装器静默安装、入口身份、卸载和注册表清理均通过。此前安装器只按注册表检测导致误报及静默弹窗，现增加实际.NET8文件核验并用SuppressibleMsgBox。真实旧版ZIP已下载检查，确无更新器。

云端v0.3.0候选四包及签名通过，完整交接脚本因未创建证据目录而失败；未创建Release、没有线上包。修正目录初始化后发行0.3.1，保留失败标签与Actions审计，不改写已存在标签。

## 线上0.3.1核验（2026-10-11）

发行代码提交f0c147c3b9daff388c83f7b06eb84aefb8a9615f，Actions [38139976095](https://github.com/Kratosmax/DisplayControlLab/actions/runs/38139976095)全部成功；[Release](https://github.com/Kratosmax/DisplayControlLab/releases/tag/v0.3.1)八个资产齐全，latest指向v0.3.1。独立下载后，八个文件大小/GitHub资产digest一致，七个受SHA256SUMS列出的文件哈希一致，update.json与update-lite.json相同。当前版本说明正文与发布源一致，无旧版本章节。

使用线上ZIP中的两个通道控制器实际执行--update-inspect、--update-check、--update-prepare：清单签名、产品/通道、入口程序集版本、实际ZIP预检均通过；真实latest清单获取及线上包下载/暂存/预检完成，未启动用户软件替换或GUI。Full仍使用无全局DOTNET_ROOT路径验证。资料通过DISPLAYCONTROL_DATA隔离，不改用户适配包和配置，不写硬件。较慢直连由隔离验收中的用户既有PinNote前缀线路替代，所有线路仍执行相同签名/哈希检查。

线上Full Setup 51285432字节，Full ZIP 72605533字节，Lite Setup 2396777字节，Lite ZIP 571094字节。Lite ZIP SHA256：084e201052dd047e4f1be8ea9b6c64987751c1d77a64bc2cf2559e3b54a0f9a8；全部校验和见Release的SHA256SUMS.txt。本地/云端独立构建的哈希不同属预期，以各自清单一致性为准。落盘证据为temp/validation/updates/online/{verification,release-api}.json，网络与发布复核命令在temp/release-work/verify-online.py。

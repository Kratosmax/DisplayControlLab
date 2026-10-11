# 代码插件模板

这里只模拟响应，**不连接真实硬件**。包含数字、只读文本、无回读动作与只写文本，并支持 probe/describe 诊断。把 Program.cs 对应分支替换为有文档的 SDK/驱动调用；在每次请求中重新核对设备与端点，别依赖旧索引。

```powershell
dotnet publish DisplayControl.SamplePlugin.csproj -c Release --self-contained false -o temp/plugin
Copy-Item adapter.json temp/plugin/adapter.json
Compress-Archive temp/plugin/* temp/my-plugin.zip
```

修改清单 EDID 匹配、功能、依赖后，在任一工具适配包页导入完整 ZIP，再明确同意信任当前内容并应用到匹配设备。程序改动需重新编译；用户副本保留完整代码。x86 驱动可发布 win-x86 插件并安装相应运行库，不要求与 x64 宿主同位数。

接口 v1：stdin 一个 JSON 请求直到 EOF；stdout 一个 JSON 结果。读取/写入用 read/write，无返回值动作用 write；probe/describe 返回依赖诊断。数字结果包含 current/maximum，文本可只含 text，动作/只写不要求读数。confirmed 只有实际回读确认后才能设 true，本示例的确认仅为模拟。

请求可能增加可选元数据，忽略不需要的请求字段；返回结构必须符合宿主契约。详见随包 docs/ADAPTERS.md 或仓库同名文档。每次调用独立进程，stdout/stderr 各有 1 MB 上限，插件调用限时 15 秒，取消后进程树终止。

真实 SDK 需要跨调用状态时使用用户资料目录，不能依赖示例中的局部变量。进程隔离不构成权限沙盒；只分发有权分发的 DLL。

using System.IO.Compression;
using System.Text.Json;
using MonitorInspector;

Console.InputEncoding = Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length == 2 && args[0] == "--ipc-forward") { using var navigation = new LocalNavigation(args[1], _ => { }); if (navigation.IsOwner) return 4; navigation.Forward("22222222222222222222222222222222"); return 0; }

if (Console.IsInputRedirected && args.Length == 0)
{
    var r = JsonSerializer.Deserialize<ControlRequest>(Console.In.ReadToEnd(), Packages.Json)!;
    if (r.ControlId == "timeout") { Thread.Sleep(10000); return 0; }
    if (r.ControlId == "exit") return 3;
    if (r.ControlId == "oversize") { Console.Write(new string('x', 1024 * 1024 + 100)); return 0; }
    if (r.ControlId is "action" or "text" or "write-only") { Console.Write(JsonSerializer.Serialize(new ControlResult { RequestId = r.RequestId, Status = "ok", Sent = r.Action == "write", Text = r.ControlId == "text" ? "结构状态文本" : null }, Packages.Json)); return 0; }
    Console.Write(JsonSerializer.Serialize(new ControlResult { RequestId = r.ControlId == "wrong-id" ? "wrong" : r.RequestId, Status = "ok", Current = 42, Maximum = 100, Message = "test" }, Packages.Json)); return 0;
}
if (args.Length != 2) throw new ArgumentException("使用：测试程序 项目根目录 证据目录");
string root = Path.GetFullPath(args[0]), evidence = Path.GetFullPath(args[1]);
string evidenceRoot = evidence;
evidence = Path.Combine(evidence, "run-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(evidence);
Environment.SetEnvironmentVariable("DISPLAYCONTROL_DATA", Path.Combine(evidence, "data-" + Guid.NewGuid().ToString("N")));
var passed = new List<string>();
void Check(bool result, string name) { if (!result) throw new Exception(name); passed.Add("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch { passed.Add("PASS " + name); return; } throw new Exception("未拒绝：" + name); }
var package = Packages.Parse(File.ReadAllText(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json")));
var control = package.Controls.Single(x => x.Id == "hdr-preset"); var raw = control.Raw!;
Check(package.Controls.Count == 14 && package.Controls.Where(x => x.Raw != null && x.Verification == "read-observed").All(x => !x.Writable) && package.Controls.Where(x => x.Id is "game-mode" or "image-ratio").All(x => !x.Writable), "私有只读观察项与新增模式比例不自动开放写入");
foreach (var id in new[] { "low-input-lag", "crosshair", "frame-counter" }) {
    var feature = package.Controls.Single(c => c.Id == id);
    Check(feature.Writable && feature.Verification == "write-confirmed" && feature.Raw!.WritePrefix == feature.Raw.ReadPayload.Replace("01 ", "03 "), id + "使用已核实的私有写入报文");
    Check(package.HdrSafety!.BlockedFeatures.Contains(id), id + "显示器HDR开启或未知时仍受互锁保护");
    Reject(() => ProtocolCodec.Value(feature, "off", feature.Rules[0].Maximum, true), id + "拒绝沿用SDR验证向系统HDR写入");
}
var frameCounter = package.Controls.Single(c => c.Id == "frame-counter");
Check(ProtocolCodec.Value(frameCounter, "bottom-right", 4, false) == 2, "帧计数器右下写入已开放");
Reject(() => ProtocolCodec.Value(frameCounter, "bottom-left", 4, false), "帧计数器拒绝写入回读不一致的左下编码");
Reject(() => ProtocolCodec.Value(frameCounter, "top-left", 4, false), "帧计数器拒绝写入回读不一致的左上编码");
var gamma = package.Controls.Single(x => x.Id == "gamma");
Check(gamma.Writable && ProtocolCodec.Value(gamma,"2.0",2,false)==1 && ProtocolCodec.Value(gamma,"2.4",2,false)==2, "伽马使用厂商数字标签，开放实机确认的三档");
Reject(() => ProtocolCodec.Value(gamma,"1.8",2,false), "拒绝实机未确认的伽马档位");
var restrictedGamma = Library.Copy(gamma); restrictedGamma.AllowedWriteValues = [0,1];
Reject(() => ProtocolCodec.Value(restrictedGamma,"2.4",2,false), "第三方包的写入白名单仍有效");
Reject(() => ProtocolCodec.Value(gamma,"2.0",2,true), "未经实测的 HDR 上下文不沿用 SDR 写入验证");
foreach (var id in new[] { "game-color", "shadow-control" }) {
    var numeric = package.Controls.Single(c => c.Id == id);
    Check(ProtocolCodec.Value(numeric,"20",20,false)==20, id + "允许设备报告范围的上边界");
    Reject(() => ProtocolCodec.Value(numeric,"21",20,false), id + "拒绝超过设备实时范围");
}
foreach (var id in new[] { "eco-preset", "dynamic-contrast" }) {
    var writableFeature = package.Controls.Single(c => c.Id == id);
    Check(writableFeature.Writable && writableFeature.Raw!.WritePrefix.StartsWith("03 F8 E1") && package.HdrSafety!.BlockedFeatures.Contains(id), id + "已验证写入且保留显示器HDR互锁");
    Reject(() => ProtocolCodec.Value(writableFeature,writableFeature.Rules[0].Values.Keys.First(),7,true), id + "拒绝系统HDR条件写入");
}
Check(package.MenuHints.Single(h => h.Id == "local-dimming").Description.Contains("返回错误") && package.MenuHints.All(h => h.Id != "aspect-ratio"), "菜单说明保留协议缺口，移除重复比例占位");
Check(DeviceControl.Standard.Count == 14 && DeviceControl.Standard.Single(x => x.Id == "input-source").ValueType == "enum" && !DeviceControl.Standard.Single(x => x.Id == "input-source").Writable, "标准目录跨设备内置，输入源不当数字滑块写入");
Check(Convert.ToHexString(ProtocolCodec.Packet(Packages.Hex(raw.ReadPayload))) == "6E518401F8E152F1", "GET 报文与实机证据一致");
Check(Convert.ToHexString(ProtocolCodec.Packet([.. Packages.Hex(raw.WritePrefix), 0, 4])) == "6E518603F8E1520004F5", "游戏 SET 报文与实机证据一致");
Check(ProtocolCodec.Parse(raw, Convert.FromHexString("6E880200F852000300001D")) == (0u,3u), "解析范围 3 的实机响应");
Check(ProtocolCodec.Parse(raw, Convert.FromHexString("6E880200F852000400041E")) == (4u,4u), "解析范围 4 的实机响应");
Reject(() => ProtocolCodec.Parse(raw, Convert.FromHexString("6E880200F852000400041F")), "错误 checksum");
Reject(() => ProtocolCodec.Parse(raw, [1,2,3]), "截断响应");
Reject(() => ProtocolCodec.Parse(raw, Convert.FromHexString("6E880200F851000400041D")), "其他子命令");
Check(ProtocolCodec.Value(control,"游戏",3,false)==3 && ProtocolCodec.Value(control,"游戏",4,true)==4, "系统 HDR 与最大值联合选取游戏值");
Reject(() => ProtocolCodec.Value(control,"游戏",3,true), "矛盾系统状态拒绝写入");
Reject(() => ProtocolCodec.Value(control,"游戏",4,null), "未知系统状态拒绝私有写入");
Reject(() => ProtocolCodec.Value(control,"3",4,true), "拒绝跨模式直接写数字");
Reject(() => ProtocolCodec.Value(DeviceControl.Standard[0],"101",100,false), "标准值超界");
Check(ProtocolCodec.Value(DeviceControl.Standard[0],"45",100,false)==45, "标准数字值校验");
string text = JsonSerializer.Serialize(package, Packages.Json);
Reject(() => Packages.Parse(text.Replace("\"apiVersion\": 1", "\"apiVersion\": 9")), "接口版本不符");
Reject(() => Packages.Parse(text.Replace("\"kind\": \"protocol\"", "\"kind\": \"script\"")), "未知包类型");
Reject(() => Packages.Parse(text[..^3]), "损坏 JSON");
Reject(() => Packages.Parse(text.Replace("\"apiVersion\": 1", "\"apiVersion\": 1, \"apiVersion\": 1")), "重复 JSON 字段");
Reject(() => Packages.Parse(text.Replace("\"currentOffset\": 8", "\"currentOffset\": 6")), "重叠响应字段");
Reject(() => Packages.Parse(text.Replace("\"currentOffset\": 8", "\"currentOffset\": 10")), "响应字段越界");
Reject(() => Packages.ContainedPath(evidence,"../escape"), "路径穿越");
Reject(() => Packages.ContainedPath(evidence,"C:/escape"), "绝对路径");
Reject(() => Packages.ContainedPath(evidence,"file.exe:stream"), "NTFS ADS");
var installed = Packages.Install(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json"));
Check(Packages.List().Count==1, "协议包按需安装");
Reject(() => Packages.Install(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json")), "重复 id 不自动覆盖");
var monitor = new MonitorData { DevicePath="test-connection",Name="Q27G4ZMN",Edid=new() {Manufacturer="AOC",Product="0xB411"} };
Check(Packages.ForDevice(monitor).Count==0, "安装不自动绑定执行");
Packages.Bind(monitor, installed.Id); Check(Packages.ForDevice(monitor).SingleOrDefault()?.Id==installed.Id,"用户手动绑定");
Packages.Enable(installed.Id,false); Check(Packages.ForDevice(monitor).Count==0,"停用包拒绝路由");
Packages.Enable(installed.Id,true); Check(Packages.ForDevice(monitor).Count>0,"重新启用");
monitor.Edid.Product="0xFFFF"; Check(Packages.ForDevice(monitor).Count==0,"错误设备不使用绑定");monitor.Edid.Product="0xB411";
string zip=Path.Combine(evidence,"adapter-export.zip"); Packages.Export(installed,zip);
using (var archive=ZipFile.OpenRead(zip)) Check(archive.GetEntry("adapter.json")!=null,"完整适配包导出");
string badZip=Path.Combine(evidence,"bad.zip");
using (var archive=ZipFile.Open(badZip,ZipArchiveMode.Create)) { using var w=new StreamWriter(archive.CreateEntry("../escape.txt").Open()); w.Write("bad"); }
Reject(()=>Packages.Install(badZip),"ZIP 逃逸拒绝安装");Check(Packages.List().Count==1,"坏包安装不破坏已有包");
Packages.Uninstall(installed);Check(Packages.List().Count==0 && Packages.State().Bindings.Count==0,"卸载清理绑定，记录不删除");
var second = Packages.Parse(File.ReadAllText(Path.Combine(root, "tests/fixtures/adapters/msi-g273q.json")));
Check(second.Match.Matches(new() { Name = "MSI G273Q", Edid = new() { Manufacturer = "MSI", Product = "0x3CA8" } }) && !second.Match.Matches(monitor), "第二品牌同格式匹配，不复用 AOC 身份");
Check(second.Controls.Single(x => x.Id == "input-source").ValueType == "enum", "枚举语义允许输入码不等于数值上界");
var inputRule = new ControlDefinition { Id = "input-source", Name = "输入源", ValueType = "enum", Rules = [new() { Maximum = 3, Values = new() { ["DisplayPort"] = 15 } }] };
Check(ProtocolCodec.Value(inputRule, "DisplayPort", 3, false) == 15, "枚举原始 maximum 不错误限制设备输入编码");
var aocInstalled = Packages.Install(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json"));
var msiInstalled = Packages.Install(Path.Combine(root, "tests/fixtures/adapters/msi-g273q.json"));
var msiDevice = new MonitorData { DevicePath = "msi-test-connection", Name = "MSI G273Q", Edid = new() { Manufacturer = "MSI", Product = "0x3CA8" } };
Packages.Bind(monitor, aocInstalled.Id); Packages.Bind(msiDevice, msiInstalled.Id);
Check(Packages.ForDevice(monitor).SingleOrDefault()?.Id == aocInstalled.Id && Packages.ForDevice(msiDevice).SingleOrDefault()?.Id == msiInstalled.Id, "两种显示器独立安装与绑定，核心没有型号分支");
Packages.Uninstall(aocInstalled); Packages.Uninstall(msiInstalled);
foreach (string scenario in new[]{"ok","wrong-id","exit","timeout","oversize"})
{
    bool rejected = false;
    try
    {
        var result=await ProcessClient.Run(Environment.ProcessPath!,new() {ControlId=scenario},scenario=="timeout"?300:3000);
        if (scenario == "ok") Check(result.Current==42,"独立插件 JSON 通信");
    }
    catch when(scenario!="ok") { rejected = true; }
    if (scenario != "ok") Check(rejected, "插件异常："+scenario);
}
await ProductTests.Run(root, evidence, passed);
File.WriteAllText(Path.Combine(evidence,"platform-tests.txt"),string.Join('\n',passed)+$"\n{passed.Count} 项通过\n");
File.Copy(Path.Combine(evidence, "platform-tests.txt"), Path.Combine(evidenceRoot, "platform-tests.txt"), true);
File.WriteAllText(Path.Combine(evidenceRoot, "latest-run.txt"), evidence);
Console.WriteLine($"{passed.Count} 项通过");return 0;

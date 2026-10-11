using MonitorInspector;
using System.IO.Compression;
using System.Text.Json;

internal static class ProductTests
{
    public static async Task Run(string root, string evidence, List<string> passed)
    {
        void Check(bool test, string name) { if (!test) throw new Exception(name); passed.Add("PASS " + name); }
        void Reject(Action action, string name) { try { action(); } catch { passed.Add("PASS " + name); return; } throw new Exception("未拒绝 " + name); }
        var sim = new SimulatedDisplayBackend(); var workspace = new DisplayWorkspace(sim); var devices = await sim.Devices(default); var device = devices[0];
        var a = await workspace.Capture(device, default);
        var game = new Preset { Name = "游戏", DevicePath = device.DevicePath, Items = [new() { FeatureId = "hdr-preset", Value = "game", Provider = "simulation-aoc" }] };
        var operation = await workspace.Apply("游戏 SDR", a, DisplayWorkspace.Preflight(game, a), default);
        Check(operation.Results.Single().Confirmed && sim.Values["hdr-preset"] == 3, "语义 game 在 SDR 映射 3");
        sim.Hdr = true; a = await workspace.Capture(device, default); operation = await workspace.Apply("游戏 HDR", a, DisplayWorkspace.Preflight(game, a), default);
        Check(operation.Results.Single().Confirmed && sim.Values["hdr-preset"] == 4, "同一语义方案在 Windows HDR 下映射 4");
        var stale = a; sim.Hdr = false; var staleWrite = DisplayWorkspace.Request(stale.Device, stale.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Capability, "write", "game");
        Check((await sim.Execute(staleWrite, default)).Status == "error", "执行前系统条件变化拒绝旧待写值");
        sim.Values["hdr-preset"] = 0; a = await workspace.Capture(device, default);
        var bright = new Preset { DevicePath = device.DevicePath, Items = [new() { FeatureId = "brightness", Value = "75" }] };
        operation = await workspace.Apply("亮度", a, DisplayWorkspace.Preflight(bright, a), default); var now = operation.After;
        Check(Library.Get<Operation>("operations", operation.Id).Results[0].Confirmed, "操作意图/结果/快照持久化可重开");
        var restore = DisplayWorkspace.Recovery(operation, now); Check(restore.Items.Single().Value == "85", "恢复保留实际原始值");
        sim.Values["brightness"] = 72; now = await workspace.Capture(device, default); Reject(() => DisplayWorkspace.Recovery(operation, now), "外部调整与恢复冲突，不覆盖");
        var fail = new FaultBackend(sim); var controlled = new DisplayWorkspace(fail); a = await controlled.Capture(device, default);
        var batch = new Preset { DevicePath = device.DevicePath, Items = [new() { FeatureId = "brightness", Value = "65" }, new() { FeatureId = "contrast", Value = "60" }, new() { FeatureId = "volume", Value = "42" }] };
        operation = await controlled.Apply("部分失败", a, DisplayWorkspace.Preflight(batch, a), default);
        Check(operation.Requests.Count == 2 && operation.Results[0].Confirmed && operation.Results[1].Status == "error" && !sim.Values.ContainsKey("volume"), "方案中途失败后停止，不伪装事务");
        restore = DisplayWorkspace.Recovery(operation, operation.After); Check(restore.Items.Count == 1 && restore.Items[0].FeatureId == "brightness", "部分失败只恢复已确认项");
        var missing = new Preset { DevicePath = device.DevicePath, Items = [new() { FeatureId = "missing", Value = "1" }] };
        Check(DisplayWorkspace.Preflight(missing, a).Single().Problem.Length > 0, "缺少必需项时预检失败"); missing.Items[0].Required = false; Check(DisplayWorkspace.Preflight(missing, a).Single().Problem.Length > 0, "可选缺失明确保留原因，不假成功");
        missing.Items.Add(missing.Items[0]); Reject(() => DisplayWorkspace.Preflight(missing, a), "重复方案功能拒绝");
        var b = Library.Copy(a); b.Readings[0].Result.Maximum = 200; Check(Comparison.Between(a, b).Count == 1, "值相同但范围变化也保留差异");
        b = Library.Copy(a); b.Readings[0].Result.Status = "error"; b.Readings[0].Result.Current = null; Check(Comparison.Between(a, b).Single().After.StartsWith("读取失败"), "读取失败不转零或旧值");
        var experiment = new Experiment { Device = device, Question = "A B A", Segments = [new() { Name = "A", Rounds = [a, Library.Copy(a)] }, new() { Name = "B", Rounds = [b, Library.Copy(b)] }] };
        Library.Save("experiments", experiment.Id, experiment); Check(Library.Get<Experiment>("experiments", experiment.Id).Compare(0,1).Count == 1, "实验落盘重开与失败样本比较");
        experiment.Segments[1].Transition = true; Reject(() => experiment.Compare(0,1), "过渡段排除稳定分析");
        string export = Path.Combine(evidence, "anonymous-experiment.zip"); if (File.Exists(export)) File.Delete(export); DisplayWorkspace.ExportExperiment(experiment, export, true);
        using (var zip = ZipFile.OpenRead(export)) { using var reader = new StreamReader(zip.GetEntry("experiment.json")!.Open()); var text = reader.ReadToEnd(); Check(!text.Contains("simulation-aoc\"") || !text.Contains("\"devicePath\": \"simulation-aoc\""), "证据分享去除设备路径"); }
        Check(DisplayWorkspace.Conditions([new() { FeatureId = "missing", EqualsValue = 1 }], a).StartsWith("条件未知"), "依赖状态未知不当成满足");
        Check(DisplayWorkspace.Conditions([new() { FeatureId = "brightness", EqualsValue = 1, Reason = "需切换模式" }], a) == "需切换模式", "条件不满足返回包提供的原因");
        sim.Offline = true; bool offline = false; try { await workspace.Capture(device, default); } catch (IOException) { offline = true; } Check(offline, "设备失联拒绝沿用原快照发命令"); sim.Offline = false;
        var package = Packages.Parse(File.ReadAllText(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json"))); var hw = new MonitorData { Name = "Q27G4ZMN", DevicePath = "route-test", Edid = new() { Manufacturer = "AOC", Product = "0xB411" } };
        var p1 = Packages.Install(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json")); Packages.Attach(hw, p1, false);
        var extra = new AdapterPackage { Id = "test.complement", Name = "互补包", Author = "test", Match = package.Match, Controls = [new() { Id = "vendor.extra", Name = "扩展功能", VcpCode = 0x62 }] };
        string source = Path.Combine(evidence, "complement.json"); File.WriteAllText(source, JsonSerializer.Serialize(extra, Packages.Json)); var p2 = Packages.Install(source); Packages.Attach(hw, p2, false);
        Check(CapabilityCatalog.Resolve(hw).Any(c => c.Definition.Id == "hdr-preset") && CapabilityCatalog.Resolve(hw).Any(c => c.Definition.Id == "vendor.extra"), "两个互补包共存，标准能力保留");
        extra.Id = "test.conflict"; extra.Name = "冲突包"; extra.Controls[0].Id = "brightness"; File.WriteAllText(source, JsonSerializer.Serialize(extra, Packages.Json)); var conflict = Packages.Install(source); Packages.Attach(hw, conflict, false);
        Check(CapabilityCatalog.Resolve(hw).Single(c => c.Definition.Id == "brightness").Problem.Contains("冲突"), "逐功能冲突不静默覆盖标准");
        Packages.Choose(hw, "brightness", "standard"); Check(CapabilityCatalog.Resolve(hw).Single(c => c.Definition.Id == "brightness").Problem.Length == 0, "选择标准来源后冲突解决");
        Packages.Choose(hw, "brightness", conflict.Id); Check(CapabilityCatalog.Resolve(hw).Single(c => c.Definition.Id == "brightness").Provider == conflict.Id, "逐功能选择包来源生效");
        string broken = Path.Combine(Packages.Home, "adapters", "test.broken"); Directory.CreateDirectory(broken); File.WriteAllText(Path.Combine(broken, "adapter.json"), "invalid"); Check(Packages.List().Count == 3 && Packages.Broken().Count == 1, "坏包隔离不阻断其他包和标准功能");
        var cycle = Library.Copy(package); cycle.Controls[0].WritableWhen = [new() { FeatureId = cycle.Controls[1].Id }]; cycle.Controls[1].WritableWhen = [new() { FeatureId = cycle.Controls[0].Id }]; Reject(() => Packages.Validate(cycle), "条件图环路拒绝");
        cycle = Library.Copy(package); cycle.Controls[0].WriteAfter = [cycle.Controls[1].Id]; cycle.Controls[1].WriteAfter = [cycle.Controls[0].Id]; Reject(() => Packages.Validate(cycle), "写入顺序图环路拒绝");
        var plugin = new AdapterPackage { Id = "test.trusted-plugin", Name = "信任测试", Author = "test", Kind = "plugin", EntryPoint = "plugin.exe", Match = package.Match, Controls = [new() { Id = "vendor.action", Name = "动作", ValueType = "action", Readable = false, Writable = true }] };
        string stage = Path.Combine(evidence, "plugin-trust"); Directory.CreateDirectory(stage); File.WriteAllText(Path.Combine(stage, "adapter.json"), JsonSerializer.Serialize(plugin, Packages.Json)); File.WriteAllText(Path.Combine(stage, "plugin.exe"), "fixture-not-executable"); string pluginZip = Path.Combine(evidence, "trust.zip"); if (File.Exists(pluginZip)) File.Delete(pluginZip); ZipFile.CreateFromDirectory(stage, pluginZip); plugin = Packages.Install(pluginZip);
        Reject(() => Packages.Attach(hw, plugin, false), "代码插件绑定需显式信任"); Packages.Attach(hw, plugin, true); Check(Packages.IsTrusted(plugin), "信任绑定完整包内容哈希"); File.AppendAllText(Path.Combine(plugin.DirectoryPath, "plugin.exe"), "changed"); Check(!Packages.IsTrusted(plugin), "插件代码改变使旧信任失效");
        Packages.Uninstall(p1); Packages.Uninstall(p2); Packages.Uninstall(conflict); Packages.Uninstall(plugin); Check(Packages.ForDevice(hw).Count == 0, "卸载清理多包绑定和逐功能路由");
        foreach (string type in new[] { "action", "text", "write-only" }) { var result = await ProcessClient.Run(Environment.ProcessPath!, new() { ControlId = type, Action = type == "text" ? "read" : "write" }); Check(result.Status == "ok" && result.Maximum == null, "插件 " + type + " 不要求 maximum"); }
        Check(System.Runtime.InteropServices.Marshal.SizeOf<Native.Path>() == 72 && System.Runtime.InteropServices.Marshal.SizeOf<Native.Mode>() == 64, "迁移保留 Win32 路径/模式结构尺寸验证");
        string channel = "test-" + Library.NewId(); var delivered = new TaskCompletionSource<string>();
        using (var owner = new LocalNavigation(channel, value => delivered.TrySetResult(value))) {
            Check(owner.IsOwner, "单实例通道取得所有权");
            var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true }; start.ArgumentList.Add("--ipc-forward"); start.ArgumentList.Add(channel);
            using var child = System.Diagnostics.Process.Start(start)!; await child.WaitForExitAsync();
            Check(child.ExitCode == 0 && await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)) == "22222222222222222222222222222222", "第二进程不取得所有权，导航转发原实例，无窗口激活");
            Reject(() => owner.Forward("C:/arbitrary.exe"), "IPC 不接受可执行路径或任意指令");
        }
        using (var newOwner = new LocalNavigation(channel, _ => { })) Check(newOwner.IsOwner, "实例退出释放所有权，无陈旧进程锁");
        string template = Path.Combine(root, "examples/plugin-template/bin/Release/net8.0-windows/DisplayControl.SamplePlugin.exe");
        foreach (var item in new[] { ("simulated-status", "read"), ("simulated-action", "write"), ("simulated-write-only", "write"), ("", "probe") }) {
            var result = await ProcessClient.Run(template, new() { ControlId = item.Item1, Action = item.Item2, Value = "notification" });
            Check(result.Status == "ok" && !result.Confirmed && result.Maximum == null, "随包源码模板实际进程：" + item.Item1 + "/" + item.Item2);
        }
        string invalidNull = File.ReadAllText(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json")).Replace("\"controls\": [", "\"controls\": null, \"unusedControls\": ["); Reject(() => Packages.Parse(invalidNull), "损坏的 null/未知结构拒绝导入");
        var swapped = Library.Copy(device); swapped.Edid.Serial = "another-unit"; Check(!HardwareReports.SameDevice(device, swapped), "同型号同连接但序列号变化视为不同设备");
        var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json")))!;
        foreach (var feature in legacyJson["controls"]!.AsArray()) feature!.AsObject().Remove("valueType");
        var legacy = Packages.Parse(legacyJson.ToJsonString());
        var legacyHdr = legacy.Controls.Single(f => f.Id == "hdr-preset");
        Check(legacyHdr.ValueType == "enum" && ProtocolCodec.Value(legacyHdr, "游戏", 3, false) == 3 && ProtocolCodec.Value(legacyHdr, "游戏", 4, true) == 4, "旧包缺少 valueType 的命名规则兼容枚举，SDR/HDR 语义仍正确");
        Check(legacy.Controls.Single(f => f.Id == "game-color").ValueType == "number", "没有命名规则的旧数值功能保持数字类型");
        Reject(() => ProtocolCodec.Value(legacyHdr, "4", 4, true), "旧包兼容不放开不明数字写入模式");
        var failedSnapshot = Library.Copy(a); failedSnapshot.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result = new() { Status = "error", Message = "VCP 读取失败" };
        var failurePlan = DisplayWorkspace.Preflight(bright, failedSnapshot);
        string? failureText = DisplayWorkspace.PreflightFailure(failurePlan);
        Check(failureText != null && failureText.Contains("亮度") && failureText.Contains("VCP 读取失败") && failureText.Contains("未发送"), "预检失败显示具体功能和原始失败原因");
        int beforeOperations = Library.All<Operation>("operations").Count;
        bool blocked = false; try { await workspace.Apply("拒绝发送", failedSnapshot, failurePlan, default); } catch (InvalidOperationException ex) { blocked = ex.Message == failureText; }
        Check(blocked && Library.All<Operation>("operations").Count == beforeOperations, "失败项详细诊断不绕过预检，也不创建写入操作");
        sim.Hdr = false; sim.Values["hdr-preset"] = 0; sim.Values["brightness"] = 100; var baseline = await workspace.Capture(device, default);
        var brightnessCapability = baseline.Readings.Single(r => r.Capability.Definition.Id == "brightness").Capability;
        var orderedIds = baseline.Readings.Select(r => r.Capability.Definition.Id).ToArray();
        var orderedUpdate = new Snapshot { Device = baseline.Device, Readings = [Library.Copy(baseline.Readings[0])] };
        orderedUpdate.Readings[0].Result.Current = 66;
        Check(DisplayWorkspace.Merge(baseline, orderedUpdate).Readings.Select(r => r.Capability.Definition.Id).SequenceEqual(orderedIds), "定向刷新原位替换，不把功能移到末尾");
        var orderedAfter = Library.Copy(baseline);
        DisplayWorkspace.UpdateFromWrite(baseline, orderedAfter, brightnessCapability, new() { Status = "ok", Current = 66, Maximum = 100 });
        Check(orderedAfter.Readings.Select(r => r.Capability.Definition.Id).SequenceEqual(orderedIds), "写入回读不改变功能顺序");
        sim.Requests.Clear(); operation = await workspace.SetFeature(baseline.Device, brightnessCapability, "30", baseline, default);
        Check(sim.Requests.Count == 2 && sim.Requests.Count(r => r.Action == "write") == 1 && sim.Requests.All(r => r.ControlId is "brightness" or "hdr-preset"), "正常亮度仅写目标并核查HDR互锁，不全量读取或重复回读");
        Check(operation.After.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result.Current == 30, "100→30 使用本次确认结果，不恢复缓存中的 100");
        sim.Requests.Clear(); var hdrCapability = baseline.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Capability;
        await workspace.SetFeature(baseline.Device, hdrCapability, "game", baseline, default);
        Check(sim.Requests.All(r => r.ControlId is "hdr-preset" or "color-preset" or "red-gain" or "green-gain") && sim.Requests.Count(r => r.Action == "write") == 1, "HDR 预设只写目标并读取声明联动项");
        var mismatchWorkspace = new DisplayWorkspace(new IgnoredWriteBackend(sim)); sim.Values["hdr-preset"] = 0; sim.Values["brightness"] = 100;
        baseline = await mismatchWorkspace.Capture(device, default); operation = await mismatchWorkspace.SetFeature(baseline.Device, brightnessCapability, "30", baseline, default);
        var mismatch = operation.After.Readings.Single(r => r.Capability.Definition.Id == "brightness");
        Check(mismatch.Result.Current == 100 && !mismatch.Result.Confirmed && mismatch.Display(false).Contains("未确认"), "显示器仍回报100时明确未确认，不伪装30已生效");
        var color = DeviceControl.Standard.Single(f => f.Id == "color-preset"); Check(color.Writable && ProtocolCodec.Value(color, "user", 11, false) == 11, "标准色温开放语义设置与 RGB 联动");
        Check(new Reading { Capability = new() { Definition = DeviceControl.Standard.Single(f => f.Id == "input-source") }, Result = new() { Status = "ok", Current = 15, Maximum = 3 } }.Display(false) == "DisplayPort 1", "离散输入源状态显示文本，非数字上限约束");
        Check(new Reading { Capability = new() { Definition = DeviceControl.Standard.Single(f => f.Id == "mccs-version") }, Result = new() { Status = "ok", Current = 514, Maximum = 255 } }.Display(false) == "MCCS 2.2", "MCCS 编码显示版本文字");
        byte[] hdrPacket = Native.HdrPacket(0x12345678, 9, 7, true);
        Check(hdrPacket.Length == 24 && BitConverter.ToUInt32(hdrPacket,0) == 16 && BitConverter.ToUInt32(hdrPacket,4) == 24 && BitConverter.ToUInt32(hdrPacket,16) == 7 && BitConverter.ToUInt32(hdrPacket,20) == 1, "HDR 专用设置结构与微软 SDK 类型/大小/目标/标志一致");
        sim.Hdr = false; var systemResult = await sim.Execute(new() { Device = device, ControlId = "windows-hdr", Action = "system-hdr", Value = "on" }, default);
        Check(systemResult.Confirmed && sim.Hdr, "系统 HDR 专用操作契约模拟切换成功");
        int attempts = 0; var delays = new List<int>();
        int recovered = DeviceControl.ReadWithRetry(() => { if (++attempts < 3) throw new System.ComponentModel.Win32Exception(31); return 94; }, delays.Add);
        Check(recovered == 94 && attempts == 3 && delays.SequenceEqual(new[] { 120, 250 }), "DDC GET短暂失败有界退避恢复");
        attempts = 0; Reject(() => DeviceControl.ReadWithRetry<int>(() => { attempts++; throw new System.ComponentModel.Win32Exception(31); }, _ => { }), "持续DDC失败不无限重试");
        Check(attempts == 3, "DDC重试最多三次GET");
        attempts = 0; Reject(() => DeviceControl.ReadWithRetry<int>(() => { attempts++; throw new InvalidDataException("校验失败"); }, _ => { }), "非传输错误不盲目重试");
        Check(attempts == 1, "身份/解析错误不进入GET重试");
        int sets = 0, checks = 0, nativeErrors = 0; uint actual = 98;
        var brightnessResult = DeviceControl.WriteBrightness(69, (98u,100u), value => { sets++; actual = value; throw new System.ComponentModel.Win32Exception(31); }, () => (actual,100u), () => checks++, _ => nativeErrors++, _ => { });
        Check(brightnessResult.Current == 69 && sets == 1 && nativeErrors == 1 && checks >= 2, "亮度SET报错但回读已生效时确认结果，不重复发送");
        sets = 0; actual = 98;
        brightnessResult = DeviceControl.WriteBrightness(69, (98u,100u), value => { if (++sets == 1) throw new System.ComponentModel.Win32Exception(31); actual = value; }, () => (actual,100u), () => { }, _ => { }, _ => { });
        Check(brightnessResult.Current == 69 && sets == 2, "亮度SET失败且回读原值未变时只补一次绝对值写入");
        sets = 0;
        brightnessResult = DeviceControl.WriteBrightness(69, (98u,100u), _ => { sets++; throw new System.ComponentModel.Win32Exception(31); }, () => (98u,100u), () => { }, _ => { }, _ => { });
        Check(sets == 2 && brightnessResult.Current == 98, "持续亮度写入失败有界结束，保留真实回读值");
        sets = 0;
        brightnessResult = DeviceControl.WriteBrightness(69, (98u,100u), _ => { sets++; throw new System.ComponentModel.Win32Exception(31); }, () => (50u,100u), () => { }, _ => { }, _ => { });
        Check(sets == 1 && brightnessResult.Current == 50, "亮度已被外部改动时不覆盖或补写");
        sets = 0;
        brightnessResult = DeviceControl.WriteBrightness(69, (98u,100u), _ => { sets++; throw new System.ComponentModel.Win32Exception(31); }, () => (98u,120u), () => { }, _ => { }, _ => { });
        Check(sets == 1 && brightnessResult.Maximum == 120, "亮度范围发生变化时不补写");
        sets = 0;
        Reject(() => DeviceControl.WriteBrightness(69, (98u,100u), _ => { sets++; throw new System.ComponentModel.Win32Exception(31); }, () => throw new IOException("当前未知"), () => { }, _ => { }, _ => { }), "回读失败时禁止盲目补写");
        Check(sets == 1, "回读未知只发送过一次SET");
        sets = checks = 0;
        Reject(() => DeviceControl.WriteBrightness(69, (98u,100u), _ => { sets++; throw new System.ComponentModel.Win32Exception(31); }, () => (98u,100u), () => { if (++checks == 2) throw new InvalidOperationException("模式变化"); }, _ => { }, _ => { }), "模式或身份变化时停止亮度恢复流程");
        Check(sets == 1, "模式变化之后零补写");
        sets = 0;
        brightnessResult = DeviceControl.WriteBrightness(98, (98u,100u), _ => sets++, () => (98u,100u), () => { }, _ => { }, _ => { });
        Check(sets == 0 && brightnessResult.Current == 98, "已是目标亮度时跳过重复SET");
        var policy = HdrSafety.Policy(device)!;
        foreach (string id in new[] { "color-preset", "contrast", "red-gain", "brightness" }) {
            Reject(() => HdrSafety.Check(policy, id, _ => new() { Status = "ok", Current = 4 }), "HDR开启禁止" + id);
            Reject(() => HdrSafety.Check(policy, id, _ => new() { Status = "error" }), "HDR未知禁止" + id);
        }
        HdrSafety.Check(policy, "color-preset", _ => new() { Status = "ok", Current = 0 });
        Check(HdrSafety.Policy(devices[1]) == null, "其他型号不强套AOC保护规则");
        Reject(() => HdrSafety.Check(policy, HdrSafety.ProtectedId(policy, new() { Id = "vendor.temperature", VcpCode = 0x14 }), _ => new() { Status = "ok", Current = 4 }), "重命名标准色温功能不能绕过互锁");
        sim.Values["hdr-preset"] = 4; baseline = await workspace.Capture(device, default);
        var locked = DisplayWorkspace.Preflight(new() { DevicePath = device.DevicePath, Items = [new() { FeatureId = "color-preset", Value = "user" }] }, baseline);
        Check(locked.Single().Problem.Contains("HDR 已开启"), "方案预检拦截HDR内色温设置");
        var staleBaseline = Library.Copy(baseline); staleBaseline.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Result.Current = 0;
        sim.Requests.Clear(); Reject(() => workspace.SetFeature(device, brightnessCapability, "42", staleBaseline, default).GetAwaiter().GetResult(), "旧SDR缓存不能绕过新HDR互锁");
        Check(sim.Requests.All(r => r.Action == "read"), "互锁拒绝前零SET");
        sim.Values["hdr-preset"] = 0; sim.Values["color-preset"] = 11;
        var savedColors = new Dictionary<string, uint> { ["color-preset"] = 11, ["red-gain"] = 43, ["green-gain"] = 46, ["blue-gain"] = 49 };
        Reading ColorRead(string id) => new() { Capability = new() { Definition = Library.Copy(DeviceControl.Standard.Single(f => f.Id == id)) }, Result = new() { Status = "ok", Current = savedColors[id], Maximum = 100 } };
        var modeCapability = new Capability { Definition = new() { Id = "hdr-preset" }, Provider = "test", Hash = "hash" };
        HdrSafety.Remember(device, modeCapability, ColorRead);
        savedColors["color-preset"] = 6; savedColors["red-gain"] = 50; savedColors["green-gain"] = 50; savedColors["blue-gain"] = 50;
        var restoredIds = new List<string>();
        var restoredText = HdrSafety.Restore(device, modeCapability, ColorRead, (r, value) => { restoredIds.Add(r.Capability.Definition.Id); savedColors[r.Capability.Definition.Id] = ProtocolCodec.Value(r.Capability.Definition, value, 100, false); return new() { Confirmed = true, Message = "已写入并回读确认" }; });
        Check(restoredText.Contains("已恢复") && restoredIds.SequenceEqual(new[] { "color-preset", "red-gain", "green-gain", "blue-gain" }) && savedColors["red-gain"] == 43, "退出先恢复用户色温再恢复原RGB");
        restoredIds.Clear(); HdrSafety.Restore(device, modeCapability, ColorRead, (_, _) => { restoredIds.Add("unexpected"); return new(); });
        Check(restoredIds.Count == 0, "恢复记录消费一次，不覆盖后续手动改色温");
        HdrSafety.Remember(device, modeCapability, ColorRead);
        var changedMode = Library.Copy(modeCapability); changedMode.Hash = "new";
        Check(HdrSafety.Restore(device, changedMode, ColorRead, (_, _) => throw new Exception("不应发送")).Contains("协议已变化"), "协议变化不自动恢复");
        var msiAdapter = Packages.Parse(File.ReadAllText(Path.Combine(root, "examples/msi-g273q/adapter.json")));
        var aocStandard = package;
        var msiDevice = new MonitorData { Name = "MSI G273Q", Edid = new() { Manufacturer = "MSI", Product = "0x3CA8" } };
        var aocDevice = new MonitorData { Name = "Q27G4ZMN", Edid = new() { Manufacturer = "AOC", Product = "0xB411" } };
        Check(msiAdapter.Match.Matches(msiDevice) && !msiAdapter.Match.Matches(aocDevice) && !aocStandard.Match.Matches(msiDevice), "两型号适配包精确匹配，不串屏");
        Check(msiAdapter.Controls.All(c => c.Id is not ("brightness" or "contrast" or "red-gain" or "green-gain" or "blue-gain")) && msiAdapter.Controls.All(c => c.VcpCode != 0x72), "副屏包复用标准基础项，不伪造可写Gamma");
        var msiMode = msiAdapter.Controls.Single(c => c.Id == "game-mode");
        Check(!msiMode.Writable && new Reading { Capability = new() { Definition = msiMode }, Result = new() { Status = "ok", Current = 2, Maximum = 11 } }.Display(false).Contains("标准定义"), "副屏预设使用显式标准标签并保持只读");
        var ratio = aocStandard.Controls.Single(c => c.Id == "image-ratio");
        Check(!ratio.Writable && new Reading { Capability = new() { Definition = ratio }, Result = new() { Status = "ok", Current = 2, Maximum = 20 } }.Display(false).Contains("全屏"), "主屏比例独立于maximum字段解释且保持只读");
        var aocColor = aocStandard.Controls.Single(c => c.Id == "color-preset");
        Check(ProtocolCodec.Value(aocColor, "normal", 11, false) == 6 && new Reading { Capability = new() { Definition = aocColor }, Result = new() { Status = "ok", Current = 6, Maximum = 11 } }.Display(false).Contains("7300K"), "主屏色温使用厂家7300K定义而非通用7500K");
        Reject(() => ProtocolCodec.Value(msiAdapter.Controls.Single(c => c.Id == "color-preset"), "6", 11, false), "副屏包不开放未声明色温编码");
        Check(Packages.BackendFor(package, package.Controls.Single(c => c.Id == "hdr-preset")) == "amd-ddc" && Packages.BackendFor(package, package.Controls.Single(c => c.Id == "color-preset")) == "standard-ddc", "同一主屏包分别路由私有HDR和标准色温");
        var noOverride = Library.Copy(package); noOverride.Controls.Single(c => c.Id == "color-preset").Backend = null;
        Reject(() => Packages.Validate(noOverride), "缺少标准通道声明的混合包拒绝错发私有报文");
        var badBackend = Library.Copy(package); badBackend.Controls[0].Backend = "unknown-transport";
        Reject(() => Packages.Validate(badBackend), "未实现通道明确拒绝，不静默换协议");
        var badDefault = Library.Copy(package); badDefault.OverridesStandard = ["hdr-preset"];
        Reject(() => Packages.Validate(badDefault), "默认标准覆盖不能冒用私有功能");
        var cleanDevice = Library.Copy(hw); cleanDevice.DevicePath = "clean-merged-device"; p1 = Packages.Install(Path.Combine(root, "examples/aoc-q27g4zmn/adapter.json")); Packages.Attach(cleanDevice, p1, false);
        Check(CapabilityCatalog.Resolve(cleanDevice).Single(c => c.Definition.Id == "color-preset").Provider == p1.Id, "首次应用完整包无需手选标准色温来源");
        Packages.Choose(cleanDevice, "color-preset", "standard");
        Check(CapabilityCatalog.Resolve(cleanDevice).Single(c => c.Definition.Id == "color-preset").Provider == "standard", "明确选择标准来源不会被型号默认覆盖");

    }
    sealed class IgnoredWriteBackend(SimulatedDisplayBackend inner) : IDisplayBackend
    {
        public Task<List<MonitorData>> Devices(CancellationToken t) => inner.Devices(t);
        public List<Capability> Capabilities(MonitorData d) => inner.Capabilities(d);
        public Task<ControlResult> Execute(ControlRequest r, CancellationToken t) => r.Action == "write" && r.ControlId == "brightness" ? Task.FromResult(new ControlResult { RequestId = r.RequestId, Status = "mismatch", Sent = true, Before = 100, Current = 100, Maximum = 100, Message = "显示器仍返回100" }) : inner.Execute(r, t);
    }
    sealed class FaultBackend(SimulatedDisplayBackend inner) : IDisplayBackend
    {
        public Task<List<MonitorData>> Devices(CancellationToken t) => inner.Devices(t);
        public List<Capability> Capabilities(MonitorData d) => inner.Capabilities(d);
        public Task<ControlResult> Execute(ControlRequest r, CancellationToken t) => r.Action == "write" && r.ControlId == "contrast" ? Task.FromResult(new ControlResult { RequestId = r.RequestId, Message = "测试注入的发送前失败" }) : inner.Execute(r, t);
    }
}

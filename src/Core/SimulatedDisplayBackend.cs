namespace MonitorInspector;

// Explicitly selected fixture backend. Never used as a fallback for a real device.
public sealed class SimulatedDisplayBackend : IDisplayBackend
{
    public System.Collections.Concurrent.ConcurrentQueue<ControlRequest> Requests { get; } = new();
    public bool Hdr { get; set; }
    public bool Offline { get; set; }
    public string? FailFeature { get; set; }
    public string? FailNextWriteFeature { get; set; }
    public Dictionary<string, int> FailNextReads { get; } = [];
    public Dictionary<string, uint> Values { get; } = new() { ["brightness"] = 85, ["contrast"] = 50, ["red-gain"] = 45, ["green-gain"] = 45, ["blue-gain"] = 50, ["hdr-preset"] = 0, ["color-preset"] = 5, ["low-blue-light"] = 0 };
    public Task<List<MonitorData>> Devices(CancellationToken token) => Task.FromResult(Offline ? new List<MonitorData>() : new List<MonitorData> {
        new() { Name = "AOC Q27G4ZMN · 模拟设备", Device = "SIM-1", DevicePath = "simulation-aoc", Connection = "DisplayPort", Resolution = "2560 × 1440", RefreshRate = "240 Hz", Hdr = Hdr ? "开启" : "关闭", WindowsHdrEnabled = Hdr, HdrSupported = true, CanSetWindowsHdr = true, HdrUserEnabled = Hdr, Edid = new() { Manufacturer = "AOC", Product = "0xB411" } },
        new() { Name = "第二品牌 · 长名称 USB 控制端点模拟显示器", Device = "SIM-2", DevicePath = "simulation-usb", Connection = "USB SDK（模拟）", Resolution = "3840 × 2160", RefreshRate = "144 Hz", Hdr = "关闭", WindowsHdrEnabled = false, Edid = new() { Manufacturer = "TST", Product = "0x1234" } }
    });
    public List<Capability> Capabilities(MonitorData device)
    {
        if (device.Device == "SIM-2") return new[] {
            new ControlDefinition { Id = "brightness", Name = "背光强度", Writable = true, Group = "image" },
            new ControlDefinition { Id = "vendor.usb-status", Name = "USB 控制端点", ValueType = "text", Group = "information" },
            new ControlDefinition { Id = "vendor.calibrate", Name = "开始校准（模拟动作）", ValueType = "action", Readable = false, Writable = true, Group = "device" },
            new ControlDefinition { Id = "vendor.write-only", Name = "仅发送通知", ValueType = "text", Readable = false, Writable = true, Group = "device" }
        }.Select(f => new Capability { Definition = f, Provider = "simulation-usb" }).ToList();
        var all = DeviceControl.Standard.Select(f => new Capability { Definition = Library.Copy(f) }).ToList();
        foreach (var rgb in all.Where(c => c.Definition.Id is "red-gain" or "green-gain" or "blue-gain")) rgb.Definition.WritableWhen = [new() { FeatureId = "color-preset", EqualsValue = 8, Reason = "切换到用户色温后可调（模拟条件，非真实设备结论）" }];
        all.AddRange(new[] {
            new ControlDefinition { Id = "hdr-preset", Name = "显示器 HDR 预设", Writable = true, ValueType = "enum", Group = "image", AffectedFeatures = ["color-preset", "red-gain", "green-gain"], Rules = [new() { Maximum = 3, WindowsHdr = false, Values = new() { ["off"] = 0, ["picture"] = 1, ["movie"] = 2, ["game"] = 3 }, Labels = new() { ["off"] = "关闭", ["picture"] = "图片", ["movie"] = "电影", ["game"] = "游戏" } }, new() { Maximum = 4, WindowsHdr = true, Values = new() { ["off"] = 0, ["game"] = 4 }, Labels = new() { ["off"] = "关闭", ["game"] = "游戏" } }] },
            new ControlDefinition { Id = "low-blue-light", Name = "低蓝光模式", Writable = true, WriteWhenWindowsHdr = false, ValueType = "enum", Group = "image", Rules = [new() { Maximum = 4, Values = new() { ["off"] = 0, ["multimedia"] = 1, ["web"] = 2, ["office"] = 3, ["reading"] = 4 }, Labels = new() { ["off"] = "关闭", ["multimedia"] = "多媒体", ["web"] = "网络", ["office"] = "办公室", ["reading"] = "阅读" } }] },
            new ControlDefinition { Id = "crosshair", Name = "准星", ValueType = "boolean", Writable = true, Group = "game", Rules = [new() { Maximum = 1, Values = new() { ["off"] = 0, ["on"] = 1 }, Labels = new() { ["off"] = "关闭", ["on"] = "开启" } }] }
        }.Select(f => new Capability { Definition = f, Provider = "simulation-aoc", Version = "1.0.0" }));
        return all;
    }
    public async Task<ControlResult> Execute(ControlRequest request, CancellationToken token)
    {
        Requests.Enqueue(Library.Copy(request));
        await Task.Delay(8, token);
        var result = new ControlResult { RequestId = request.RequestId };
        if (request.Action == "write" && request.ControlId == FailNextWriteFeature) { FailNextWriteFeature = null; result.Message = "写入前读取失败（模拟），未发送设置"; return result; }
        if (request.Action == "read" && FailNextReads.GetValueOrDefault(request.ControlId) > 0) { FailNextReads[request.ControlId]--; result.Message = "模式切换暂时不可读（模拟）"; return result; }
        if (Offline || request.ControlId == FailFeature) { result.Message = Offline ? "设备离线（模拟）" : "回读超时（模拟）"; return result; }
        var device = (await Devices(token)).Single(d => d.DevicePath == request.Device.DevicePath);
        if (request.Action == "system-hdr") { bool before = Hdr; Hdr = request.Value == "on"; return new() { RequestId = request.RequestId, Status = "ok", Sent = true, Confirmed = true, Before = before ? 1u : 0u, Current = Hdr ? 1u : 0u, Maximum = 1, Message = "系统 HDR 已切换（模拟）" }; }
        var c = Capabilities(device).Single(c => c.Definition.Id == request.ControlId); var f = request.DraftPackage?.Controls.Single(x => x.Id == request.ControlId) ?? c.Definition;
        uint maximum = f.Id == "hdr-preset" ? Hdr ? 4u : 3u : f.Id == "low-blue-light" ? 4u : f.ValueType == "boolean" ? 1u : 100u;
        if (request.Action == "write") {
            try { HdrSafety.Check(HdrSafety.Policy(device), f.Id, id => new() { Status = FailFeature == id ? "error" : "ok", Current = Values.GetValueOrDefault(id) }); }
            catch (InvalidOperationException ex) { result.Message = ex.Message; return result; }
            if (device.WindowsHdrEnabled != request.Device.WindowsHdrEnabled) { result.Message = "系统 HDR 条件已变化"; return result; }
            if (f.ValueType is "action" or "text") { result.Status = "ok"; result.Sent = true; result.Message = "指令已发送（模拟），无回读"; return result; }
            result.Before = Values.GetValueOrDefault(f.Id, 50u);
            uint desired = ProtocolCodec.Value(f, request.Value!, maximum, device.WindowsHdrEnabled);
            var policy = HdrSafety.Policy(device);
            Reading ColorRead(string id) => new() { Capability = Capabilities(device).Single(c => c.Definition.Id == id), Result = new() { Status = FailFeature == id ? "error" : "ok", Current = Values.GetValueOrDefault(id), Maximum = 100 } };
            if (policy?.RestoreColorOnExit == true && f.Id == policy.ModeFeatureId && result.Before == policy.OffValue && desired != policy.OffValue) HdrSafety.Remember(device, c, ColorRead);
            Values[f.Id] = desired; result.Sent = result.Confirmed = true;
            if (policy?.RestoreColorOnExit == true && f.Id == policy.ModeFeatureId && result.Before != policy.OffValue && desired == policy.OffValue)
                result.Message = HdrSafety.Restore(device, c, ColorRead, (reading, value) => {
                    var restore = DisplayWorkspace.Request(device, reading.Capability, "write", value); Requests.Enqueue(restore);
                    try { HdrSafety.Check(policy, restore.ControlId, id => new() { Status = "ok", Current = Values.GetValueOrDefault(id) }); }
                    catch (InvalidOperationException ex) { return new() { Message = ex.Message }; }
                    if (FailFeature == restore.ControlId) return new() { Message = "恢复失败（模拟）" };
                    Values[restore.ControlId] = ProtocolCodec.Value(reading.Capability.Definition, value, 100, device.WindowsHdrEnabled);
                    return new() { Status = "ok", Sent = true, Confirmed = true, Message = "已写入并回读确认" };
                });
        }
        result.Current = Values.GetValueOrDefault(f.Id, 50u); result.Maximum = maximum; result.Status = "ok"; result.Message = result.Message.Length > 0 ? result.Message : "读取成功（模拟）";
        if (f.ValueType == "text") { result.Current = result.Maximum = null; result.Text = "USB endpoint 1 · connected"; }
        return result;
    }
}

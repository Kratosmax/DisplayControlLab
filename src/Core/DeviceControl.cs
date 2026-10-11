using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MonitorInspector;

public sealed class ControlRequest
{
    public int ApiVersion { get; set; } = 1;
    public string Action { get; set; } = "read";
    public MonitorData Device { get; set; } = new();
    public string? PackageId { get; set; }
    public string ControlId { get; set; } = "";
    public string? Value { get; set; }
    public string? ExpectedHash { get; set; }
    public AdapterPackage? DraftPackage { get; set; }
    public List<string> DependencyTrail { get; set; } = [];
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed class ControlResult
{
    public int ApiVersion { get; set; } = 1;
    public string RequestId { get; set; } = "";
    public string Status { get; set; } = "error";
    public string Message { get; set; } = "";
    public uint? Current { get; set; }
    public uint? Maximum { get; set; }
    public uint? Before { get; set; }
    public uint? BeforeMaximum { get; set; }
    public bool Sent { get; set; }
    public bool Confirmed { get; set; }
    public string? Text { get; set; }
    public List<ControlDefinition>? Capabilities { get; set; }
    public string Raw { get; set; } = "";
    public int? NativeErrorCode { get; set; }
    public int WriteAttempts { get; set; }
}
public static class ProtocolCodec
{
    public static byte[] Packet(byte[] payload)
    {
        if (payload.Length is 0 or > 64) throw new InvalidDataException("报文长度无效");
        byte[] packet = [0x6E, 0x51, (byte)(0x80 | payload.Length), .. payload, 0];
        foreach (byte value in packet[..^1]) packet[^1] ^= value;
        return packet;
    }
    public static (uint Current, uint Maximum) Parse(RawProtocol rule, byte[] reply)
    {
        if (reply.Length != rule.ReplyLength || !reply.AsSpan().StartsWith(Packages.Hex(rule.ReplyPrefix))) throw new InvalidDataException("响应格式或状态不符");
        byte checksum = rule.ChecksumSeed;
        foreach (byte value in reply) checksum ^= value;
        if (checksum != 0) throw new InvalidDataException("响应校验失败");
        uint current = (uint)(reply[rule.CurrentOffset] << 8 | reply[rule.CurrentOffset + 1]);
        uint maximum = (uint)(reply[rule.MaximumOffset] << 8 | reply[rule.MaximumOffset + 1]);
        // A protocol reply is evidence, not yet a numeric capability.
        // Numeric bounds are checked after resolving the feature's value type.
        return (current, maximum);
    }
    public static ValueRule? Rule(ControlDefinition control, uint maximum, bool? windowsHdr)
    {
        var rules = control.Rules.Where(x => (x.MatchAnyMaximum || x.Maximum == maximum) && (x.WindowsHdr == null || x.WindowsHdr == windowsHdr)).ToList();
        if (rules.Count > 1) throw new InvalidDataException("多个条件规则匹配，拒绝操作");
        return rules.SingleOrDefault();
    }
    public static uint Value(ControlDefinition control, string value, uint maximum, bool? windowsHdr)
    {
        if (control.WriteWhenWindowsHdr.HasValue && control.WriteWhenWindowsHdr != windowsHdr) throw new InvalidOperationException("当前系统 HDR 条件下未开放该功能写入");
        if (control.Rules.Count > 0)
        {
            var rule = Rule(control, maximum, windowsHdr) ?? throw new InvalidOperationException("当前能力范围与系统状态没有匹配规则");
            string id = rule.Labels.FirstOrDefault(x => x.Value == value).Key ?? value;
            if (!rule.Values.TryGetValue(id, out uint mapped)) throw new ArgumentException("请选择当前条件定义的模式名称");
            return CheckAllowed(control, mapped);
        }
        if (!uint.TryParse(value, out uint number) || control.ValueType == "number" && (number > maximum || number < control.Minimum || (number - control.Minimum) % control.Step != 0)) throw new ArgumentException("数值超出显示器当前范围");
        return CheckAllowed(control, number);
    }
    private static uint CheckAllowed(ControlDefinition control, uint value)
    {
        if (control.AllowedWriteValues != null && !control.AllowedWriteValues.Contains(value)) throw new InvalidOperationException("该取值尚未开放写入；读取选项不代表全部验证可写");
        return value;
    }
}
public static partial class DeviceControl
{
    public static List<ControlDefinition> Standard { get; } =
    [
        StandardFeature("brightness", "亮度", 0x10, "number", "image", true),
        StandardFeature("contrast", "对比度", 0x12, "number", "image", true),
        StandardFeature("red-gain", "红色增益", 0x16, "number", "image", true),
        StandardFeature("green-gain", "绿色增益", 0x18, "number", "image", true),
        StandardFeature("blue-gain", "蓝色增益", 0x1A, "number", "image", true),
        StandardFeature("color-preset", "色温预设", 0x14, "enum", "image", false),
        StandardFeature("volume", "音量", 0x62, "number", "audio", true),
        StandardFeature("mute", "静音状态", 0x8D, "enum", "audio", false),
        StandardFeature("input-source", "输入源", 0x60, "enum", "connection", false),
        StandardFeature("power", "电源状态", 0xD6, "enum", "device", false),
        StandardFeature("display-preset", "画面预设", 0xDC, "enum", "image", false),
        StandardFeature("osd", "OSD 状态", 0xCA, "enum", "osd", false),
        StandardFeature("osd-language", "OSD 语言", 0xCC, "enum", "osd", false),
        StandardFeature("mccs-version", "MCCS 版本", 0xDF, "status", "information", false)
    ];
    private static ControlDefinition StandardFeature(string id, string name, byte code, string type, string group, bool writable) =>
        StandardOptions.Feature(id, name, code, type, group, writable);
    public static ControlResult Execute(ControlRequest request, MonitorData? readDevice = null, List<Capability>? readCatalogue = null, bool retryStandardRead = false)
    {
        var result = new ControlResult { RequestId = request.RequestId };
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Device.DevicePath)));
        using var mutex = new Mutex(false, "Local\\DisplayControl-" + key);
        bool locked = false;
        try
        {
            try { locked = mutex.WaitOne(TimeSpan.FromSeconds(3)); } catch (AbandonedMutexException) { locked = true; }
            if (!locked) throw new IOException("显示器正在被另一操作使用");
            if (request.ApiVersion != 1 || request.Action is not ("read" or "write" or "probe" or "describe" or "system-hdr") || !Guid.TryParseExact(request.RequestId, "N", out _)) throw new InvalidDataException("操作接口不兼容或请求标识无效");
            if (readDevice != null && request.Action != "read") throw new InvalidOperationException("批量缓存仅可用于读取");
            var matches = (readDevice == null ? Native.Enumerate() : new List<MonitorData> { readDevice }).Where(x => x.DevicePath == request.Device.DevicePath && x.Device == request.Device.Device).ToList();
            if (matches.Count != 1 || !HardwareReports.SameDevice(matches[0], request.Device)) throw new InvalidOperationException("设备连接变化或身份不唯一，请刷新");
            var monitor = matches.Single();
            if (request.Action == "system-hdr") {
                if (request.ControlId != "windows-hdr" || request.Value is not ("on" or "off")) throw new InvalidDataException("系统 HDR 请求无效");
                if (!monitor.CanSetWindowsHdr) throw new NotSupportedException("当前 Windows/驱动未提供可用的 HDR 专用设置接口");
                result.Before = monitor.WindowsHdrEnabled == true ? 1u : 0u; result.BeforeMaximum = result.Maximum = 1;
                Journal(request, new() { RequestId = request.RequestId, Status = "intent", Message = "用户请求切换系统 HDR，显示器可能短暂黑屏" });
                result.Sent = true; Native.SetWindowsHdr(monitor, request.Value == "on");
                for (int i = 0; i < 12; i++) {
                    Thread.Sleep(i == 0 ? 400 : 150);
                    var now = Native.Enumerate().SingleOrDefault(d => HardwareReports.SameDevice(d, monitor));
                    if (now?.WindowsHdrEnabled == (request.Value == "on")) { result.Confirmed = true; result.Current = now.WindowsHdrEnabled == true ? 1u : 0u; break; }
                }
                result.Status = result.Confirmed ? "ok" : "unknown"; result.Message = result.Confirmed ? "系统 HDR 已切换，正在重载显示器状态" : "系统 HDR 请求已发送，状态尚未确认"; return result;
            }
            using var packageGate = request.PackageId != null || request.DraftPackage != null ? Packages.Gate() : null;
            if (request.DraftPackage == null && request.Action is "read" or "write") {
                var route = (readCatalogue ?? CapabilityCatalog.Resolve(monitor)).SingleOrDefault(c => c.Definition.Id == request.ControlId) ?? throw new InvalidOperationException("功能已不可用");
                if (route.Problem.Length > 0) throw new InvalidOperationException(route.Problem);
                if ((route.Provider == "standard" ? null : route.Provider) != request.PackageId) throw new InvalidOperationException("功能提供者选择已改变，请重新读取/预检");
            }
            if (request.Action == "write" && monitor.WindowsHdrEnabled != request.Device.WindowsHdrEnabled) throw new InvalidOperationException("系统 HDR 条件已改变，请刷新后重试");
            AdapterPackage? package = null;
            ControlDefinition control;
            if (request.DraftPackage is { } draft)
            {
                Packages.Validate(draft);
                if (draft.Kind != "protocol" || !draft.Match.Matches(monitor)) throw new InvalidOperationException("草稿只允许匹配设备的声明式协议；代码插件先安装并信任");
                package = draft; control = draft.Controls.Single(x => x.Id == request.ControlId);
            }
            else if (request.PackageId == null) control = Standard.Single(x => x.Id == request.ControlId);
            else
            {
                package = Packages.ForDevice(monitor).SingleOrDefault(x => x.Id == request.PackageId);
                if (package == null) throw new InvalidOperationException("适配包未启用、未绑定或设备不匹配");
                if (request.ExpectedHash != null && request.ExpectedHash != Packages.ContentHash(package)) throw new InvalidOperationException("适配包内容已改变，请重新预检");
                if (package.Kind == "plugin" && !Packages.IsTrusted(package)) throw new InvalidOperationException("插件代码已变化或未经信任，请重新确认启用");
                if (package.Kind == "plugin" && request.Action is "probe" or "describe") {
                    request.Device = monitor;
                    result = ProcessClient.Run(Packages.ContainedPath(package.DirectoryPath, package.EntryPoint!), request, 15000).GetAwaiter().GetResult();
                    if (result.Capabilities is { Count: > 0 }) { var description = Library.Copy(package); description.Controls = result.Capabilities; Packages.Validate(description); }
                    return result;
                }
                control = package.Controls.Single(x => x.Id == request.ControlId);
            }
            if (request.Action is "probe" or "describe") throw new InvalidOperationException("声明式协议使用清单描述能力；运行时探测由代码插件实现");
            if (request.Action == "read" && (!control.Readable || control.ValueType == "action")) throw new InvalidOperationException("该功能仅写入或执行，不提供读取");
            if (request.Action == "write" && !control.Writable) throw new InvalidOperationException("该功能不允许写入");
            var safety = request.Action == "write" ? HdrSafety.Policy(monitor, request.DraftPackage) : null;
            ControlResult ReadSafety(string id) {
                if (id == control.Id) throw new InvalidOperationException("HDR 互锁规则引用自身");
                var candidate = CapabilityCatalog.Resolve(monitor).SingleOrDefault(c => c.Definition.Id == id);
                if (candidate == null || candidate.Problem.Length > 0) return new() { Message = "HDR 状态来源不可用" };
                return Execute(DisplayWorkspace.Request(monitor, candidate, "read"));
            }
            Reading ReadColor(string id) {
                var candidate = CapabilityCatalog.Resolve(monitor).Single(c => c.Definition.Id == id);
                return new() { Capability = candidate, Result = candidate.Problem.Length > 0 ? new() { Message = candidate.Problem } : Execute(DisplayWorkspace.Request(monitor, candidate, "read")) };
            }
            var mode = new Capability { Definition = control, Provider = package?.Id ?? "standard", Hash = request.Action != "write" ? "" : package == null ? "builtin" : request.DraftPackage != null ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(package, Packages.Json)))) : request.ExpectedHash ?? Packages.ContentHash(package) };
            string RestoreColor() => HdrSafety.Restore(monitor, mode, ReadColor, (reading, value) => Execute(DisplayWorkspace.Request(monitor, reading.Capability, "write", value)));
            if (request.DependencyTrail.Count > 8 || request.DependencyTrail.Contains(control.Id)) throw new InvalidOperationException("条件依赖循环或过深");
            if (request.Action == "write" && control.WriteWhenWindowsHdr.HasValue && control.WriteWhenWindowsHdr != monitor.WindowsHdrEnabled) throw new InvalidOperationException("当前系统 HDR 条件下未开放该功能写入");
            foreach (var condition in request.Action == "read" ? control.ReadableWhen : control.WritableWhen) {
                var candidate = CapabilityCatalog.Resolve(monitor).SingleOrDefault(c => c.Definition.Id == condition.FeatureId);
                if (request.DraftPackage != null && package!.Controls.SingleOrDefault(c => c.Id == condition.FeatureId) is { } draftControl) candidate = new() { Definition = draftControl, Provider = package.Id };
                if (candidate == null || candidate.Problem.Length > 0) throw new InvalidOperationException("条件依赖不可用：" + condition.FeatureId);
                var dependency = new ControlRequest { Device = monitor, ControlId = condition.FeatureId, PackageId = candidate.Provider == "standard" ? null : candidate.Provider, DraftPackage = request.DraftPackage, DependencyTrail = [.. request.DependencyTrail, control.Id] };
                var value = Execute(dependency);
                if (value.Status != "ok" || value.Current != condition.EqualsValue) throw new InvalidOperationException(value.Status == "ok" ? condition.Reason.Length == 0 ? "当前模式条件不满足" : condition.Reason : "条件未知：" + condition.FeatureId);
            }
            if (package?.Kind == "plugin")
            {
                request.Device = monitor;
                uint? modeBefore = null;
                if (request.Action == "write" && control.ValueType is "number" or "enum" or "boolean") {
                    uint maximum = ushort.MaxValue;
                    if (control.Readable) {
                        var probe = new ControlRequest { Device = monitor, PackageId = package.Id, ControlId = control.Id };
                        var state = ProcessClient.Run(Packages.ContainedPath(package.DirectoryPath, package.EntryPoint!), probe, 15000).GetAwaiter().GetResult();
                        if (state.Status != "ok" || !state.Current.HasValue || !state.Maximum.HasValue) throw new InvalidDataException("插件写入前读取失败或缺少有效范围");
                        maximum = state.Maximum.Value;
                        modeBefore = state.Current;
                    }
                    ProtocolCodec.Value(control, request.Value ?? "", maximum, monitor.WindowsHdrEnabled);
                    if (safety?.RestoreColorOnExit == true && control.Id == safety.ModeFeatureId && modeBefore == safety.OffValue && ProtocolCodec.Value(control, request.Value ?? "", maximum, monitor.WindowsHdrEnabled) != safety.OffValue)
                        HdrSafety.Remember(monitor, mode, ReadColor);
                }
                if (request.Action == "write") { HdrSafety.Check(safety, HdrSafety.ProtectedId(safety, control), ReadSafety); Journal(request, new() { RequestId = request.RequestId, Status = "intent", Message = "即将调用代码插件写入；结果尚未确认" }); }
                if (request.Action == "write") result.Sent = true;
                result = ProcessClient.Run(Packages.ContainedPath(package.DirectoryPath, package.EntryPoint!), request, 15000).GetAwaiter().GetResult();
                if (control.Readable && control.ValueType is "number" or "enum" or "boolean" && result.Status == "ok" && (!result.Current.HasValue || !result.Maximum.HasValue)) throw new InvalidDataException("数值型能力缺少读数或范围");
                if (control.ValueType == "number" && result.Status == "ok" && result.Current > result.Maximum)
                    throw new InvalidDataException("数值功能的读数超过范围");
                if (request.Action == "write" && result.Status == "ok" && result.Confirmed && safety?.RestoreColorOnExit == true && control.Id == safety.ModeFeatureId && modeBefore.HasValue && modeBefore != safety.OffValue && result.Current == safety.OffValue) {
                    try { result.Message += "；" + RestoreColor(); }
                    catch (Exception ex) { result.Message += "；HDR 已关闭，色温恢复未完成：" + ex.Message; }
                }
                return result;
            }
            string backend = Packages.BackendFor(package, control);
            (uint Current, uint Maximum) Read()
            {
                if (backend == "standard-ddc") return request.Action == "write" || retryStandardRead
                    ? ReadWithRetry(() => Native.Vcp(monitor, control.VcpCode!.Value, null))
                    : Native.Vcp(monitor, control.VcpCode!.Value, null);
                var reply = AdlTransport.Exchange(monitor, ProtocolCodec.Packet(Packages.Hex(control.Raw!.ReadPayload)), control.Raw.ReplyLength);
                result.Raw = Convert.ToHexString(reply); return ProtocolCodec.Parse(control.Raw, reply);
            }
            var before = Read(); result.Before = before.Current; result.BeforeMaximum = before.Maximum; result.Current = before.Current; result.Maximum = before.Maximum;
            if (control.ValueType == "number" && before.Current > before.Maximum) throw new InvalidDataException("数值功能的读数超过范围");
            if (request.Action == "write")
            {
                uint desired = ProtocolCodec.Value(control, request.Value ?? "", before.Maximum, monitor.WindowsHdrEnabled);
                if (safety?.RestoreColorOnExit == true && control.Id == safety.ModeFeatureId && before.Current == safety.OffValue && desired != safety.OffValue)
                    HdrSafety.Remember(monitor, mode, ReadColor);
                HdrSafety.Check(safety, HdrSafety.ProtectedId(safety, control), ReadSafety);
                Journal(request, new ControlResult { RequestId = request.RequestId, Status = "intent", Before = before.Current, Current = desired, Maximum = before.Maximum });
                void ValidateContext() {
                    var current = Native.Enumerate().SingleOrDefault(d => HardwareReports.SameDevice(d, monitor));
                    if (current == null || current.WindowsHdrEnabled != monitor.WindowsHdrEnabled) throw new InvalidOperationException("操作期间设备或 Windows HDR 状态变化，需重新读取");
                    HdrSafety.Check(safety, HdrSafety.ProtectedId(safety, control), ReadSafety);
                }
                (uint Current, uint Maximum) after;
                if (backend == "standard-ddc" && package == null && control.VcpCode == 0x10 && control.ValueType == "number") {
                    after = WriteBrightness(desired, before,
                        value => { result.Sent = true; result.WriteAttempts++; Native.Vcp(monitor, 0x10, value); },
                        Read, ValidateContext, ex => result.NativeErrorCode = ex.NativeErrorCode);
                }
                else
                {
                    result.Sent = true; result.WriteAttempts++;
                    if (backend == "standard-ddc") Native.Vcp(monitor, control.VcpCode!.Value, desired);
                    else {
                        if (desired > ushort.MaxValue) throw new InvalidDataException("私有值超过两字节");
                        AdlTransport.Exchange(monitor, ProtocolCodec.Packet([.. Packages.Hex(control.Raw!.WritePrefix), (byte)(desired >> 8), (byte)desired]), 0);
                    }
                    Thread.Sleep(control.StabilizationMs > 0 ? control.StabilizationMs : backend == "standard-ddc" ? 120 : 800);
                    var afterMonitor = Native.Enumerate().Single(x => x.DevicePath == monitor.DevicePath && x.Device == monitor.Device);
                    if (afterMonitor.WindowsHdrEnabled != monitor.WindowsHdrEnabled) throw new InvalidOperationException("操作期间 Windows HDR 状态变化，需重新读取");
                    after = Read();
                    foreach (int delay in new[] { 150, 250 }) { if (after.Current == desired) break; Thread.Sleep(delay); after = Read(); }
                }
                result.Current = after.Current; result.Maximum = after.Maximum;
                result.Confirmed = after.Maximum == before.Maximum && after.Current == desired;
                result.Status = result.Confirmed ? "ok" : "mismatch";
                result.Message = result.Confirmed ? "已写入并回读确认" : $"目标 {desired}，显示器回读 {after.Current}，设置尚未确认";
                if (result.Confirmed && !result.Sent) result.Message = "当前已是目标值，无需重复写入";
                if (result.NativeErrorCode.HasValue && !result.Confirmed) result.Message += $"（Windows DDC 错误 {result.NativeErrorCode.Value}）";
                if (result.Confirmed && safety?.RestoreColorOnExit == true && control.Id == safety.ModeFeatureId && before.Current != safety.OffValue && desired == safety.OffValue) {
                    // Exit is read-back confirmed before any restoration SET. Nested writes
                    // run the same identity, HDR interlock and provider checks under the mutex.
                    try { result.Message += "；" + RestoreColor(); }
                    catch (Exception ex) { result.Message += "；HDR 已关闭，色温恢复未完成：" + ex.Message; }
                }
            }
            else { result.Status = "ok"; result.Message = "读取成功"; }
        }
        catch (Exception ex) { if (ex is System.ComponentModel.Win32Exception native) result.NativeErrorCode = native.NativeErrorCode; result.Status = result.Sent ? "unknown" : "error"; result.Message = result.Sent ? "已发起设置，但结果无法确认；请重新读取核查。" + ex.Message : request.Action == "write" ? "未发送设置：" + ex.Message : ex.Message; if (result.NativeErrorCode.HasValue) result.Message += $"（Windows 错误 {result.NativeErrorCode.Value}）"; }
        finally { if (locked) mutex.ReleaseMutex(); Journal(request, result); }
        return result;
    }
    private static void Journal(ControlRequest request, ControlResult result)
    {
        Directory.CreateDirectory(Path.Combine(Packages.Home, "logs"));
        var record = new { time = DateTimeOffset.Now, request, result };
        // Separate files avoid interprocess truncation and preserve each write intent.
        string filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.RequestId)));
        File.AppendAllText(Path.Combine(Packages.Home, "logs", filename + ".jsonl"), JsonSerializer.Serialize(record, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n");
    }
    internal static List<FeatureRow> ReadInstalled(MonitorData monitor)
    {
        var rows = new List<FeatureRow>();
        List<Capability> capabilities;
        try { capabilities = CapabilityCatalog.Resolve(monitor).Where(c => c.Provider != "standard" && c.Problem.Length == 0 && c.Definition.Readable).ToList(); }
        catch (Exception ex) { return [new() { Name = "适配包", Status = ex.Message }]; }
        foreach (var capability in capabilities)
        {
            var control = capability.Definition; var p = Packages.ForDevice(monitor).Single(p => p.Id == capability.Provider);
            var readAt = DateTimeOffset.Now;
            var watch = Stopwatch.StartNew();
            var value = Execute(new() { Device = monitor, PackageId = p.Id, ControlId = control.Id });
            string interpretation = "";
            if (value.Maximum.HasValue && value.Current.HasValue)
                try { interpretation = ProtocolCodec.Rule(control, value.Maximum.Value, monitor.WindowsHdrEnabled)?.Values.FirstOrDefault(x => x.Value == value.Current.Value).Key ?? ""; } catch { }
            rows.Add(new() { Code = p.Id + "/" + control.Id, Name = control.Name, Declared = p.Name, Current = value.Current, Maximum = value.Maximum, Physical = monitor.Device, Values = value.Raw, Status = value.Message, Interpretation = interpretation, ReadAt = readAt, QueryDurationMs = watch.Elapsed.TotalMilliseconds });
        }
        return rows;
    }
}
public static class ProcessClient
{
    public static async Task<ControlResult> Run(string executable, ControlRequest request, int timeoutMs = 20000, CancellationToken token = default, bool worker = false)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false), WorkingDirectory = Path.GetDirectoryName(executable)! };
        if (worker) start.ArgumentList.Add("--worker");
        using var process = Process.Start(start) ?? throw new IOException("无法启动通信进程");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(timeoutMs);
        async Task<string> ReadBounded(StreamReader reader)
        {
            var text = new StringBuilder(); var buffer = new char[2048]; int length;
            while ((length = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
            {
                if (text.Length + length > 1024 * 1024) throw new InvalidDataException("插件输出超过 1 MB");
                text.Append(buffer, 0, length);
            }
            return text.ToString();
        }
        try
        {
            var output = ReadBounded(process.StandardOutput); var error = ReadBounded(process.StandardError);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Packages.Json).AsMemory(), timeout.Token); process.StandardInput.Close();
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).WaitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new IOException("插件退出失败：" + (await error)[..Math.Min(500, (await error).Length)]);
            var result = JsonSerializer.Deserialize<ControlResult>(await output, Packages.Json) ?? throw new InvalidDataException("插件没有结果");
            if (result.ApiVersion != 1 || result.RequestId != request.RequestId || result.Status is not ("ok" or "error" or "mismatch" or "unsupported" or "unknown")) throw new InvalidDataException("插件返回不兼容或请求标识不符");
            if (result.Confirmed && (!result.Sent || request.Action is not ("write" or "system-hdr"))) throw new InvalidDataException("插件验证标记不符");
            if (result.Status == "ok" && request.Action == "read" && !result.Current.HasValue && result.Text == null) throw new InvalidDataException("插件成功结果缺少读数或文本状态");
            return result;
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}

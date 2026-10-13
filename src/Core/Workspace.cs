using System.Text.Json;
using System.IO.Compression;

namespace MonitorInspector;

public static class Library
{
    public static string Folder(string kind) { string dir = Path.Combine(Packages.Home, kind); Directory.CreateDirectory(dir); return dir; }
    public static void Save<T>(string kind, string id, T value)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("资料 id 无效");
        SaveFile(Path.Combine(Folder(kind), id + ".json"), value);
    }
    internal static void SaveFile<T>(string path, T value)
    {
        string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(stage, JsonSerializer.Serialize(value, Packages.Json));
            for (int attempt = 0; ; attempt++) {
                try { File.Move(stage, path, true); break; }
                catch (IOException ex) when (attempt < 5 && (ex.HResult & 0xffff) is 32 or 33) { Thread.Sleep(30 << attempt); }
                catch (UnauthorizedAccessException) when (attempt < 5 && File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly)) { Thread.Sleep(30 << attempt); }
            }
        } finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    public static List<T> All<T>(string kind)
    {
        var result = new List<T>();
        foreach (string path in Directory.GetFiles(Folder(kind), "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            try { var item = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Packages.Json); if (item != null) result.Add(item); }
            catch (JsonException) { }
        return result;
    }
    public static T Get<T>(string kind, string id) => All<T>(kind).Single(x => (string?)typeof(T).GetProperty("Id")!.GetValue(x) == id);
    public static void Delete(string kind, string id) { if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException(); File.Delete(Path.Combine(Folder(kind), id + ".json")); }
    public static string NewId() => Guid.NewGuid().ToString("N");
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Packages.Json), Packages.Json)!;
}
public sealed class Reading
{
    [System.Text.Json.Serialization.JsonIgnore] public bool? DisplayHdrContext { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayText => Display(DisplayHdrContext);
    public Capability Capability { get; set; } = new();
    public ControlResult Result { get; set; } = new();
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
    public string Display(bool? hdr)
    {
        if (Capability.Problem.Length > 0) return Capability.Problem;
        if (Result.Status == "mismatch" && Result.Current.HasValue) return $"当前 {Result.Current} · 设置未确认";
        if (Result.Status != "ok") return "读取失败 · " + Result.Message;
        if (Result.Text != null) return Result.Text;
        if (Result.Current == null) return "无返回值";
        var f = Capability.Definition;
        ValueRule? rule;
        try { rule = ProtocolCodec.Rule(f, Result.Maximum ?? 0, hdr); } catch { return "条件矛盾"; }
        string? id = rule?.Values.FirstOrDefault(x => x.Value == Result.Current).Key;
        if (f.Id == "mccs-version") return $"MCCS {Result.Current >> 8}.{Result.Current & 255}";
        if (id != null) return rule!.Labels.GetValueOrDefault(id, id);
        if (VcpRecognition.Display(f, Result) is { } recognized) return recognized;
        if (f.ValueType == "boolean" && Result.Current is 0 or 1) return Result.Current == 0 ? "关闭" : "开启";
        return f.ValueType is "enum" or "status" ? $"名称未确认（编码 {Result.Current} / 0x{Result.Current:X2}）" : Result.Current + (f.Unit.Length == 0 ? "" : " " + f.Unit);
    }
    public string Semantic(bool? hdr)
    {
        var rule = ProtocolCodec.Rule(Capability.Definition, Result.Maximum ?? 0, hdr);
        return rule?.Values.FirstOrDefault(x => x.Value == Result.Current).Key ?? Result.Current?.ToString() ?? Result.Text ?? "";
    }
}
public sealed class Snapshot
{
    public string Id { get; set; } = Library.NewId();
    public string Name { get; set; } = "快照";
    public MonitorData Device { get; set; } = new();
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
    public List<Reading> Readings { get; set; } = [];
    public bool Complete { get; set; } = true;
    public override string ToString() => $"{Name} · {Time:MM-dd HH:mm:ss} · {Device.Name}";
}
public sealed class Difference
{
    public string Feature { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string Evidence { get; set; } = "";
}
public static class Comparison
{
    public static List<Difference> Between(Snapshot a, Snapshot b)
    {
        var result = new List<Difference>();
        foreach (string id in a.Readings.Select(r => r.Capability.Definition.Id).Union(b.Readings.Select(r => r.Capability.Definition.Id)))
        {
            var x = a.Readings.SingleOrDefault(r => r.Capability.Definition.Id == id); var y = b.Readings.SingleOrDefault(r => r.Capability.Definition.Id == id);
            if (x == null || y == null || x.Result.Status != y.Result.Status || x.Result.Current != y.Result.Current || x.Result.Maximum != y.Result.Maximum || x.Result.Text != y.Result.Text || !DisplayWorkspace.SameCapability(x.Capability, y.Capability))
                result.Add(new() { Feature = y?.Capability.Definition.Name ?? x!.Capability.Definition.Name, Before = x?.Display(a.Device.WindowsHdrEnabled) ?? "未采集", After = y?.Display(b.Device.WindowsHdrEnabled) ?? "未采集", Evidence = $"范围 {x?.Result.Maximum} → {y?.Result.Maximum}；状态 {x?.Result.Status ?? "缺失"} → {y?.Result.Status ?? "缺失"}；提供者 {x?.Capability.Provider} → {y?.Capability.Provider}" });
        }
        if (a.Device.WindowsHdrEnabled != b.Device.WindowsHdrEnabled) result.Add(new() { Feature = "Windows HDR（系统状态）", Before = a.Device.Hdr, After = b.Device.Hdr, Evidence = "条件发生变化，不能直接将关联值命名为 HDR" });
        return result;
    }
}
public sealed class ExperimentSegment
{
    public string Name { get; set; } = "基线 A";
    public bool Transition { get; set; }
    public List<Snapshot> Rounds { get; set; } = [];
    public override string ToString() => $"{Name} · {Rounds.Count(r => r.Complete)} 完整轮 / {Rounds.Count(r => !r.Complete)} 未完成轮{(Transition ? " · 过渡（排除）" : "")}";
}
public sealed class Experiment
{
    public string Id { get; set; } = Library.NewId();
    public string Question { get; set; } = "菜单切换观察";
    public MonitorData Device { get; set; } = new();
    public List<string> FeatureIds { get; set; } = [];
    public List<ExperimentSegment> Segments { get; set; } = [];
    public bool Expanded { get; set; }
    public List<string> MarkerRevisions { get; set; } = [];
    public override string ToString() => Question + " · " + Segments.Count + " 段 · " + Device.Name;
    public List<Difference> Compare(int first, int second)
    {
        var a = Segments[first]; var b = Segments[second];
        if (a.Transition || b.Transition || !a.Rounds.Any(r => r.Complete) || !b.Rounds.Any(r => r.Complete)) throw new InvalidOperationException("请选择有完整有效轮次的稳定段");
        var diff = Comparison.Between(a.Rounds.Last(r => r.Complete), b.Rounds.Last(r => r.Complete));
        foreach (var row in diff)
        {
            bool stable = new[] { a, b }.All(s => s.Rounds.Where(r => r.Complete).SelectMany(r => r.Readings.Where(x => x.Capability.Definition.Name == row.Feature)).Select(x => (x.Result.Status, x.Result.Current, x.Result.Maximum, x.Result.Text)).Distinct().Count() == 1);
            row.Evidence += stable ? $"；段内稳定（{a.Rounds.Count}/{b.Rounds.Count}轮），仅为关联证据" : "；段内不稳定，不能确认关联";
        }
        return diff;
    }
}
public sealed class Operation
{
    public string Id { get; set; } = Library.NewId();
    public string Name { get; set; } = "";
    public Snapshot Before { get; set; } = new();
    public Snapshot After { get; set; } = new();
    public List<ControlRequest> Requests { get; set; } = [];
    public List<ControlResult> Results { get; set; } = [];
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
    public override string ToString() => $"{Time:MM-dd HH:mm:ss} · {Name} · {Results.Count(x => x.Confirmed)}/{Requests.Count} 已确认";
}
public sealed class PresetItem
{
    public string FeatureId { get; set; } = "";
    public string Value { get; set; } = "";
    public string Provider { get; set; } = "standard";
    public string Hash { get; set; } = "builtin";
    public bool Required { get; set; } = true;
}
public sealed class Preset
{
    public string Id { get; set; } = Library.NewId();
    public string Name { get; set; } = "我的方案";
    public string DevicePath { get; set; } = "";
    public List<PresetItem> Items { get; set; } = [];
    public override string ToString() => Name + " · " + Items.Count + " 项";
}
public sealed class PlanItem
{
    public PresetItem Item { get; set; } = new();
    public Reading? Current { get; set; }
    public string Problem { get; set; } = "";
    public override string ToString() => $"{Current?.Capability.Definition.Name ?? Item.FeatureId}：{Current?.Result.Current} → {Item.Value}　{Problem}";
}
public sealed class NavigationContext
{
    public string Id { get; set; } = Library.NewId();
    public MonitorData Device { get; set; } = new();
    public string FeatureId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Page { get; set; } = "control";
    public string? SessionId { get; set; }
    public string? OperationId { get; set; }
}
public interface IDisplayBackend
{
    Task<List<MonitorData>> Devices(CancellationToken token);
    List<Capability> Capabilities(MonitorData device);
    Task<ControlResult> Execute(ControlRequest request, CancellationToken token);
}
public sealed class SystemDisplayBackend(string worker) : IDisplayBackend, IBatchDisplayBackend
{
    public Task<List<MonitorData>> Devices(CancellationToken token) => Task.Run(Native.Enumerate, token);
    public List<Capability> Capabilities(MonitorData device) => CapabilityCatalog.Resolve(device);
    public Task<ControlResult> Execute(ControlRequest request, CancellationToken token) => ProcessClient.Run(worker, request, token: token, worker: true);
    public Task<Snapshot> ReadBatch(MonitorData device, List<string>? scope, CancellationToken token) => BatchClient.Read(worker, device, scope, token);
}
public sealed partial class DisplayWorkspace(IDisplayBackend backend)
{
    public IDisplayBackend Backend { get; } = backend;
    public static string Conditions(IEnumerable<FeatureCondition> conditions, Snapshot? snapshot)
    {
        foreach (var condition in conditions) {
            var value = snapshot?.Readings.SingleOrDefault(r => r.Capability.Definition.Id == condition.FeatureId);
            if (value?.Result.Status != "ok" || value.Result.Current == null) return "条件未知：需读取 " + condition.FeatureId;
            if (value.Result.Current != condition.EqualsValue) return condition.Reason.Length == 0 ? "当前状态不满足条件：" + condition.FeatureId : condition.Reason;
        }
        return "";
    }
    public async Task<Snapshot> Capture(MonitorData device, CancellationToken token, Action<Reading>? progress = null, IEnumerable<string>? scope = null)
    {
        if (Backend is IBatchDisplayBackend batch) { var result = await batch.ReadBatch(device, scope?.ToList(), token); foreach (var reading in result.Readings) progress?.Invoke(reading); return result; }
        var fresh = (await Backend.Devices(token)).SingleOrDefault(x => HardwareReports.SameDevice(x, device)) ?? throw new IOException("设备已断开或连接变化；资料已保留");
        var snapshot = new Snapshot { Device = Library.Copy(fresh) };
        var capabilities = await Task.Run(() => Backend.Capabilities(fresh), token);
        foreach (var capability in capabilities.Where(c => scope == null || scope.Contains(c.Definition.Id)))
        {
            token.ThrowIfCancellationRequested();
            ControlResult result;
            if (capability.Problem.Length > 0) result = new() { Status = capability.Availability, Message = capability.Problem };
            else if (!capability.Definition.Readable || capability.Definition.ValueType == "action") result = new() { Status = "unsupported", Message = "仅写入或执行，不提供回读" };
            else try { result = await Backend.Execute(Request(fresh, capability, "read"), token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result = new() { Message = ex.Message }; }
            var reading = new Reading { Capability = capability, Result = result }; snapshot.Readings.Add(reading); progress?.Invoke(reading);
        }
        return snapshot;
    }
    public static ControlRequest Request(MonitorData device, Capability c, string action, string? value = null) => new() { Device = Library.Copy(device), ControlId = c.Definition.Id, PackageId = c.Provider == "standard" ? null : c.Provider, ExpectedHash = c.Hash, Action = action, Value = value };
    public static List<PlanItem> Preflight(Preset preset, Snapshot now)
    {
        var safety = HdrSafety.Policy(now.Device);
        if (preset.Items.Count is 0 or > 128 || preset.Items.Select(i => i.FeatureId).Distinct().Count() != preset.Items.Count) throw new InvalidOperationException("方案项目为空、过多或功能重复");
        if (preset.DevicePath != now.Device.DevicePath) throw new InvalidOperationException("方案属于另一台显示器；请先复制并核对目标设备");
        var plans = preset.Items.Select(item => {
            var reading = now.Readings.SingleOrDefault(x => x.Capability.Definition.Id == item.FeatureId);
            string problem = "";
            try {
                if (reading == null) throw new InvalidOperationException("功能缺失");
                if (reading.Capability.Provider != item.Provider || reading.Capability.Hash != item.Hash) throw new InvalidOperationException("提供者或版本内容已变化，需重新保存方案");
                if (reading.Capability.Problem.Length > 0) throw new InvalidOperationException(reading.Capability.Problem);
                var f = reading.Capability.Definition;
                string interlock = HdrSafety.Problem(safety, HdrSafety.ProtectedId(safety, f), now); if (interlock.Length > 0) throw new InvalidOperationException(interlock);
                if (!f.Writable) throw new InvalidOperationException("只读功能");
                string condition = Conditions(f.WritableWhen, now); if (condition.Length > 0) throw new InvalidOperationException(condition);
                if (f.Readable && reading.Result.Status != "ok") throw new InvalidOperationException("当前读取失败：" + reading.Result.Message);
                if (f.ValueType is not ("action" or "text")) ProtocolCodec.Value(f, item.Value, reading.Result.Maximum ?? ushort.MaxValue, now.Device.WindowsHdrEnabled);
            } catch (Exception ex) { problem = ex.Message; }
            return new PlanItem { Item = item, Current = reading, Problem = problem };
        }).ToList();
        var ordered = new List<PlanItem>(); var pending = plans.ToList();
        while (pending.Count > 0) {
            var next = pending.FirstOrDefault(p => (p.Current?.Capability.Definition.WriteAfter ?? []).All(id => pending.All(x => x.Item.FeatureId != id)));
            if (next == null) throw new InvalidOperationException("方案依赖顺序存在环路");
            ordered.Add(next); pending.Remove(next);
        }
        return ordered;
    }
    public static string? PreflightFailure(IEnumerable<PlanItem> plans)
    {
        var failures = plans.Where(p => p.Item.Required && p.Problem.Length > 0).Select(p => $"{p.Current?.Capability.Definition.Name ?? p.Item.FeatureId}：{p.Problem}").ToList();
        return failures.Count == 0 ? null : "未发送任何设置。" + string.Join("；", failures);
    }
    public async Task<Operation> Apply(string name, Snapshot before, List<PlanItem> plans, CancellationToken token)
    {
        if (PreflightFailure(plans) is { } failure) throw new InvalidOperationException(failure);
        var op = new Operation { Name = name, Before = Library.Copy(before), After = Library.Copy(before) };
        Library.Save("operations", op.Id, op);
        foreach (var plan in plans.Where(p => p.Problem.Length == 0))
        {
            token.ThrowIfCancellationRequested();
            var current = Merge(op.After, await Capture(before.Device, token, scope: WriteScope(plan.Current!.Capability.Definition)));
            var check = Preflight(new Preset { DevicePath = before.Device.DevicePath, Items = [plan.Item] }, current).Single();
            if (check.Problem.Length > 0) { op.Name += "（执行前条件变化，已停止）"; break; }
            var c = check.Current!.Capability;
            var request = Request(current.Device, c, "write", plan.Item.Value); op.Requests.Add(request);
            Library.Save("operations", op.Id, op);
            ControlResult result;
            try { result = await Backend.Execute(request, token); }
            catch (Exception ex) { result = new() { RequestId = request.RequestId, Status = "unknown", Message = "结果未知；请核查，不要直接重发。" + ex.Message }; }
            op.Results.Add(result);
            UpdateFromWrite(op.Before, op.After, c, result);
            Library.Save("operations", op.Id, op);
            if (result.Status != "ok" || !result.Confirmed && c.Definition.Readable && c.Definition.ValueType != "action") break;
        }
        var affected = plans.Where(p => p.Current != null).SelectMany(p => AffectedScope(p.Current!.Capability.Definition)).Distinct().ToList();
        if (affected.Count > 0) try { op.After = Merge(op.After, await Capture(before.Device, token, scope: affected)); } catch (Exception ex) { op.After.Name = "联动核查未完成：" + ex.Message; }
        Library.Save("operations", op.Id, op); return op;
    }
    public static Preset Recovery(Operation op, Snapshot now)
    {
        var preset = new Preset { Name = "恢复 " + op.Name, DevicePath = now.Device.DevicePath };
        if (!HardwareReports.SameDevice(op.Before.Device, now.Device) || !now.Complete) throw new InvalidOperationException("恢复设备不匹配或核查未完成");
        foreach (var request in op.Requests) {
            var result = op.Results.SingleOrDefault(x => x.RequestId == request.RequestId);
            if (result?.Confirmed != true) continue;
            var old = op.Before.Readings.SingleOrDefault(r => r.Capability.Definition.Id == request.ControlId);
            var current = now.Readings.SingleOrDefault(r => r.Capability.Definition.Id == request.ControlId);
            if (old == null || old.Result.Status != "ok" || current?.Result.Status != "ok") throw new InvalidOperationException("恢复范围存在缺失读数，请先核查");
            if (!SameCapability(current.Capability, old.Capability) || current.Result.Current != result.Current || current.Result.Maximum != result.Maximum || now.Device.WindowsHdrEnabled != op.After.Device.WindowsHdrEnabled) throw new InvalidOperationException("恢复冲突：当前状态/条件/适配包已变化，不覆盖外部调整");
            preset.Items.Add(new() { FeatureId = request.ControlId, Value = old.Semantic(op.Before.Device.WindowsHdrEnabled), Provider = old.Capability.Provider, Hash = old.Capability.Hash });
        }
        if (preset.Items.Count == 0) throw new InvalidOperationException("没有可确认恢复的项目；先重新读取核查");
        return preset;
    }
    public static void ExportExperiment(Experiment experiment, string file, bool anonymize)
    {
        var copy = Library.Copy(experiment);
        if (anonymize) {
            void Scrub(MonitorData d) { d.DevicePath = "redacted"; d.Device = "redacted"; d.Edid.Serial = "redacted"; d.Edid.RawHex = ""; }
            Scrub(copy.Device); foreach (var round in copy.Segments.SelectMany(s => s.Rounds)) Scrub(round.Device);
        }
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create); using var output = new StreamWriter(zip.CreateEntry("experiment.json").Open()); output.Write(JsonSerializer.Serialize(copy, Packages.Json));
    }
}

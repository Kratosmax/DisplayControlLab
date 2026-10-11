using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MonitorInspector;

public sealed class AdapterPackage
{
    public int ApiVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Kind { get; set; } = "protocol";
    public DeviceMatch Match { get; set; } = new();
    public string Backend { get; set; } = "standard-ddc";
    public List<string> OverridesStandard { get; set; } = [];
    public string? EntryPoint { get; set; }
    public List<string> Dependencies { get; set; } = [];
    public List<ControlDefinition> Controls { get; set; } = [];
    public List<MenuHint> MenuHints { get; set; } = [];
    public HdrSafetyPolicy? HdrSafety { get; set; }
    [JsonIgnore] public string DirectoryPath { get; set; } = "";
    public override string ToString() => $"{Name} · {Version} [{Kind}]";
}
public sealed class MenuHint
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "device";
    public string MenuPath { get; set; } = "";
    public string Description { get; set; } = "";
    public string HelpText { get; set; } = "";
}
public sealed class DeviceMatch
{
    public string Manufacturer { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Matches(MonitorData m) => Manufacturer == m.Edid.Manufacturer && ProductCode.Equals(m.Edid.Product, StringComparison.OrdinalIgnoreCase) && (Name.Length == 0 || Name == m.Name);
}
public sealed class ControlDefinition
{
    public string? Backend { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Writable { get; set; }
    public bool Readable { get; set; } = true;
    public string Unit { get; set; } = "";
    public string MenuPath { get; set; } = "";
    public string Description { get; set; } = "";
    public string HelpText { get; set; } = "";
    public uint Minimum { get; set; }
    public uint Step { get; set; } = 1;
    public int StabilizationMs { get; set; }
    public List<string> AffectedFeatures { get; set; } = [];
    public List<string> WriteAfter { get; set; } = [];
    public List<FeatureCondition> VisibleWhen { get; set; } = [];
    public List<FeatureCondition> ReadableWhen { get; set; } = [];
    public List<FeatureCondition> WritableWhen { get; set; } = [];
    public string ValueType { get; set; } = "number";
    public string Group { get; set; } = "image";
    public string Verification { get; set; } = "declared";
    public List<uint>? AllowedWriteValues { get; set; }
    public bool? WriteWhenWindowsHdr { get; set; }
    public byte? VcpCode { get; set; }
    public RawProtocol? Raw { get; set; }
    public List<ValueRule> Rules { get; set; } = [];
    public override string ToString() => Name;
}
public sealed class FeatureCondition
{
    public string FeatureId { get; set; } = "";
    public uint EqualsValue { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class ValueRule
{
    public bool MatchAnyMaximum { get; set; }
    public uint Maximum { get; set; }
    public bool? WindowsHdr { get; set; }
    public Dictionary<string, uint> Values { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
}
public sealed class RawProtocol
{
    public string ReadPayload { get; set; } = "";
    public string WritePrefix { get; set; } = "";
    public int ReplyLength { get; set; } = 11;
    public string ReplyPrefix { get; set; } = "";
    public int CurrentOffset { get; set; } = 8;
    public int MaximumOffset { get; set; } = 6;
    public byte ChecksumSeed { get; set; } = 80;
}
public static partial class Packages
{
    public static string BackendFor(AdapterPackage? package, ControlDefinition control) => control.Backend ?? package?.Backend ?? "standard-ddc";
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public static string Home { get; } = Path.GetFullPath(Environment.GetEnvironmentVariable("DISPLAYCONTROL_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayControlLab"));
    private static string Installed => Path.Combine(Home, "adapters");
    public static byte[] Hex(string text) => Convert.FromHexString(text.Replace(" ", ""));
    public static AdapterPackage Parse(string text)
    {
        if (text.Length > 1024 * 1024) throw new InvalidDataException("协议定义超过 1 MB");
        using var document = JsonDocument.Parse(text);
        CheckDuplicateKeys(document.RootElement);
        var p = JsonSerializer.Deserialize<AdapterPackage>(text, Json) ?? throw new InvalidDataException("空适配包");
        // v1 named rules predate valueType. Their write contract has always required
        // a mode key, so render those legacy definitions as options, not a slider.
        if (p.Controls != null && document.RootElement.TryGetProperty("controls", out var controls) && controls.ValueKind == JsonValueKind.Array)
            foreach (var entry in controls.EnumerateArray().Select((json, index) => (json, index)))
                if (entry.json.ValueKind == JsonValueKind.Object && !entry.json.TryGetProperty("valueType", out _) && p.Controls[entry.index] is { Rules.Count: > 0 } feature) feature.ValueType = "enum";
        Validate(p); return p;
    }
    private static void CheckDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>();
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException("重复 JSON 字段：" + property.Name);
                CheckDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicateKeys(item);
    }
    private static AdapterPackage Load(string file)
    {
        if (new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("协议定义超过 1 MB");
        return Parse(File.ReadAllText(file));
    }
    public static void Validate(AdapterPackage p)
    {
        if (p.OverridesStandard == null || p.OverridesStandard.Distinct().Count() != p.OverridesStandard.Count) throw new InvalidDataException("标准覆盖声明无效");
        if (p.Match == null || p.Controls == null || p.Dependencies == null || p.MenuHints == null || p.Id == null || p.Kind == null || p.Backend == null || p.Match.Manufacturer == null || p.Match.ProductCode == null || p.Match.Name == null) throw new InvalidDataException("必需字段不能为 null");
        if (p.Controls.Any(f => f == null) || p.MenuHints.Any(h => h == null || string.IsNullOrWhiteSpace(h.Id) || string.IsNullOrWhiteSpace(h.Name))) throw new InvalidDataException("功能或菜单线索无效");
        if (p.ApiVersion != 1) throw new InvalidDataException("适配接口版本不兼容");
        if (!Regex.IsMatch(p.Id, "^[a-z][a-z0-9.-]{2,79}$") || p.Id.EndsWith('.') || p.Id.Contains("..")) throw new InvalidDataException("适配包 id 无效");
        if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Author) || !System.Version.TryParse(p.Version, out _)) throw new InvalidDataException("名称、作者或版本无效");
        if (p.Kind is not ("protocol" or "plugin")) throw new InvalidDataException("未知适配包类型");
        if (p.Match.Manufacturer.Length != 3 || !Regex.IsMatch(p.Match.ProductCode, "^0x[0-9A-Fa-f]{4}$")) throw new InvalidDataException("必须指定 EDID 厂商和产品代码");
        if (p.Controls.Count is 0 or > 128 || p.Controls.Select(x => x.Id).Distinct().Count() != p.Controls.Count) throw new InvalidDataException("功能为空、过多或重复");
        if (p.HdrSafety is { } safety && (string.IsNullOrWhiteSpace(safety.ModeFeatureId) || safety.BlockedFeatures == null || safety.BlockedFeatures.Count is 0 or > 128 || safety.BlockedFeatures.Any(id => string.IsNullOrWhiteSpace(id) || id == safety.ModeFeatureId) || safety.BlockedFeatures.Distinct().Count() != safety.BlockedFeatures.Count)) throw new InvalidDataException("HDR 互锁规则无效");
        if (p.Kind == "plugin")
        {
            if (string.IsNullOrWhiteSpace(p.EntryPoint) || !p.EntryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("代码插件必须指定独立 exe 入口");
            ContainedPath(Path.GetTempPath(), p.EntryPoint);
        }
        else if (p.Backend is not ("standard-ddc" or "amd-ddc")) throw new InvalidDataException("协议包通信后端不支持；专用驱动请用代码插件");
        foreach (var f in p.Controls)
        {
            if (f == null || f.Rules == null || f.VisibleWhen == null || f.ReadableWhen == null || f.WritableWhen == null || f.WriteAfter == null || f.AffectedFeatures == null || f.Id == null || f.Group == null || f.MenuPath == null || f.Unit == null) throw new InvalidDataException("功能字段不能为 null");
            if (f.Rules.Any(r => r == null || r.Values == null || r.Labels == null) || f.VisibleWhen.Concat(f.ReadableWhen).Concat(f.WritableWhen).Any(c => c == null)) throw new InvalidDataException("条件字段不能为 null");
            if (!Regex.IsMatch(f.Id, "^[a-z][a-z0-9.-]{1,79}$") || string.IsNullOrWhiteSpace(f.Name)) throw new InvalidDataException("功能标识或名称无效");
            if (f.ValueType is not ("number" or "enum" or "boolean" or "status" or "text" or "action")) throw new InvalidDataException("未知功能值类型");
            if (f.StabilizationMs is < 0 or > 5000) throw new InvalidDataException("稳定等待超出 0–5000 ms");
            if (f.Step == 0 || p.Kind == "protocol" && (!f.Readable || f.ValueType is "text" or "action")) throw new InvalidDataException("该能力需要代码插件或有效步长");
            if (f.Writable && f.ValueType == "status") throw new InvalidDataException("状态类型只读");
            if (f.VisibleWhen.Concat(f.ReadableWhen).Concat(f.WritableWhen).Any(c => c.FeatureId == f.Id || string.IsNullOrWhiteSpace(c.FeatureId))) throw new InvalidDataException("条件引用自身或缺少功能 id");
            if (f.Verification is not ("declared" or "read-observed" or "write-confirmed")) throw new InvalidDataException("验证级别无效");
            if (f.AllowedWriteValues is { } allowed && (allowed.Count == 0 || allowed.Distinct().Count() != allowed.Count)) throw new InvalidDataException("可写取值白名单为空或重复");
            if (p.Kind == "protocol")
            {
                string backend = BackendFor(p, f);
                if (backend is not ("standard-ddc" or "amd-ddc")) throw new InvalidDataException("功能通信后端尚未支持");
                if ((f.VcpCode != null) == (f.Raw != null)) throw new InvalidDataException("每项功能只能指定一种协议");
                if (backend == "standard-ddc" && f.VcpCode == null || backend == "amd-ddc" && f.Raw == null) throw new InvalidDataException("协议与通信后端不符");
                if (f.VcpCode.HasValue && Native.SkipQuery(f.VcpCode.Value)) throw new InvalidDataException("不允许声明恢复/命令类项目");
                if (f.Raw is { } r)
                {
                    var read = Hex(r.ReadPayload); var prefix = Hex(r.ReplyPrefix);
                    if (read.Length is < 2 or > 32 || read[0] != 1 || r.ReplyLength is < 6 or > 64 || prefix.Length < 4 || prefix.Length > r.ReplyLength - 1) throw new InvalidDataException("读取报文或响应前缀无效");
                    if (r.CurrentOffset < prefix.Length || r.MaximumOffset < prefix.Length || r.CurrentOffset + 2 >= r.ReplyLength || r.MaximumOffset + 2 >= r.ReplyLength || Math.Abs(r.CurrentOffset - r.MaximumOffset) < 2) throw new InvalidDataException("响应字段越界或重叠");
                    if (f.Writable && (Hex(r.WritePrefix).Length is < 2 or > 32 || Hex(r.WritePrefix)[0] != 3)) throw new InvalidDataException("写入报文无效");
                }
            }
            if (f.Rules.Select(x => (x.Maximum, x.WindowsHdr)).Distinct().Count() != f.Rules.Count) throw new InvalidDataException("条件规则重复");
            foreach (var rule in f.Rules)
                if (rule.Values.Count == 0 || rule.Labels.Keys.Any(id => !rule.Values.ContainsKey(id)) || rule.Values.Values.Distinct().Count() != rule.Values.Count || rule.Values.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value > ushort.MaxValue || f.ValueType == "number" && x.Value > rule.Maximum || f.ValueType == "boolean" && x.Value > 1)) throw new InvalidDataException("模式取值重复、显示名引用无效或超出范围");
        }
        foreach (string id in p.OverridesStandard) {
            var control = p.Controls.SingleOrDefault(c => c.Id == id);
            var standard = DeviceControl.Standard.SingleOrDefault(c => c.Id == id);
            if (p.Kind != "protocol" || control == null || standard == null || BackendFor(p, control) != "standard-ddc" || control.VcpCode != standard.VcpCode) throw new InvalidDataException("默认标准覆盖必须引用相同VCP的标准功能");
        }
        var edges = p.Controls.ToDictionary(f => f.Id, f => f.WriteAfter.Concat(f.ReadableWhen.Select(c => c.FeatureId)).Concat(f.WritableWhen.Select(c => c.FeatureId)).ToList());
        while (edges.Count > 0) { var ready = edges.FirstOrDefault(e => e.Value.All(id => !edges.ContainsKey(id))); if (ready.Key == null) throw new InvalidDataException("功能条件或顺序依赖存在环路"); edges.Remove(ready.Key); }
    }
    public static string ContainedPath(string directory, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\\') || relative.Split('/').Any(x => x is "" or "." or "..")) throw new InvalidDataException("包内路径无效");
        var path = Path.GetFullPath(Path.Combine(directory, relative));
        if (!path.StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("包路径越界");
        return path;
    }
    public static List<AdapterPackage> List()
    {
        if (!Directory.Exists(Installed)) return [];
        var packages = new List<AdapterPackage>();
        foreach (var x in Directory.GetDirectories(Installed).Where(x => !Path.GetFileName(x).StartsWith('.')))
        {
            try {
            var p = Load(Path.Combine(x, "adapter.json"));
            if (p.Id != Path.GetFileName(x)) throw new InvalidDataException("安装目录与包 id 不符");
            p.DirectoryPath = x; packages.Add(p);
            } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { }
        }
        return packages.OrderBy(x => x.Name).ToList();
    }
    public static AdapterPackage Install(string source, bool replace = false)
    {
        using var gate = Gate();
        Directory.CreateDirectory(Installed);
        string stage = Path.Combine(Installed, ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { Load(source); File.Copy(source, Path.Combine(stage, "adapter.json")); }
            else
            {
                using var zip = ZipFile.OpenRead(source);
                if (zip.Entries.Count > 512 || zip.Entries.Sum(x => x.Length) > 64 * 1024 * 1024) throw new InvalidDataException("适配包超过大小限制");
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    string file = ContainedPath(stage, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!); entry.ExtractToFile(file);
                }
            }
            var p = Load(Path.Combine(stage, "adapter.json"));
            if (p.Kind == "plugin" && !File.Exists(ContainedPath(stage, p.EntryPoint!))) throw new InvalidDataException("插件入口文件缺失；代码插件需导入完整 ZIP");
            string target = Path.Combine(Installed, p.Id), backup = stage + ".backup";
            if (Directory.Exists(target) && !replace) throw new IOException("同 id 已安装；请编辑为用户副本，或明确选择替换");
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(stage, target); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, target); throw; }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            p.DirectoryPath = target; return p;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public static void Uninstall(AdapterPackage package)
    {
        using var gate = Gate();
        Validate(package);
        var path = Path.Combine(Installed, package.Id);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Mutate(state => {
        foreach (var key in state.Bindings.Where(x => x.Value == package.Id).Select(x => x.Key).ToList()) state.Bindings.Remove(key);
        foreach (var ids in state.DevicePackages.Values) ids.Remove(package.Id);
        foreach (var key in state.FeatureRoutes.Where(x => x.Value == package.Id).Select(x => x.Key).ToList()) state.FeatureRoutes.Remove(key);
        state.TrustedHashes.Remove(package.Id);
        state.Disabled.Remove(package.Id);
        });
    }
    public static void Export(AdapterPackage package, string destination) => ZipFile.CreateFromDirectory(package.DirectoryPath, destination);
    public static void Enable(string id, bool enabled)
    {
        Mutate(state => { state.Disabled.Remove(id); if (!enabled) state.Disabled.Add(id); });
    }
    public static void Bind(MonitorData monitor, string? id)
    {
        if (id == null) Mutate(state => { state.Bindings.Remove(monitor.DevicePath); state.DevicePackages.Remove(monitor.DevicePath); });
        else Attach(monitor, List().Single(p => p.Id == id), false);
    }
    public static PackageState State() => File.Exists(Path.Combine(Home, "state.json")) ? JsonSerializer.Deserialize<PackageState>(File.ReadAllText(Path.Combine(Home, "state.json")), Json) ?? new() : new();
    private static void SaveState(PackageState state)
    {
        Directory.CreateDirectory(Home); var path = Path.Combine(Home, "state.json");
        string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(stage, JsonSerializer.Serialize(state, Json)); File.Move(stage, path, true);
    }
}
public sealed class PackageState
{
    public Dictionary<string, string> Bindings { get; set; } = [];
    public HashSet<string> Disabled { get; set; } = [];
    public Dictionary<string, List<string>> DevicePackages { get; set; } = [];
    public Dictionary<string, string> FeatureRoutes { get; set; } = [];
    public Dictionary<string, string> TrustedHashes { get; set; } = [];
}

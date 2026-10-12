using System.Globalization;
using System.Text.Json;

namespace MonitorInspector;

public static class VcpRecognition
{
    sealed class Catalog
    {
        public string Version { get; set; } = "";
        public string StandardSource { get; set; } = "";
        public List<Entry> Standards { get; set; } = [];
        public List<Entry> Vendors { get; set; } = [];
    }
    sealed class Entry
    {
        public byte Code { get; set; }
        public string Name { get; set; } = "";
        public string Group { get; set; } = "information";
        public bool Manual { get; set; }
        public uint MinimumMccs { get; set; } = 512;
        public string Manufacturer { get; set; } = "";
        public Dictionary<string, string> Labels { get; set; } = [];
        public string Source { get; set; } = "";
        public string Scope { get; set; } = "";
        public bool PackedInput { get; set; }
    }
    static readonly Catalog Data = Load();
    static Catalog Load()
    {
        using var stream = typeof(VcpRecognition).Assembly.GetManifestResourceStream("DisplayControl.VcpRecognition.json")!;
        var data = JsonSerializer.Deserialize<Catalog>(stream, Packages.Json)!;
        if (data.Standards.Select(e => e.Code).Distinct().Count() != data.Standards.Count
            || data.Vendors.Select(e => (e.Manufacturer, e.Code)).Distinct().Count() != data.Vendors.Count
            || data.Standards.Any(e => Native.SkipQuery(e.Code) || string.IsNullOrWhiteSpace(e.Name))
            || data.Vendors.Any(e => Native.SkipQuery(e.Code) || e.Manual || e.Source.Length == 0)) throw new InvalidDataException("内置识别目录无效");
        return data;
    }
    public static int StandardCount => Data.Standards.Count;
    public static string? Name(byte code) => Data.Standards.FirstOrDefault(e => e.Code == code)?.Name;
    public static bool IsNumericImage(byte code) => Data.Standards.Any(e => e.Code == code && e.Manual);
    public static void Recognize(ReaderSurvey report)
    {
        foreach (var item in report.Features.Where(f => f.Backend == "standard-ddc" && f.Definition.VcpCode.HasValue)) {
            var f = item.Definition; byte code = f.VcpCode!.Value;
            var origin = report.Rounds.FirstOrDefault()?.Readings.SingleOrDefault(r => r.Capability.Definition.Id == f.Id)?.Capability.Provider;
            bool builtin = origin == "standard" || origin?.StartsWith("user.detected-", StringComparison.Ordinal) == true;
            // User/model definitions always win, including explicit read-only overrides.
            if (!builtin && f.Id != $"vcp.{code:x2}") continue;
            if (f.Id == $"vcp.{code:x2}" && origin != null && !builtin) continue;
            if (builtin && DeviceControl.Standard.Any(c => c.Id == f.Id && c.VcpCode == code)) item.Recognition = "公开标准";
            var standard = Data.Standards.FirstOrDefault(e => e.Code == code);
            var vendor = Data.Vendors.FirstOrDefault(e => e.Manufacturer == report.Device.Edid.Manufacturer && e.Code == code);
            if (f.Id == $"vcp.{code:x2}" && standard == null && vendor == null) item.Recognition = "用途未知";
            if (f.Id == $"vcp.{code:x2}" && standard != null) {
                item.Recognition = "公开标准";
                f.Name = standard.Name; f.Group = standard.Group; f.ValueType = "status"; f.Writable = false;
                f.HelpText = $"公开 MCCS 定义（识别目录 {Data.Version}）。厂商实际实现可能不同。来源：{Data.StandardSource}";
                if (standard.Labels.Count > 0) SetLabels(f, standard.Labels);
                uint? version = report.Features.FirstOrDefault(x => x.Definition.Id == "mccs-version" && x.ReadStatus == "读取已验证")?.Current;
                bool declared = report.Discovery?.Features.Any(r => r.Code.Equals($"0x{code:X2}", StringComparison.OrdinalIgnoreCase) && r.Declared == "显示器声明") == true;
                if (standard.Manual && declared && version >= standard.MinimumMccs && item.ReadStatus == "读取已验证"
                    && item.Samples.All(r => r.Status == "ok" && r.Current.HasValue && r.Maximum is > 0 and <= ushort.MaxValue && r.Current <= r.Maximum)) {
                    f.ValueType = "number"; f.Writable = true; f.WriteWhenWindowsHdr = false;
                }
                item.Note = f.HelpText + (f.Writable ? "；声明与范围有效，可手动调节，未试写不代表写入已验证" : "；只读识别，不推导未知或复合编码的写入");
            }
            if (vendor == null) continue;
            if (vendor.PackedInput) {
                if (item.Current is not uint value || value >> 8 is not (15 or 17)) continue;
                string? port = StandardOptions.Labels("input-source").GetValueOrDefault(StandardOptions.Values("input-source").FirstOrDefault(v => v.Value == (value & 255)).Key ?? "");
                if (port == null && (value & 255) == 27) port = "USB-C";
                if (port == null) continue;
                AddObservedLabel(f, value, port + "（厂商候选）");
            } else if (f.Id == $"vcp.{code:x2}") {
                f.Name = vendor.Name + "（厂商候选）"; f.Group = vendor.Group; f.ValueType = "status"; f.Writable = false;
                SetLabels(f, vendor.Labels.ToDictionary(e => e.Key, e => e.Value + "（厂商候选）"));
            } else if (f.ValueType == "enum" && f.Rules.Count == 1 && f.Rules[0].MatchAnyMaximum && f.Rules[0].WindowsHdr == null) {
                var defaults = StandardOptions.Labels(f.Id);
                if (f.Rules[0].Values.Keys.Any(key => f.Rules[0].Labels.TryGetValue(key, out var label)
                    && label != defaults.GetValueOrDefault(key) && !label.EndsWith("（厂商候选）", StringComparison.Ordinal) && !label.Contains("名称未确认"))) {
                    item.Recognition = "适配包定义"; continue;
                }
                foreach (var option in f.Rules[0].Values)
                    if (vendor.Labels.TryGetValue(option.Value.ToString(CultureInfo.InvariantCulture), out string? label)) f.Rules[0].Labels[option.Key] = label + "（厂商候选）";
            } else continue;
            f.HelpText = $"厂商候选规则 {Data.Version}；本机菜单含义尚未验证。{vendor.Scope}。来源：{vendor.Source}";
            item.Recognition = "厂商候选";
            item.Note = f.HelpText;
        }
    }
    static void SetLabels(ControlDefinition f, Dictionary<string, string> labels)
    {
        var rule = new ValueRule { MatchAnyMaximum = true };
        foreach (var label in labels) { uint value = uint.Parse(label.Key, CultureInfo.InvariantCulture); string key = $"code-{value:x2}"; rule.Values[key] = value; rule.Labels[key] = label.Value; }
        f.Rules = [rule];
    }
    static void AddObservedLabel(ControlDefinition f, uint value, string label)
    {
        if (f.Rules.Count != 1 || !f.Rules[0].MatchAnyMaximum || f.Rules[0].WindowsHdr != null) return;
        string key = f.Rules[0].Values.FirstOrDefault(v => v.Value == value).Key ?? $"code-{value:x4}";
        f.Rules[0].Values[key] = value; f.Rules[0].Labels[key] = label;
    }
    public static string? Display(ControlDefinition f, ControlResult result)
    {
        if (result.Status != "ok" || result.Current is not uint value || f.VcpCode is not byte code
            || f.Id != $"vcp.{code:x2}" || f.ValueType != "status" || f.Rules.Count > 0 || Name(code) == null) return null;
        return code switch {
            0x0b => value is > 0 and <= 5000 ? $"{value} K / 步（标准解释）" : $"{value}（步进异常）",
            0x0c => $"步数 {value}；色温需结合 0x0B，不直接当 K 值",
            0x52 => value == 0 ? "没有新的控制变化" : $"最近变化码 0x{value & 255:X2}（标准解释）",
            0xae => $"{value / 100.0:0.##} Hz（标准解释）",
            0xac => $"原始编码 {value}（频率编码可能因厂商而异）",
            0xc0 => $"{value} 小时（MCCS 2.x 的 16 位解释）",
            0xc6 => $"0x{value:X4}",
            0xc9 => $"{value >> 8}.{value & 255}（原始 0x{value:X4}）",
            _ => $"{value}（标准码原始值）"
        };
    }
}

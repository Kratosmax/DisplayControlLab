using System.Security.Cryptography;
using System.Text;

namespace MonitorInspector;

public sealed class HdrSafetyPolicy
{
    public string ModeFeatureId { get; set; } = "hdr-preset";
    public uint OffValue { get; set; }
    public List<string> BlockedFeatures { get; set; } = [];
    public bool RestoreColorOnExit { get; set; } = true;
}
public sealed class HdrColorState
{
    public string Id { get; set; } = "";
    public MonitorData Device { get; set; } = new();
    public string ModeProvider { get; set; } = "";
    public string ModeHash { get; set; } = "";
    public bool Pending { get; set; }
    public List<Reading> Saved { get; set; } = [];
    public List<string> Results { get; set; } = [];
}
public static class HdrSafety
{
    public static HdrSafetyPolicy? Policy(MonitorData device, AdapterPackage? draft = null)
    {
        var policies = Packages.ForDevice(device).Where(p => p.HdrSafety != null).Select(p => p.HdrSafety!).ToList();
        if (draft?.HdrSafety != null) policies.Add(draft.HdrSafety);
        // This compatibility rule protects the reported model even with its old pack.
        if (device.Edid.Manufacturer == "AOC" && device.Edid.Product.Equals("0xB411", StringComparison.OrdinalIgnoreCase))
            policies.Add(new() { BlockedFeatures = ["brightness", "contrast", "color-preset", "red-gain", "green-gain", "blue-gain", "gamma", "eco-preset", "dynamic-contrast", "game-color", "shadow-control", "low-blue-light", "display-preset"] });
        if (policies.Count == 0) return null;
        if (policies.Any(p => p.ModeFeatureId != policies[0].ModeFeatureId || p.OffValue != policies[0].OffValue)) throw new InvalidOperationException("HDR 互锁规则冲突，停止写入");
        return new() { ModeFeatureId = policies[0].ModeFeatureId, OffValue = policies[0].OffValue, BlockedFeatures = policies.SelectMany(p => p.BlockedFeatures).Distinct().ToList(), RestoreColorOnExit = policies.Any(p => p.RestoreColorOnExit) };
    }
    public static string ProtectedId(HdrSafetyPolicy? policy, ControlDefinition definition)
    {
        if (policy == null || definition.Id == policy.ModeFeatureId || policy.BlockedFeatures.Contains(definition.Id)) return definition.Id;
        return definition.VcpCode switch { 0x10 => "brightness", 0x12 => "contrast", 0x14 => "color-preset", 0x16 => "red-gain", 0x18 => "green-gain", 0x1A => "blue-gain", byte code when VcpRecognition.IsNumericImage(code) => "brightness", _ => definition.Id };
    }
    public static string Problem(HdrSafetyPolicy? policy, string featureId, Snapshot? snapshot)
    {
        if (policy == null || !policy.BlockedFeatures.Contains(featureId)) return "";
        var mode = snapshot?.Readings.SingleOrDefault(r => r.Capability.Definition.Id == policy.ModeFeatureId);
        if (mode?.Result.Status != "ok" || mode.Result.Current == null) return "显示器 HDR 状态未知，暂不允许设置此项";
        return mode.Result.Current == policy.OffValue ? "" : "显示器 HDR 已开启，关闭显示器 HDR 后才能设置此项";
    }
    internal static void Check(HdrSafetyPolicy? policy, string featureId, Func<string, ControlResult> read)
    {
        if (policy == null || !policy.BlockedFeatures.Contains(featureId)) return;
        var state = read(policy.ModeFeatureId);
        if (state.Status != "ok" || !state.Current.HasValue) throw new InvalidOperationException("显示器 HDR 状态未确认，已阻止设置");
        if (state.Current != policy.OffValue) throw new InvalidOperationException("显示器 HDR 已开启，已阻止冲突设置；请先关闭显示器 HDR");
    }
    static string StateId(MonitorData device) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(device.DevicePath)))[..32].ToLowerInvariant();
    internal static void Remember(MonitorData device, Capability mode, Func<string, Reading> read)
    {
        var saved = new List<Reading> { read("color-preset") };
        if (saved[0].Result.Status != "ok" || saved[0].Result.Current == null) throw new IOException("无法保存进入 HDR 前的色温，未开启 HDR");
        if (saved[0].Semantic(device.WindowsHdrEnabled) == "user") {
            saved.AddRange(new[] { "red-gain", "green-gain", "blue-gain" }.Select(read));
            if (saved.Any(r => r.Result.Status != "ok" || r.Result.Current == null)) throw new IOException("无法保存自定义色温的 RGB，未开启 HDR");
        }
        foreach (var reading in saved) {
            if (!reading.Capability.Definition.Writable || reading.Capability.Problem.Length > 0) throw new IOException("原色温来源不可恢复，未开启 HDR");
            ProtocolCodec.Value(reading.Capability.Definition, reading.Semantic(device.WindowsHdrEnabled), reading.Result.Maximum ?? 0, device.WindowsHdrEnabled);
        }
        var state = new HdrColorState { Id = StateId(device), Device = Library.Copy(device), ModeProvider = mode.Provider, ModeHash = mode.Hash, Pending = true, Saved = saved };
        Library.Save("hdr-color-state", state.Id, state);
    }
    internal static string Restore(MonitorData device, Capability mode, Func<string, Reading> read, Func<Reading, string, ControlResult> write)
    {
        var state = Library.All<HdrColorState>("hdr-color-state").SingleOrDefault(s => s.Id == StateId(device));
        if (state?.Pending != true) return "没有进入 HDR 前的色温记录，未猜测恢复值";
        if (!HardwareReports.SameDevice(state.Device, device) || state.ModeProvider != mode.Provider || state.ModeHash != mode.Hash) return "设备或协议已变化，未自动恢复色温";
        state.Results.Clear();
        bool complete = true;
        foreach (var saved in state.Saved) {
            var current = read(saved.Capability.Definition.Id);
            if (current.Result.Status != "ok" || current.Capability.Provider != saved.Capability.Provider || current.Capability.Hash != saved.Capability.Hash) { state.Results.Add(saved.Capability.Definition.Name + "来源变化或读取失败，恢复停止"); complete = false; break; }
            if (current.Result.Current == saved.Result.Current) { state.Results.Add(saved.Capability.Definition.Name + "已是原值"); continue; }
            var result = write(current, saved.Semantic(device.WindowsHdrEnabled));
            state.Results.Add(saved.Capability.Definition.Name + "：" + result.Message);
            if (!result.Confirmed) { complete = false; break; }
        }
        // Consume once; a later unrelated HDR-off request must not overwrite user edits.
        state.Pending = false; Library.Save("hdr-color-state", state.Id, state);
        return complete ? "已恢复进入 HDR 前的色温" : "色温恢复未完成：" + string.Join("；", state.Results);
    }
}

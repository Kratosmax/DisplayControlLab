using System.Security.Cryptography;
using System.Text;

namespace MonitorInspector;

public static partial class Packages
{
    public static IDisposable Gate() => new PackageGate();
    private sealed class PackageGate : IDisposable
    {
        readonly Mutex mutex = new(false, "Local\\DisplayControl-PackageFiles-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Home)))[..20]);
        public PackageGate() { bool acquired; try { acquired = mutex.WaitOne(10000); } catch (AbandonedMutexException) { acquired = true; } if (!acquired) { mutex.Dispose(); throw new IOException("适配包正在使用中，请稍后重试"); } }
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
    public static string ContentHash(AdapterPackage package)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(package.DirectoryPath, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(package.DirectoryPath, file)));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static bool IsTrusted(AdapterPackage package, string? hash = null) => State().TrustedHashes.GetValueOrDefault(package.Id) == (hash ?? ContentHash(package));
    public static void Attach(MonitorData device, AdapterPackage package, bool allowCode, bool selectControls = false)
    {
        if (!package.Match.Matches(device)) throw new InvalidOperationException("包与当前设备 EDID 不匹配");
        if (package.Kind == "plugin" && !allowCode) throw new InvalidOperationException("必须明确同意运行插件代码");
        Mutate(s => {
            if (!s.DevicePackages.TryGetValue(device.DevicePath, out var ids)) s.DevicePackages[device.DevicePath] = ids = [];
            if (!ids.Contains(package.Id)) ids.Add(package.Id);
            s.Disabled.Remove(package.Id);
            if (package.Kind == "plugin") s.TrustedHashes[package.Id] = ContentHash(package);
            if (selectControls) foreach (var control in package.Controls) s.FeatureRoutes[device.DevicePath + "|" + control.Id] = package.Id;
            if (package.Id.StartsWith("user.detected-", StringComparison.Ordinal) && package.Controls.All(c => c.Writable && c.Verification == "write-confirmed") && package.MenuHints.Count == 0) {
                s.ControlPackageSelections[device.DevicePath] = package.Id;
                s.VerifiedControlProfiles[device.DevicePath] = package.Id;
                foreach (var control in package.Controls) s.FeatureRoutes[device.DevicePath + "|" + control.Id] = package.Id;
            }
        });
    }
    public static void Detach(MonitorData device, string id) => Mutate(s => {
        if (s.DevicePackages.TryGetValue(device.DevicePath, out var ids)) ids.Remove(id);
        if (s.Bindings.GetValueOrDefault(device.DevicePath) == id) s.Bindings.Remove(device.DevicePath);
        if (s.VerifiedControlProfiles.GetValueOrDefault(device.DevicePath) == id) s.VerifiedControlProfiles.Remove(device.DevicePath);
        if (s.ControlPackageSelections.GetValueOrDefault(device.DevicePath) == id) s.ControlPackageSelections.Remove(device.DevicePath);
    });
    public static void SelectControlPackage(MonitorData device, string id)
    {
        var package = id == "standard" ? null : List().Single(p => p.Id == id);
        if (package != null && (package.Kind != "protocol" || !package.Match.Matches(device))) throw new InvalidOperationException("请选择匹配当前显示器的协议包。");
        Mutate(s => {
            s.ControlPackageSelections[device.DevicePath] = id;
            s.VerifiedControlProfiles.Remove(device.DevicePath);
            if (package != null) {
                if (!s.DevicePackages.TryGetValue(device.DevicePath, out var ids)) s.DevicePackages[device.DevicePath] = ids = [];
                if (!ids.Contains(id)) ids.Add(id);
                s.Disabled.Remove(id);
                if (id.StartsWith("user.detected-", StringComparison.Ordinal) && package.Controls.All(c => c.Writable && c.Verification == "write-confirmed") && package.MenuHints.Count == 0)
                    s.VerifiedControlProfiles[device.DevicePath] = id;
            }
            foreach (var key in s.FeatureRoutes.Keys.Where(k => k.StartsWith(device.DevicePath + "|", StringComparison.Ordinal)).ToList()) s.FeatureRoutes.Remove(key);
            if (package != null) foreach (var control in package.Controls) s.FeatureRoutes[device.DevicePath + "|" + control.Id] = id;
        });
    }
    public static HashSet<string>? ControlPackageFeatures(MonitorData device)
    {
        var state = State();
        if (!state.ControlPackageSelections.TryGetValue(device.DevicePath, out var id)) return VerifiedControls(device);
        if (id == "standard") return DeviceControl.Standard.Select(f => f.Id).ToHashSet();
        var package = ForDevice(device).FirstOrDefault(p => p.Id == id);
        if (package == null) return [];
        return state.VerifiedControlProfiles.GetValueOrDefault(device.DevicePath) == id ? VerifiedControls(device) ?? [] : package.Controls.Select(f => f.Id).ToHashSet();
    }
    public static HashSet<string>? VerifiedControls(MonitorData device)
    {
        var id = State().VerifiedControlProfiles.GetValueOrDefault(device.DevicePath);
        var package = id == null ? null : ForDevice(device).FirstOrDefault(p => p.Id == id);
        return package?.Controls.Where(f => f.Writable && f.Verification == "write-confirmed").Select(f => f.Id).ToHashSet();
    }
    public static void Choose(MonitorData device, string feature, string provider) => Mutate(s => s.FeatureRoutes[device.DevicePath + "|" + feature] = provider);
    private static void Mutate(Action<PackageState> mutation)
    {
        using var mutex = new Mutex(false, "Local\\DisplayControl-Packages-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Home)))[..20]);
        bool acquired = false;
        try { try { acquired = mutex.WaitOne(5000); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("适配配置正在被使用");
            var state = State(); mutation(state); SaveState(state);
        } finally { if (acquired) mutex.ReleaseMutex(); }
    }
    public static List<AdapterPackage> ForDevice(MonitorData device)
    {
        var state = State();
        var ids = state.DevicePackages.GetValueOrDefault(device.DevicePath) ?? [];
        if (state.Bindings.TryGetValue(device.DevicePath, out var legacy)) ids = [.. ids, legacy];
        return List().Where(p => ids.Contains(p.Id) && !state.Disabled.Contains(p.Id) && p.Match.Matches(device)).ToList();
    }
    public static List<string> Broken()
    {
        if (!Directory.Exists(Installed)) return [];
        var errors = new List<string>();
        foreach (var dir in Directory.GetDirectories(Installed).Where(x => !Path.GetFileName(x).StartsWith('.')))
            try { Load(Path.Combine(dir, "adapter.json")); } catch (Exception ex) { errors.Add(Path.GetFileName(dir) + ": " + ex.Message); }
        return errors;
    }
}

public sealed class Capability
{
    public ControlDefinition Definition { get; set; } = new();
    public string Provider { get; set; } = "standard";
    public string Version { get; set; } = "builtin";
    public string Hash { get; set; } = "builtin";
    public string Problem { get; set; } = "";
    public string Availability { get; set; } = "unavailable";
    public List<string> Candidates { get; set; } = [];
}
public static class CapabilityCatalog
{
    public static List<Capability> Resolve(MonitorData device)
    {
        var state = Packages.State();
        var active = Packages.ForDevice(device);
        var profile = state.ControlPackageSelections.GetValueOrDefault(device.DevicePath);
        if (profile != null) active = active.Where(p => p.Id == profile).ToList();
        var all = DeviceControl.Standard.Select(f => new Capability { Definition = f }).ToList();
        foreach (var p in active) {
            string hash = Packages.ContentHash(p);
            bool trusted = p.Kind != "plugin" || Packages.IsTrusted(p, hash);
            foreach (var f in p.Controls) all.Add(new() { Definition = f, Provider = p.Id, Version = p.Version, Hash = hash, Problem = !trusted ? "插件未信任或内容已变化" : "" });
        }
        foreach (var p in Packages.List().Where(p => p.Match.Matches(device)))
            foreach (var hint in p.MenuHints.Where(h => all.All(c => c.Definition.Id != h.Id)))
                all.Add(new() { Definition = new() { Id = hint.Id, Name = hint.Name, Group = hint.Group, MenuPath = hint.MenuPath, Description = hint.Description, HelpText = hint.HelpText, Readable = false, ValueType = "status" }, Provider = p.Id, Availability = "unadapted", Problem = "菜单已知，软件控制待适配" });
        return all.GroupBy(c => c.Definition.Id).Select(group => {
            var candidates = group.ToList();
            string? chosen = profile != null ? candidates.Any(c => c.Provider == profile) ? profile : "standard" : state.FeatureRoutes.GetValueOrDefault(device.DevicePath + "|" + group.Key);
            if (chosen == null && candidates.Any(c => c.Provider == "standard")) {
                var preferred = active.Where(p => p.OverridesStandard.Contains(group.Key) && candidates.Any(c => c.Provider == p.Id)).ToList();
                if (preferred.Count == 1 && candidates.Count == 2) chosen = preferred[0].Id;
            }
            var selected = candidates.SingleOrDefault(c => c.Provider == chosen) ?? candidates[0];
            selected.Candidates = candidates.Select(c => c.Provider).ToList();
            if (candidates.Count > 1 && chosen == null) selected.Problem = "提供者冲突：请选择此功能的来源";
            if (chosen != null && candidates.All(c => c.Provider != chosen)) selected.Problem = "所选提供者已不可用";
            return selected;
        }).ToList();
    }
}

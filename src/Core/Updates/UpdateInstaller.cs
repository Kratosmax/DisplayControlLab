using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace MonitorInspector.Updates;

public static class UpdateIdentity
{
    public static Version CurrentVersion { get { var v = typeof(UpdateIdentity).Assembly.GetName().Version!; return new(v.Major, v.Minor, v.Build); } }
    public static UpdatePackageMetadata Read(string root)
    {
        RejectLinks(root);
        string marker = Path.Combine(root, "update-install.json");
        if (!File.Exists(marker) || new FileInfo(marker).Length > 65536 || !File.Exists(Path.Combine(root, "DisplayControl.Controller.exe")) || !File.Exists(Path.Combine(root, "DisplayControl.Reader.exe")) || !File.Exists(Path.Combine(root, "DisplayControl.Updater.exe"))) throw new InvalidOperationException("当前目录不是可就地更新的正式安装包，请手动下载");
        var identity = JsonSerializer.Deserialize<UpdatePackageMetadata>(File.ReadAllText(marker)) ?? throw new InvalidDataException("安装身份无效");
        if (identity.ProductId != UpdateTrust.ProductId || identity.SchemaVersion != 1 || !UpdateTrust.IsSupportedChannel(identity.Channel) || !Version.TryParse(identity.Version, out _)) throw new InvalidDataException("安装身份或通道不符");
        return identity;
    }
    public static void RejectLinks(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("更新目标包含链接目录");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("更新目标包含链接文件");
    }
    public static FileStream Lease(string root, bool exclusive)
    {
        string file = Path.Combine(root, ".displaycontrol-session"); RejectLinks(file);
        if (!File.Exists(file)) { using var create = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite); }
        return new FileStream(file, FileMode.Open, exclusive ? FileAccess.ReadWrite : FileAccess.Read, exclusive ? FileShare.None : FileShare.Read);
    }
    public static string? Startup(string root)
    {
        if (!File.Exists(Path.Combine(root, "update-install.json")) && !Directory.Exists(Path.Combine(root, ".displaycontrol-update"))) return null;
        try {
            using var lease = Lease(root, true);
            string? recovery = UpdateInstaller.Recover(root); _ = Read(root);
            string result = Path.Combine(root, "update-result.json");
            if (File.Exists(result)) { var value = JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(result)); File.Delete(result); return value?.Success == false ? "上次更新失败：" + value.Message : recovery ?? "软件已更新，用户资料已保留。"; }
            return recovery;
        } catch (IOException) { return null; }
        catch (Exception) { return "更新恢复需要核查，请查看更新日志；未自动覆盖安装目录。"; }
    }
}

public sealed class UpdateTransaction
{
    public string Phase { get; set; } = "staging";
    public List<UpdateFileChange> Changes { get; set; } = [];
}
public sealed record UpdateFileChange(string RelativePath, bool HadOriginal);
public static class UpdateInstaller
{
    const string TransactionName = ".displaycontrol-update";
    static string Inside(string root, string relative)
    {
        string safe = UpdatePackageValidator.NormalizeRelativePath(relative);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(fullRoot, safe));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新路径越界");
        UpdateIdentity.RejectLinks(path); return path;
    }
    static void Save(string root, UpdateTransaction state)
    {
        var path = Path.Combine(root, "transaction.json"); var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, state); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
    public static string? Recover(string target)
    {
        var root = Path.Combine(target, TransactionName); UpdateIdentity.RejectLinks(root);
        if (!Directory.Exists(root)) return null;
        var journal = Path.Combine(root, "transaction.json");
        if (!File.Exists(journal) || new FileInfo(journal).Length > 2 * 1024 * 1024) throw new InvalidDataException("更新事务缺失或过大，保留现场");
        var transaction = JsonSerializer.Deserialize<UpdateTransaction>(File.ReadAllText(journal)) ?? throw new InvalidDataException("事务损坏");
        if (transaction.Phase == "committed") { Directory.Delete(root, true); return "软件更新完成，用户适配包及资料已保留。"; }
        foreach (var change in transaction.Changes.AsEnumerable().Reverse()) {
            string original = Inside(target, change.RelativePath), backup = Inside(Path.Combine(root, "backup"), change.RelativePath);
            if (File.Exists(backup)) { Directory.CreateDirectory(Path.GetDirectoryName(original)!); File.Move(backup, original, true); }
            else if (!change.HadOriginal) File.Delete(original);
        }
        Directory.Delete(root, true); return "上次更新未完成，已恢复原版本，用户资料未修改。";
    }
    public static async Task Install(string package, string target, UpdateInfo info, CancellationToken token = default)
    {
        target = Path.GetFullPath(target); _ = UpdateIdentity.Read(target);
        using var lease = UpdateIdentity.Lease(target, true);
        var identity = UpdateIdentity.Read(target);
        if (identity.Channel != info.Channel || Version.Parse(identity.Version) >= info.Version) throw new InvalidOperationException("更新通道不符或不是较新版本");
        if (Directory.Exists(Path.Combine(target, TransactionName))) { Recover(target); throw new IOException("已恢复上次事务，请重新检查更新"); }
        var validated = await UpdatePackageValidator.ValidateAsync(package, info, token).ConfigureAwait(false);
        var root = Path.Combine(target, TransactionName); var stage = Path.Combine(root, "stage"); var backupRoot = Path.Combine(root, "backup");
        Directory.CreateDirectory(stage); Directory.CreateDirectory(backupRoot); var state = new UpdateTransaction(); Save(root, state);
        try {
            using (var archive = ZipFile.OpenRead(package)) foreach (var entry in archive.Entries.Where(e => e.Name.Length > 0)) {
                token.ThrowIfCancellationRequested(); string destination = Inside(stage, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open(); await using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await BoundedStream.CopyToAsync(source, stream, 500L * 1024 * 1024, token).ConfigureAwait(false);
            }
            state.Phase = "replacing"; Save(root, state);
            foreach (var relative in validated.Files.Where(f => File.Exists(Inside(stage, f)))) {
                token.ThrowIfCancellationRequested(); string source = Inside(stage, relative), destination = Inside(target, relative), backup = Inside(backupRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                state.Changes.Add(new(relative, File.Exists(destination))); Save(root, state);
                if (File.Exists(destination)) { Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Move(destination, backup); }
                File.Move(source, destination);
            }
            foreach (var obsolete in identity.Files.Where(f => !validated.Files.Contains(f, StringComparer.OrdinalIgnoreCase))) {
                string destination = Inside(target, obsolete), backup = Inside(backupRoot, obsolete);
                if (!File.Exists(destination)) continue;
                state.Changes.Add(new(obsolete, true)); Save(root, state); Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Move(destination, backup);
            }
            var installed = UpdateIdentity.Read(target);
            if (installed.Version != info.Version.ToString(3)) throw new InvalidDataException("替换后版本不符");
            state.Phase = "committed"; Save(root, state);
        } catch {
            try { Recover(target); } catch (Exception ex) { throw new IOException("更新失败且回滚未完成，已保留事务备份，请勿重复安装", ex); }
            throw;
        }
        // Confirmed transactions are cleaned by the next launch; backups survive a crash.
    }
}

public sealed class UpdateResult { public bool Success { get; set; } public string Message { get; set; } = ""; }

public sealed record PreparedUpdate(string PackagePath, string ManifestPath, string LauncherPath);
public sealed class UpdateClient
{
    readonly string installRoot;
    public UpdateClient(string? root = null) => installRoot = root ?? AppContext.BaseDirectory;
    public string Channel { get { try { return UpdateIdentity.Read(installRoot).Channel; } catch { return "Lite"; } } }
    public bool CanInstall { get { try { _ = UpdateIdentity.Read(installRoot); return true; } catch { return false; } } }
    public async Task<UpdateInfo?> Check(UpdateSettings settings, CancellationToken token)
    {
        using var network = new UpdateNetwork(settings);
        var info = await network.Fetch(new Uri(UpdateTrust.Repository + "/releases/latest/download/" + UpdateTrust.ManifestName(Channel)), UpdateManifestCodec.MaximumManifestSize,
            json => UpdateManifestCodec.ParseAndVerify(json, UpdateTrust.PublicKeyPem, Channel), token).ConfigureAwait(false);
        return info.Version > UpdateIdentity.CurrentVersion ? info : null;
    }
    public async Task<PreparedUpdate> Prepare(UpdateInfo update, UpdateSettings settings, IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        if (!CanInstall) throw new InvalidOperationException("开发目录不能就地更新，请使用正式ZIP或安装包");
        // One download/handoff across controller and reader; it is an OS file lease, not a UI-only flag.
        string cache = Library.Folder("updates"); using var gate = new FileStream(Path.Combine(cache, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string folder = Path.Combine(cache, update.Version.ToString(3) + "-" + Guid.NewGuid().ToString("N"));
        using var network = new UpdateNetwork(settings); string package = await network.Download(update, folder, progress, token).ConfigureAwait(false);
        string manifest = Path.Combine(folder, "update.json"); await File.WriteAllTextAsync(manifest, update.RawManifest, token).ConfigureAwait(false);
        string launcher = Path.Combine(folder, "launcher"); Directory.CreateDirectory(launcher);
        var launcherFiles = update.Channel == "Full" ? Directory.GetFiles(installRoot).Where(p => Path.GetExtension(p) is ".exe" or ".dll" or ".json").Select(Path.GetFileName).Cast<string>() : new[] { "DisplayControl.Updater.exe", "DisplayControl.Updater.dll", "DisplayControl.Updater.deps.json", "DisplayControl.Updater.runtimeconfig.json", "DisplayControl.Core.dll" };
        foreach (var name in launcherFiles) File.Copy(Path.Combine(installRoot, name), Path.Combine(launcher, name));
        return new(package, manifest, Path.Combine(launcher, "DisplayControl.Updater.exe"));
    }
    public void Launch(PreparedUpdate prepared, bool reader)
    {
        var start = new ProcessStartInfo(prepared.LauncherPath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(prepared.LauncherPath)! };
        foreach (string arg in new[] { "--package", prepared.PackagePath, "--manifest", prepared.ManifestPath, "--target", installRoot, "--pid", Environment.ProcessId.ToString(), "--started", Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(), "--entry", reader ? "DisplayControl.Reader.exe" : "DisplayControl.Controller.exe" }) start.ArgumentList.Add(arg);
        _ = Process.Start(start) ?? throw new IOException("无法启动更新器，原安装未修改");
    }
    public static void Cleanup()
    {
        try {
            string root = Library.Folder("updates"); using var gate = new FileStream(Path.Combine(root, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            foreach (var directory in Directory.GetDirectories(root).Take(32)) {
                UpdateIdentity.RejectLinks(directory);
                if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-14)) Directory.Delete(directory, true);
            }
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

using System.Diagnostics;
using System.Text.Json;
using MonitorInspector;
using MonitorInspector.Updates;

string? validatedTarget = null;
try {
    var options = new Dictionary<string, string>();
    for (int i = 0; i < args.Length; i += 2) { if (i + 1 >= args.Length || !args[i].StartsWith("--")) throw new ArgumentException("更新器参数无效"); options.Add(args[i][2..], args[i + 1]); }
    string target = Path.GetFullPath(options["target"]);
    if (Path.GetFullPath(AppContext.BaseDirectory).StartsWith(target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("更新器必须从安装目录外启动");
    var identity = UpdateIdentity.Read(target); validatedTarget = target;
    var info = UpdateManifestCodec.ParseAndVerify(File.ReadAllText(options["manifest"]), UpdateTrust.PublicKeyPem, identity.Channel);
    await UpdatePackageValidator.ValidateAsync(options["package"], info);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try {
        using var process = Process.GetProcessById(int.Parse(options["pid"]));
        if (process.StartTime.ToUniversalTime().Ticks != long.Parse(options["started"])) throw new IOException("原进程身份已变化");
        await process.WaitForExitAsync(timeout.Token);
    } catch (ArgumentException) { }
    while (true) {
        timeout.Token.ThrowIfCancellationRequested();
        try { using var lease = UpdateIdentity.Lease(target, true); break; }
        catch (IOException) { await Task.Delay(300, timeout.Token); }
    }
    await UpdateInstaller.Install(options["package"], target, info);
    string entry = options["entry"];
    if (entry is not ("DisplayControl.Reader.exe" or "DisplayControl.Controller.exe")) throw new ArgumentException("重启入口无效");
    File.WriteAllText(Path.Combine(target, "update-result.json"), JsonSerializer.Serialize(new UpdateResult { Success = true, Message = "已更新到 " + info.Version.ToString(3) }));
    var restart = new ProcessStartInfo(Path.Combine(target, entry)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = target };
    restart.ArgumentList.Add("--updated-from"); restart.ArgumentList.Add(identity.Version);
    if (options.GetValueOrDefault("verify-startup") == "true") { restart.ArgumentList.Clear(); restart.ArgumentList.Add("--update-verify-startup"); }
    Process.Start(restart); return 0;
} catch (Exception ex) {
    if (validatedTarget != null) try { File.WriteAllText(Path.Combine(validatedTarget, "update-result.json"), JsonSerializer.Serialize(new UpdateResult { Success = false, Message = ex is OperationCanceledException ? "工具未在60秒内退出，未替换软件。" : ex.Message })); } catch { }
    try { string log = Path.Combine(Library.Folder("logs"), "update-error.txt"); if (File.Exists(log) && new FileInfo(log).Length > 262144) File.Move(log, log + ".old", true); File.AppendAllText(log, DateTimeOffset.Now + " " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine); } catch { }
    return 1;
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonitorInspector;

namespace DisplayControl.UI;
public partial class WorkspaceWindow
{
    sealed record PendingControl(MonitorData Device, Capability Capability, string Value, long Sequence);
    readonly Dictionary<string, PendingControl> pendingWrites = [];
    readonly Dictionary<string, long> latestIntent = [];
    readonly HashSet<string> writingDevices = [];
    readonly HashSet<string> ownOperations = [];
    readonly Dictionary<string, Reading> lastKnownReadings = [];
    readonly HashSet<string> modeReloading = [];
    readonly CancellationTokenSource modeLifetime = new();
    readonly DispatcherTimer renderTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    long sequence;
    bool polling, closeAfterWrites;
    DateTime lastOperationCheck = DateTime.UtcNow;
    long Revision(string key) => deviceRevisions.GetValueOrDefault(key);
    void StoreSnapshot(Snapshot update, bool full = false)
    {
        string key = update.Device.DevicePath;
        if (deviceSnapshots.TryGetValue(key, out var prior) && (!HardwareReports.SameDevice(prior.Device, update.Device) || prior.Device.WindowsHdrEnabled != update.Device.WindowsHdrEnabled))
            foreach (var id in lastKnownReadings.Keys.Where(id => id.StartsWith(key + "|")).ToList()) lastKnownReadings.Remove(id);
        foreach (var reading in update.Readings.Where(r => r.Result.Status == "ok")) lastKnownReadings[key + "|" + reading.Capability.Definition.Id] = Library.Copy(reading);
        deviceSnapshots[key] = full ? Library.Copy(update) : DisplayWorkspace.Merge(deviceSnapshots.GetValueOrDefault(key), update);
        if (device?.DevicePath == key) snapshot = deviceSnapshots[key];
    }
    async Task WarmDevices(CancellationToken token)
    {
        var list = devices.Items.Cast<MonitorData>().ToList();
        string[] priority = ["brightness", "contrast", "hdr-preset", "color-preset"];
        await Task.WhenAll(list.Select(async d => { long version = Revision(d.DevicePath); var update = await workspace.Capture(d, token, scope: priority); if (version == Revision(d.DevicePath)) StoreSnapshot(update); }));
        ShowPage(); Say("所有显示器的常用状态已读取，正在补充其他功能…");
        await Task.WhenAll(list.Select(async d => {
            var ids = (await Task.Run(() => workspace.Backend.Capabilities(d), token)).Select(c => c.Definition.Id).Except(priority).ToList();
            if (ids.Count > 0) { long version = Revision(d.DevicePath); var update = await workspace.Capture(d, token, scope: ids); if (version == Revision(d.DevicePath)) StoreSnapshot(update); }
        }));
        ShowPage(); Say($"{list.Count} 台显示器的初始状态已加载，切换设备直接使用当前缓存。");
    }
    void QueueControlWrite(Capability capability, string value)
    {
        if (device == null) return;
        if (modeReloading.Contains(device.DevicePath)) { Say("显示模式正在恢复，暂不发送设置。"); return; }
        var target = Library.Copy(deviceSnapshots.GetValueOrDefault(device.DevicePath)?.Device ?? device); string key = target.DevicePath + "|" + capability.Definition.Id;
        long version = ++sequence; latestIntent[key] = version;
        pendingWrites[key] = new(target, capability, value, version); deviceRevisions[target.DevicePath] = Revision(target.DevicePath) + 1;
        if (live.TryGetValue(capability.Definition.Id, out var label)) { label.Text = "正在应用：" + value; label.Visibility = Visibility.Visible; }
        if (writingDevices.Add(target.DevicePath)) _ = DrainWrites(target.DevicePath);
    }
    async Task DrainWrites(string devicePath)
    {
        try {
            while (pendingWrites.FirstOrDefault(x => x.Value.Device.DevicePath == devicePath) is { Key: not null } item) {
                pendingWrites.Remove(item.Key);
                await WriteFor(item.Value.Device, item.Value.Capability, item.Value.Value, CancellationToken.None, item.Value.Sequence);
            }
        } finally { writingDevices.Remove(devicePath); RequestRender(); if (closeAfterWrites && writingDevices.Count == 0) Close(); }
    }
    async Task WriteFor(MonitorData target, Capability capability, string value, CancellationToken token, long intent = 0)
    {
        string key = target.DevicePath + "|" + capability.Definition.Id; controlProblems.Remove(key);
        try {
            var cached = deviceSnapshots.GetValueOrDefault(target.DevicePath) ?? (snapshot?.Device.DevicePath == target.DevicePath ? snapshot : null);
            Say("正在设置 " + capability.Definition.Name + "…");
            var op = await workspace.SetFeature(target, capability, value, cached, token); ownOperations.Add(op.Id);
            StoreSnapshot(op.After);
            if (intent == 0 || latestIntent.GetValueOrDefault(key) == intent) {
                var result = op.Results.Last();
                if (!result.Confirmed && capability.Definition.Readable && capability.Definition.ValueType != "action") controlProblems[key] = "目标 " + value + "；" + result.Message;
                Say(result.Message);
                if (device?.DevicePath == target.DevicePath && live.TryGetValue(capability.Definition.Id, out var label)) label.Text = op.After.Readings.Single(r => r.Capability.Definition.Id == capability.Definition.Id).Display(op.After.Device.WindowsHdrEnabled);
                RequestRender();
            }
            if (capability.Definition.Readable && op.Results.Last().Status is "error" or "unknown") {
                await Task.Delay(250, token);
                await RetryFeatureRead(target, capability, token);
            }
        } catch (Exception ex) {
            controlProblems[key] = ex.Message;
            // A rejected stale SDR intent must also update the visible interlock.
            var safety = HdrSafety.Policy(target);
            if (safety?.BlockedFeatures.Contains(HdrSafety.ProtectedId(safety, capability.Definition)) == true) {
                long version = Revision(target.DevicePath);
                try { var update = await workspace.Capture(target, token, scope: [safety.ModeFeatureId]); if (version == Revision(target.DevicePath)) StoreSnapshot(update); }
                catch (Exception refresh) { controlProblems[key] += "；HDR 状态核查未完成：" + refresh.Message; }
            }
            Say(controlProblems[key]); RequestRender();
        }
        if (!IsVisible) ShowPage();
    }
    async Task RetryFeatureRead(MonitorData target, Capability capability, CancellationToken token)
    {
        long version = Revision(target.DevicePath);
        string key = target.DevicePath + "|" + capability.Definition.Id;
        try {
            var current = (await workspace.Backend.Devices(token)).SingleOrDefault(d => HardwareReports.SameDevice(d, target));
            if (current == null || current.WindowsHdrEnabled != target.WindowsHdrEnabled) { Say("设备或系统模式已变化，等待状态重载。"); return; }
            var update = await workspace.Capture(current, token, scope: [capability.Definition.Id]);
            if (version != Revision(target.DevicePath)) return;
            StoreSnapshot(update);
            var reading = update.Readings.Single(r => r.Capability.Definition.Id == capability.Definition.Id);
            if (reading.Result.Status == "ok") {
                controlProblems.Remove(key);
                Say(capability.Definition.Name + "读取已恢复；未自动重发设置，请按当前值继续调节。");
            } else Say(capability.Definition.Name + "仍无法读取，可点此项的“重试读取”；其他功能不受影响。");
            RequestRender();
        } catch (Exception ex) { Say("单项读取未恢复：" + ex.Message); }
    }
    async Task<bool> ReloadAfterModeChange(MonitorData target, CancellationToken token)
    {
        if (!modeReloading.Add(target.DevicePath)) return false;
        long version = Revision(target.DevicePath);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, modeLifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var attempts = new List<object>();
        bool recovered = false, verifyFull = false, verificationScheduled = false;
        List<string>? scope = null;
        StoreSnapshot(new Snapshot { Device = target }, true);
        foreach (var key in controlProblems.Keys.Where(k => k.StartsWith(target.DevicePath + "|")).ToList()) controlProblems.Remove(key);
        presetPlan = null;
        ShowPage(); Say("系统颜色模式已改变，正在等待连接稳定并恢复显示器状态…");
        try {
            int[] delays = [1200, 1000, 2000, 3000, 5000];
            for (int attempt = 0; attempt < delays.Length; attempt++) {
                await Task.Delay(delays[attempt], deadline.Token);
                if (version != Revision(target.DevicePath)) return false;
                try {
                    var current = (await workspace.Backend.Devices(deadline.Token)).SingleOrDefault(d => HardwareReports.SameDevice(d, target));
                    if (current == null) { attempts.Add(new { attempt, state = "disconnected" }); continue; }
                    if (current.WindowsHdrEnabled != target.WindowsHdrEnabled) return false;
                    await Task.Delay(250, deadline.Token);
                    var stable = (await workspace.Backend.Devices(deadline.Token)).SingleOrDefault(d => HardwareReports.SameDevice(d, current));
                    if (stable == null) { attempts.Add(new { attempt, state = "disconnected" }); continue; }
                    if (stable.WindowsHdrEnabled != target.WindowsHdrEnabled) return false;
                    Say($"正在恢复显示器状态 · 第 {attempt + 1} 次核查…");
                    var update = await workspace.Capture(stable, deadline.Token, scope: scope);
                    if (version != Revision(target.DevicePath)) return false;
                    StoreSnapshot(update, scope == null);
                    var failures = update.Readings.Where(r => r.Capability.Problem.Length == 0 && r.Capability.Definition.Readable && r.Result.Status is "error" or "unknown").Select(r => r.Capability.Definition.Id).ToList();
                    attempts.Add(new { attempt, milliseconds = timer.ElapsedMilliseconds, scope, failures });
                    if (failures.Count == 0) {
                        // Once a transition returned bad data, also replace early successful
                        // values that may have preceded the display's actual mode change.
                        if (verifyFull) { verifyFull = false; verificationScheduled = true; scope = null; continue; }
                        recovered = true; break;
                    }
                    if (scope == null && !verificationScheduled) verifyFull = true;
                    scope = failures;
                } catch (IOException ex) { attempts.Add(new { attempt, state = "read-failed", message = ex.Message }); }
            }
        } catch (OperationCanceledException) when (!token.IsCancellationRequested && !modeLifetime.IsCancellationRequested) {
            attempts.Add(new { state = "deadline" });
        } finally {
            modeReloading.Remove(target.DevicePath);
            Library.Save("mode-reloads", Library.NewId(), new { device = target.DevicePath, expectedHdr = target.WindowsHdrEnabled, recovered, milliseconds = timer.ElapsedMilliseconds, attempts });
            if (!modeLifetime.IsCancellationRequested) { ShowPage(); Say(recovered ? "显示器状态已重新读取，模式选项已更新。" : "显示器状态尚未完全恢复；可重读当前显示器，或在失败功能旁重试。未发送显示器设置。"); }
        }
        return recovered;
    }
    void RequestRender()
    {
        if (!IsVisible) return;
        renderTimer.Stop(); renderTimer.Start();
    }
    void InitializeLiveLifecycle()
    {
        renderTimer.Tick += (_, _) => { if (body.IsMouseCaptureWithin || System.Windows.Input.Keyboard.FocusedElement is TextBox || writingDevices.Count > 0 || pendingWrites.Count > 0) return; renderTimer.Stop(); ShowPage(); };
        Closing += (_, e) => { if (writingDevices.Count == 0) return; e.Cancel = true; closeAfterWrites = true; pendingWrites.Clear(); Say("正在等当前设置完成后退出…"); };
        Closed += (_, _) => { modeLifetime.Cancel(); renderTimer.Stop(); foreach (var editor in numericEditors) editor.Dispose(); };
    }
    void UpdateModeBadge()
    {
        var d = snapshot?.Device ?? device;
        bool? enabled = d?.WindowsHdrEnabled;
        modeBadge.Text = enabled == true ? "● HDR · 系统 HDR 已开启" : enabled == false ? "○ SDR · 系统 HDR 已关闭" : "○ 系统颜色状态未知";
        modeBadge.Foreground = Brush(enabled == true ? "#6B3CB4" : "#326A7C"); modeBadge.Background = Brush(enabled == true ? "#EFE4FF" : "#E6F2F5");
        if (systemHdrButton == null) return;
        systemHdrButton.Visibility = reader ? Visibility.Collapsed : Visibility.Visible;
        systemHdrButton.Content = enabled == true ? "关闭系统 HDR" : "开启系统 HDR";
        systemHdrButton.IsEnabled = d?.CanSetWindowsHdr == true && !modeReloading.Contains(d.DevicePath);
        systemHdrButton.ToolTip = d?.CanSetWindowsHdr == true ? "系统 HDR 让 Windows 为当前显示器启用高动态范围输出；它与显示器自己的 HDR 预设是两个独立设置。切换时可能短暂黑屏，之后自动重载显示器状态。" : "此显示器或当前 Windows/驱动未提供可用的 HDR 专用接口";
    }
    async Task ChangeSystemHdr(CancellationToken token)
    {
        if (device == null) return;
        var target = Library.Copy(snapshot?.Device ?? device);
        var request = new ControlRequest { Device = target, Action = "system-hdr", ControlId = "windows-hdr", Value = target.WindowsHdrEnabled == true ? "off" : "on" };
        deviceRevisions[target.DevicePath] = Revision(target.DevicePath) + 1;
        foreach (var key in pendingWrites.Where(kv => kv.Value.Device.DevicePath == target.DevicePath).Select(kv => kv.Key).ToList()) pendingWrites.Remove(key);
        Say("正在切换系统 HDR，显示器可能短暂黑屏…");
        var result = await workspace.Backend.Execute(request, token);
        string id = Library.NewId(); Library.Save("system-operations", id, new { request, result, time = DateTimeOffset.Now });
        if (result.Status != "ok" || !result.Confirmed) { Say(result.Message); return; }
        target.WindowsHdrEnabled = request.Value == "on";
        target.Hdr = target.WindowsHdrEnabled == true ? "系统 HDR 已开启，正在核查显示器" : "系统 HDR 已关闭，正在核查显示器";
        await ReloadAfterModeChange(target, token);
    }
    async Task PollState(bool force = false)
    {
        if (polling || active != null || writingDevices.Count > 0 || !force && !IsVisible) return;
        polling = true;
        try {
            var list = await workspace.Backend.Devices(CancellationToken.None);
            foreach (var d in list) {
                var previous = deviceSnapshots.GetValueOrDefault(d.DevicePath);
                if (previous == null || !HardwareReports.SameDevice(previous.Device, d) || previous.Device.WindowsHdrEnabled != d.WindowsHdrEnabled) {
                    deviceRevisions[d.DevicePath] = Revision(d.DevicePath) + 1;
                    foreach (var key in pendingWrites.Where(kv => kv.Value.Device.DevicePath == d.DevicePath).Select(kv => kv.Key).ToList()) pendingWrites.Remove(key);
                    // Invalidate old ranges/options immediately. Read again after the black-screen transition settles.
                    await ReloadAfterModeChange(d, CancellationToken.None); RequestRender();
                }
            }
            if (device != null && list.All(d => !HardwareReports.SameDevice(d, device))) { banner.Text = "当前显示器连接已改变或断开，等待恢复。"; body.IsEnabled = false; }
            else if (active == null) body.IsEnabled = true;
            string stamp = string.Join("|", new[] { "adapters", "state.json" }.Select(p => { string path = Path.Combine(Packages.Home, p); return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : Directory.Exists(path) ? string.Join(",", Directory.GetFiles(path, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc)) : ""; }));
            if (libraryStamp.Length > 0 && stamp != libraryStamp) { deviceSnapshots.Clear(); await WarmDevices(CancellationToken.None); } libraryStamp = stamp;
            DateTime checkedAt = DateTime.UtcNow;
            var changed = Directory.GetFiles(Library.Folder("operations"), "*.json").Where(f => File.GetLastWriteTimeUtc(f) > lastOperationCheck).ToList();
            foreach (var file in changed) {
                var op = System.Text.Json.JsonSerializer.Deserialize<Operation>(await File.ReadAllTextAsync(file), Packages.Json);
                if (op == null || ownOperations.Contains(op.Id) || op.Results.Count == 0 || !deviceSnapshots.TryGetValue(op.Before.Device.DevicePath, out var cached)) continue;
                var scope = op.Requests.Select(r => r.ControlId).Concat(op.Before.Readings.Where(r => op.Requests.Any(q => q.ControlId == r.Capability.Definition.Id)).SelectMany(r => DisplayWorkspace.AffectedScope(r.Capability.Definition))).Distinct().ToList();
                if (scope.Count > 0) {
                    long version = Revision(cached.Device.DevicePath);
                    var update = await workspace.Capture(cached.Device, CancellationToken.None, scope: scope);
                    if (version == Revision(cached.Device.DevicePath)) StoreSnapshot(update);
                }
            }
            lastOperationCheck = checkedAt;
            if (changed.Count > 0 && page is "history" or "experiments") RequestRender();
        } catch (Exception ex) { Say("状态同步暂未完成：" + ex.Message); }
        finally { polling = false; }
    }
}

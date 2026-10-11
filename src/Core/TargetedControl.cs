namespace MonitorInspector;

public sealed partial class DisplayWorkspace
{
    public static List<string> WriteScope(ControlDefinition f) => new[] { f.Id }.Concat(f.WritableWhen.Select(c => c.FeatureId)).Distinct().ToList();
    // Semantic HDR presets may invalidate color state. Older packs lack explicit
    // affectedFeatures, so observe only this small standard color scope.
    public static List<string> AffectedScope(ControlDefinition f) => f.AffectedFeatures.Count > 0 ? f.AffectedFeatures : f.Id == "hdr-preset" ? ["color-preset", "red-gain", "green-gain", "blue-gain"] : [];
    public static Snapshot Merge(Snapshot? prior, Snapshot update)
    {
        if (prior == null || !HardwareReports.SameDevice(prior.Device, update.Device)) return Library.Copy(update);
        var merged = Library.Copy(prior); merged.Device = Library.Copy(update.Device); merged.Time = update.Time;
        foreach (var r in update.Readings) ReplaceReading(merged, r);
        return merged;
    }
    public static void UpdateFromWrite(Snapshot before, Snapshot after, Capability c, ControlResult result)
    {
        var old = before.Readings.SingleOrDefault(r => r.Capability.Definition.Id == c.Definition.Id);
        if (old != null && result.Before.HasValue) { old.Result.Current = result.Before; old.Result.Maximum = result.BeforeMaximum ?? old.Result.Maximum; }
        var r = new Reading { Capability = Library.Copy(c), Result = Library.Copy(result) };
        ReplaceReading(after, r);
    }
    static void ReplaceReading(Snapshot snapshot, Reading reading)
    {
        int index = snapshot.Readings.FindIndex(r => r.Capability.Definition.Id == reading.Capability.Definition.Id);
        if (index >= 0) snapshot.Readings[index] = Library.Copy(reading);
        else snapshot.Readings.Add(Library.Copy(reading));
    }
    public async Task<Operation> SetFeature(MonitorData device, Capability capability, string value, Snapshot? cached, CancellationToken token)
    {
        var fresh = (await Backend.Devices(token)).SingleOrDefault(d => HardwareReports.SameDevice(d, device)) ?? throw new IOException("显示器连接已变化");
        if (fresh.WindowsHdrEnabled != device.WindowsHdrEnabled) throw new InvalidOperationException("系统 HDR 条件已改变，旧设置已取消，请等待状态重载");
        var before = cached == null ? new Snapshot { Device = fresh } : Library.Copy(cached);
        var f = capability.Definition;
        var reading = before.Readings.SingleOrDefault(r => r.Capability.Definition.Id == f.Id);
        bool needsRead = f.Readable && (reading?.Result.Status != "ok" || cached?.Device.WindowsHdrEnabled != fresh.WindowsHdrEnabled);
        var scope = f.WritableWhen.Select(c => c.FeatureId).ToList(); if (needsRead) scope.Add(f.Id);
        var safety = HdrSafety.Policy(fresh);
        if (safety?.BlockedFeatures.Contains(HdrSafety.ProtectedId(safety, f)) == true) scope.Add(safety.ModeFeatureId);
        if (scope.Count > 0) before = Merge(before, await Capture(fresh, token, scope: scope));
        before.Device = fresh;
        if (!f.Readable && reading == null) before.Readings.Add(new Reading { Capability = capability, Result = new() { Status = "unsupported" } });
        var preset = new Preset { DevicePath = fresh.DevicePath, Items = [new() { FeatureId = f.Id, Value = value, Provider = capability.Provider, Hash = capability.Hash }] };
        var plan = Preflight(preset, before);
        if (PreflightFailure(plan) is { } failure) throw new InvalidOperationException(failure);
        var op = new Operation { Name = f.Name, Before = Library.Copy(before), After = Library.Copy(before) };
        var request = Request(fresh, capability, "write", value); op.Requests.Add(request); Library.Save("operations", op.Id, op);
        ControlResult result;
        try { result = await Backend.Execute(request, token); }
        catch (Exception ex) { result = new() { RequestId = request.RequestId, Status = "unknown", Message = "结果无法确认，请核查：" + ex.Message }; }
        op.Results.Add(result); UpdateFromWrite(op.Before, op.After, capability, result); Library.Save("operations", op.Id, op);
        // The worker already read back the target. Refresh only declared dependents.
        var affected = AffectedScope(f);
        if (result.Sent && affected.Count > 0)
            try { op.After = Merge(op.After, await Capture(fresh, token, scope: affected)); }
            catch (Exception ex) { op.After.Name = "联动核查未完成：" + ex.Message; }
        Library.Save("operations", op.Id, op); return op;
    }
}

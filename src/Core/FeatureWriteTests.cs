namespace MonitorInspector;

public sealed partial class ReaderSurveyRunner
{
    public static bool NeedsManualConfirmation(SurveyFeature item) => item.Definition.Id is "input-source" or "power" or "hdr-preset" or "osd" or "osd-language"
        || item.Definition.Group is "connection" or "device" or "osd" || item.Definition.VcpCode is 0x60 or 0xD6 or 0xCA or 0xCC;

    public static string CannotTest(SurveyFeature item)
    {
        var f = item.Definition;
        if (f.VcpCode is byte code && Native.SkipQuery(code)) return "恢复出厂等不可撤销命令，不允许试写";
        if (!f.Readable || f.ValueType is "action" or "text") return "没有可恢复的读数，不能自动试写";
        if (f.ValueType == "status") return "只有读取定义；需补充可靠写入协议后再测试";
        if (item.Backend is not ("standard-ddc" or "amd-ddc")) return "此通道需要专用测试，不能导出通用控制包";
        bool standardEnum = item.Backend == "standard-ddc" && f.VcpCode.HasValue
            && DeviceControl.Standard.Any(d => d.Id == f.Id && d.VcpCode == f.VcpCode && d.ValueType == "enum");
        if (!f.Writable && !standardEnum) return "协议明确只读；补充写入定义后再测试";
        if (f.VcpCode == null && f.Raw == null) return "缺少可执行的写入协议";
        return "";
    }

    public async Task Retry(ReaderSurvey report, string id, string? value, bool manualConfirmed, Action<string> progress, CancellationToken token)
    {
        var item = report.Features.Single(f => f.Definition.Id == id);
        var previousTest = Library.Copy(item);
        // A new trial never replaces a pending original state.
        foreach (var pending in Library.All<ReaderSurvey>("reader-surveys").Where(r => r.RecoveryRequired && HardwareReports.SameDevice(r.Device, report.Device))) {
            var now = await workspace.Capture(report.Device, token);
            if (!ReaderSurvey.RecoveryMatches(pending.RecoveryBaseline, now)) throw new InvalidOperationException("先按橙色行确认恢复，再进行试写。现在仍可重新读取。");
            pending.RecoveryRequired = false; pending.Status = "原值已核查恢复，请重新检测";
            Library.Save("reader-surveys", pending.Id, pending);
        }
        if (report.RecoveryRequired) {
            if (!ReaderSurvey.RecoveryMatches(report.RecoveryBaseline, await workspace.Capture(report.Device, token))) throw new InvalidOperationException("先确认原设置已恢复，再测试。");
            report.RecoveryRequired = false;
        }
        try {
            await TestFeature(report, item, value, manualConfirmed, progress, token);
            // Successful single-option retries add evidence; a failed retry still revokes this feature.
            if (item.WriteVerified && previousTest.WriteVerified && !report.RecoveryRequired) {
                item.TestedValues = previousTest.TestedValues.Concat(item.TestedValues).Distinct().ToList();
            }
        }
        finally {
            report.Status = report.RecoveryRequired ? "需要核查恢复" : "已完成";
            Library.Save("reader-surveys", report.Id, report);
        }
    }

    async Task TestFeature(ReaderSurvey report, SurveyFeature item, string? selectedValue, bool manualConfirmed, Action<string> progress, CancellationToken token)
    {
        try { await TestFeatureCore(report, item, selectedValue, manualConfirmed, progress, token); }
        catch (OperationCanceledException) {
            item.WriteStatus = report.RecoveryRequired ? "恢复未确认" : "测试已取消";
            item.TestReason = report.RecoveryRequired ? "测试取消，恢复尚未确认" : "测试取消，原值已恢复；可重试";
            throw;
        }
        catch (Exception ex) {
            item.TestReason = report.RecoveryRequired ? "恢复尚未确认，请先核查" : "测试没完成：" + ex.Message;
            if (!report.RecoveryRequired) item.WriteStatus = "测试失败";
            item.ErrorDetails = ex.ToString();
        }
    }

    async Task TestFeatureCore(ReaderSurvey report, SurveyFeature item, string? selectedValue, bool manualConfirmed, Action<string> progress, CancellationToken token)
    {
        item.WriteStatus = "未验证"; item.TestedValues.Clear();
        void Skip(string reason) { item.TestReason = reason; if (!item.WriteVerified) item.WriteStatus = "未试写"; }
        if (CannotTest(item) is { Length: > 0 } unsupported) { Skip(unsupported); return; }
        if (NeedsManualConfirmation(item) && !manualConfirmed) { Skip("会影响菜单、连接或模式；请选择档位后手动测试"); return; }
        // Colour/picture preset restoration observes and restores the known image scope.
        var origin = report.Rounds.FirstOrDefault()?.Readings.FirstOrDefault(r => r.Capability.Definition.Id == item.Definition.Id)?.Capability.Provider;
        if (!item.Definition.Writable && origin != "standard" && origin?.StartsWith("user.detected-", StringComparison.Ordinal) != true) { Skip("当前协议明确只读，请先更新写入定义"); return; }
        if (item.Definition.Id is "color-preset" or "display-preset" && (origin == "standard" || origin?.StartsWith("user.detected-", StringComparison.Ordinal) == true)) {
            var fresh = await workspace.Capture(report.Device, token);
            var current = fresh.Readings.FirstOrDefault(r => r.Capability.Definition.Id == item.Definition.Id);
            var detected = report.Rounds.FirstOrDefault()?.Readings.FirstOrDefault(r => r.Capability.Definition.Id == item.Definition.Id)?.Capability;
            if (current == null || !Good(current.Result)) { Skip("没有读到原设置，请检查连接后重试"); return; }
            if (detected != null && !DisplayWorkspace.SameCapability(detected, current.Capability)) { Skip("控制来源改变，请重新检测"); return; }
            item.ReadStatus = "读取已验证"; item.Samples.Add(Library.Copy(current.Result));
            int probes = item.Probes.Count; var note = item.Note;
            await TestImagePresets(report, item, progress, token, selectedValue);
            item.TestReason = item.WriteVerified ? "写入和恢复通过" : report.RecoveryRequired ? "未确认恢复；先按提示核查" : item.Probes.Count == probes
                ? item.Note.StartsWith(note) && item.Note.Length > note.Length ? item.Note[note.Length..].TrimStart('；') : "当前条件不允许试调，请改变模式后重试"
                : "写入未通过，可选择档位重试";
            return;
        }
        var definition = Library.Copy(item.Definition);
        if (definition.Group == "image" && definition.WriteWhenWindowsHdr != true) definition.WriteWhenWindowsHdr = false;
        var snapshot = await CaptureTrial(report, item, definition, token);
        var reading = snapshot.Readings.Single(r => r.Capability.Definition.Id == definition.Id);
        if (!snapshot.Complete || !Good(reading.Result)) { Skip("没有读到原设置，请检查连接后重试"); return; }
        var originalCapability = report.Rounds.FirstOrDefault()?.Readings.FirstOrDefault(r => r.Capability.Definition.Id == definition.Id)?.Capability;
        if (originalCapability != null && !DisplayWorkspace.SameCapability(originalCapability, reading.Capability)) { Skip("控制来源已改变，请重新检测"); return; }
        item.ReadStatus = "读取已验证"; item.Samples.Add(Library.Copy(reading.Result));
        definition.Writable = true;
        if (definition.ValueType == "enum" && item.Backend == "standard-ddc") report.UseDeclaredStandardChoices(definition);
        if (definition.ValueType == "boolean" && definition.Rules.Count == 0) definition.Rules = [new() { MatchAnyMaximum = true, Values = new() { ["off"] = 0, ["on"] = 1 } }];
        var choices = new List<string>(); string original;
        try {
            original = new Reading { Capability = new() { Definition = definition }, Result = reading.Result }.Semantic(snapshot.Device.WindowsHdrEnabled);
            if (definition.ValueType == "number" && definition.Rules.Count == 0) {
                uint old = reading.Result.Current!.Value, max = reading.Result.Maximum!.Value, step = definition.Step;
                if (old > max || old < definition.Minimum || step == 0 || max < definition.Minimum) { Skip("返回的数值范围异常，不能确定安全测试值"); return; }
                var target = max - old >= step ? old + step : old - definition.Minimum >= step ? old - step : old;
                if (target == old) { Skip("没有不同于原值的合法测试值"); return; }
                choices.Add(target.ToString());
            } else {
                var rule = ProtocolCodec.Rule(definition, reading.Result.Maximum!.Value, snapshot.Device.WindowsHdrEnabled);
                if (rule == null || !rule.Values.ContainsKey(original)) { Skip("原档位或可写档位未知；需补充协议后再测试"); return; }
                choices.AddRange(rule.Values.Where(v => v.Key != original && (definition.AllowedWriteValues == null || definition.AllowedWriteValues.Contains(v.Value))).Select(v => v.Key));
                if (choices.Count == 0) { Skip("没有其他可测试档位"); return; }
            }
            if (selectedValue != null) {
                if (ProtocolCodec.Value(definition, selectedValue, reading.Result.Maximum!.Value, snapshot.Device.WindowsHdrEnabled) == reading.Result.Current) { Skip("测试值与原值相同，请换一个值"); return; }
                choices = [selectedValue];
            }
        } catch (Exception ex) { Skip("测试值不合法：" + ex.Message); return; }
        if (NeedsManualConfirmation(item) && selectedValue == null) { Skip("请选择一个档位，再点击手动测试"); return; }
        int verified = 0; item.TestReason = ""; item.TestedValues.Clear();
        foreach (string value in choices) {
            token.ThrowIfCancellationRequested(); progress("正在测试“" + definition.Name + "”，完成后恢复…");
            if (!await Trial(report, item, definition, value, progress, token)) break;
            verified++;
            if (report.RecoveryRequired) break;
        }
        if (report.RecoveryRequired) { item.TestReason = "未确认恢复；先按提示核查"; return; }
        if (verified > 0) {
            item.Definition = definition; item.WriteStatus = "试写及恢复已验证"; item.TestReason = verified == choices.Count ? "写入和恢复通过" : "部分档位通过，其余可重试";
        } else { item.WriteStatus = "写入未通过"; if (item.TestReason.Length == 0) item.TestReason = "写入或回读未通过；可重试或选择其他测试值"; }
    }

    async Task<Snapshot> CaptureTrial(ReaderSurvey report, SurveyFeature item, ControlDefinition definition, CancellationToken token)
    {
        var now = await workspace.Capture(report.Device, token);
        if (now.Readings.All(r => r.Capability.Definition.Id != definition.Id)) {
            // A newly discovered standard numeric feature can be tested without installing a read-only pack first.
            if (item.Backend != "standard-ddc" || definition.VcpCode is not byte code || !VcpRecognition.IsNumericImage(code) || !definition.Writable)
                throw new InvalidOperationException("写入来源不在当前资料中，请先更新对应协议。");
            var draft = TrialPackage(report, definition);
            var result = await workspace.Backend.Execute(new() { Device = now.Device, Action = "read", ControlId = definition.Id, DraftPackage = draft }, token);
            now.Readings.Add(new() { Capability = new() { Definition = Library.Copy(definition) }, Result = result });
        }
        return now;
    }

    static AdapterPackage TrialPackage(ReaderSurvey report, ControlDefinition definition)
    {
        var controls = new List<ControlDefinition> { Library.Copy(definition) };
        for (int index = 0; index < controls.Count; index++) {
            foreach (string id in controls[index].ReadableWhen.Concat(controls[index].WritableWhen).Select(c => c.FeatureId)) {
                if (controls.Any(c => c.Id == id)) continue;
                var dependency = report.Features.FirstOrDefault(f => f.Definition.Id == id) ?? throw new InvalidOperationException("缺少条件依赖：" + id);
                var copy = Library.Copy(dependency.Definition); copy.Backend = dependency.Backend; copy.Writable = false; controls.Add(copy);
            }
        }
        return new() {
            Id = "user.feature-trial", Name = "功能试写", Author = "Reader", Backend = report.Features.Single(f => f.Definition.Id == definition.Id).Backend,
            Match = new() { Manufacturer = report.Device.Edid.Manufacturer, ProductCode = report.Device.Edid.Product, Name = report.Device.Name },
            Controls = controls, HdrSafety = report.HdrSafety
        };
    }

    async Task<bool> Trial(ReaderSurvey report, SurveyFeature item, ControlDefinition definition, string value, Action<string> progress, CancellationToken token)
    {
        var before = await CaptureTrial(report, item, definition, token);
        var saved = before.Readings.Single(r => r.Capability.Definition.Id == definition.Id);
        if (!Good(saved.Result)) { item.TestReason = "没读到原值，停止试写"; return false; }
        string old = new Reading { Capability = new() { Definition = definition }, Result = saved.Result }.Semantic(before.Device.WindowsHdrEnabled);
        uint target;
        try { target = ProtocolCodec.Value(definition, value, saved.Result.Maximum!.Value, before.Device.WindowsHdrEnabled); }
        catch (Exception ex) { item.TestReason = ex.Message; return false; }
        var working = Library.Copy(before); working.Readings.Single(r => r.Capability.Definition.Id == definition.Id).Capability.Definition = definition;
        var plan = DisplayWorkspace.Preflight(new() { DevicePath = before.Device.DevicePath, Items = [new() { FeatureId = definition.Id, Value = value, Provider = saved.Capability.Provider, Hash = saved.Capability.Hash }] }, working);
        if (DisplayWorkspace.PreflightFailure(plan) is { } reason) { item.TestReason = reason; return false; }
        var draft = TrialPackage(report, definition); Packages.Validate(draft);
        report.RecoveryRequired = true; report.RecoveryBaseline = Library.Copy(before); report.RecoveryObserved = null; item.WriteStatus = "待恢复核查";
        Library.Save("reader-surveys", report.Id, report);
        var probe = new ImageProbe { Value = target }; item.Probes.Add(probe);
        bool verified = false;
        Snapshot? observed = null;
        try {
            var op = await TrialWrite(report, item, saved.Capability, definition, value, before, draft, token);
            probe.OperationId = op.Id; item.OperationId = op.Id; probe.Result = op.Results.Single();
            observed = await CaptureTrial(report, item, definition, token); op.After = Library.Copy(observed); Library.Save("operations", op.Id, op);
            probe.Readings = observed.Readings.Select(Library.Copy).ToList();
            var actual = observed.Readings.Single(r => r.Capability.Definition.Id == definition.Id);
            verified = probe.Result.Confirmed && Good(actual.Result) && actual.Result.Current == target;
            probe.Note = verified ? "写入与独立回读一致" : "写入或回读未通过：" + probe.Result.Message;
        } finally {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try {
                var now = await CaptureTrial(report, item, definition, limit.Token); report.RecoveryObserved = Library.Copy(now);
                var current = now.Readings.Single(r => r.Capability.Definition.Id == definition.Id);
                if (!now.Complete || !HardwareReports.SameDevice(before.Device, now.Device) || before.Device.WindowsHdrEnabled != now.Device.WindowsHdrEnabled
                    || !DisplayWorkspace.SameCapability(saved.Capability, current.Capability) || !Good(current.Result) || current.Result.Maximum != saved.Result.Maximum
                    || observed != null && !ReaderSurvey.RecoveryMatches(observed, now)) throw new IOException("设备、模式或外部设置改变，停止覆盖");
                if (current.Result.Current == target) {
                    var restore = await TrialWrite(report, item, saved.Capability, definition, old, now, draft, limit.Token);
                    if (!restore.Results.Single().Confirmed) throw new IOException("原值恢复未确认");
                    now = await CaptureTrial(report, item, definition, limit.Token); report.RecoveryObserved = Library.Copy(now);
                } else if (current.Result.Current != saved.Result.Current) throw new IOException("当前值与试调值、原值均不符，停止覆盖");
                if (!ReaderSurvey.RecoveryMatches(before, now)) throw new IOException("原值或联动状态没有完全恢复");
                probe.Restored = true; report.RecoveryRequired = false;
                if (verified) {
                    item.TestedValues.Add(target); item.TestedValues.Add(saved.Result.Current!.Value); item.TestedValues = item.TestedValues.Distinct().ToList();
                }
            } catch (Exception ex) { item.WriteStatus = "恢复未确认"; item.TestReason = "原值或联动尚未恢复，请先核查"; item.ErrorDetails = ex.ToString(); }
            Library.Save("reader-surveys", report.Id, report);
        }
        return verified && probe.Restored;
    }

    async Task<Operation> TrialWrite(ReaderSurvey report, SurveyFeature item, Capability expected, ControlDefinition definition, string value, Snapshot before, AdapterPackage draft, CancellationToken token)
    {
        var fresh = (await workspace.Backend.Devices(token)).SingleOrDefault(d => HardwareReports.SameDevice(d, before.Device));
        if (fresh == null || fresh.WindowsHdrEnabled != before.Device.WindowsHdrEnabled) throw new IOException("设备或模式改变，未发送设置");
        var routed = workspace.Backend.Capabilities(fresh).FirstOrDefault(c => c.Definition.Id == definition.Id);
        if (routed != null && (!DisplayWorkspace.SameCapability(expected, routed) || routed.Problem.Length > 0)) throw new IOException("控制来源改变，未发送设置");
        if (routed == null && !(item.Backend == "standard-ddc" && definition.VcpCode is byte code && VcpRecognition.IsNumericImage(code))) throw new IOException("来源已消失，未发送设置");
        var checkedState = Library.Copy(before); checkedState.Readings.Single(r => r.Capability.Definition.Id == definition.Id).Capability.Definition = definition;
        var plan = DisplayWorkspace.Preflight(new() { DevicePath = fresh.DevicePath, Items = [new() { FeatureId = definition.Id, Value = value, Provider = expected.Provider, Hash = expected.Hash }] }, checkedState);
        if (DisplayWorkspace.PreflightFailure(plan) is { } reason) throw new InvalidOperationException(reason);
        // Installed/model definitions stay on their existing provider. Only new standard features use a draft.
        var request = routed == null ? new ControlRequest { Device = fresh, Action = "write", ControlId = definition.Id, Value = value, DraftPackage = draft }
            : DisplayWorkspace.Request(fresh, routed, "write", value);
        if (routed?.Provider == "standard" || routed?.Provider.StartsWith("user.detected-", StringComparison.Ordinal) == true) request.DraftPackage = draft;
        var operation = new Operation { Name = "试写及恢复 " + definition.Name, Before = Library.Copy(before), After = Library.Copy(before), Requests = [request] };
        Library.Save("operations", operation.Id, operation);
        try { operation.Results.Add(await workspace.Backend.Execute(request, token)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { operation.Results.Add(new() { RequestId = request.RequestId, Status = "unknown", Message = ex.Message }); }
        Library.Save("operations", operation.Id, operation); return operation;
    }
}

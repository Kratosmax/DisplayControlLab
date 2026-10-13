namespace MonitorInspector;

public sealed class ImageProbe
{
    public uint Value { get; set; }
    public ControlResult Result { get; set; } = new();
    public List<Reading> Readings { get; set; } = [];
    public string Note { get; set; } = "";
    public bool Restored { get; set; }
    public string OperationId { get; set; } = "";
}

public sealed partial class ReaderSurveyRunner
{
    static readonly string[] ColorScope = ["color-preset", "red-gain", "green-gain", "blue-gain"];
    async Task TestImagePresets(ReaderSurvey report, SurveyFeature item, Action<string> progress, CancellationToken token, string? selectedValue = null)
    {
        bool colour = item.Definition.Id == "color-preset";
        byte code = colour ? (byte)0x14 : (byte)0xDC;
        if (item.ReadStatus != "读取已验证") { item.WriteStatus = "未试写 / 未读到稳定状态"; item.Note += "；跳过该项，其他功能继续检测"; return; }
        if (colour && !item.Definition.Writable || item.Definition.VcpCode != code || item.Backend != "standard-ddc"
            || item.Definition.ValueType != "enum" || item.Definition.WriteWhenWindowsHdr == true
            || colour && (item.Definition.Rules.Count != 1 || !item.Definition.Rules[0].MatchAnyMaximum || item.Definition.Rules[0].WindowsHdr != null)
            || !colour && item.Definition.Rules.Count > 0 && (item.Definition.Rules.Count != 1 || !item.Definition.Rules[0].MatchAnyMaximum || item.Definition.Rules[0].WindowsHdr != null)) return;
        string? source = report.Rounds[0].Readings.Single(r => r.Capability.Definition.Id == item.Definition.Id).Capability.Provider;
        if (source != "standard" && source?.StartsWith("user.detected-", StringComparison.Ordinal) != true) { item.Note += "；型号/第三方图像预设规则不自动改写试调档位"; return; }
        var row = report.Discovery?.Features.FirstOrDefault(r => r.Code.Equals($"0x{code:X2}", StringComparison.OrdinalIgnoreCase) && r.Declared == "显示器声明");
        if (row == null || string.IsNullOrWhiteSpace(row.Values)) { item.Note += "；未声明图像预设档位，不自动猜测试写值"; return; }
        var values = new List<uint>();
        foreach (string text in row.Values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || !uint.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out uint value) || colour && value == 0 || value > ushort.MaxValue) {
                item.Note += "；声明的图像预设取值格式未知，未自动试写"; return;
            }
            // MCCS 0xDC 00–07 are ordinary image modes. Power-saving, demo and private values are not trialled.
            if (!colour && value > 7) { item.Note += $"；档位0x{value:X2}不在常见画面档位范围，跳过试写"; continue; }
            if (colour && value > 255) { item.Note += "；扩展色温档位不自动试写"; return; }
            if (item.Definition.AllowedWriteValues == null || item.Definition.AllowedWriteValues.Contains(value)) values.Add(value);
        }
        values = values.Distinct().ToList();
        if (values.Count is < 2 or > 16 || item.Current is not uint original || !values.Contains(original)) { item.Note += "；原档位不在声明范围内或档位过多，未自动试写"; return; }
        var baseline = await workspace.Capture(report.Device, token);
        var detected = report.Rounds[0].Readings.Single(r => r.Capability.Definition.Id == item.Definition.Id).Capability;
        var current = baseline.Readings.Single(r => r.Capability.Definition.Id == item.Definition.Id);
        if (baseline.Device.WindowsHdrEnabled != false || current.Result.Current != original || !Good(current.Result)
            || !DisplayWorkspace.SameCapability(current.Capability, detected)) { item.WriteStatus = "未试写 / 条件锁定"; return; }
        var scope = colour ? ColorScope : baseline.Readings.Where(r => r.Capability.Definition.Group == "image" && Good(r.Result)).Select(r => r.Capability.Definition.Id).ToArray();
        if (colour && baseline.Readings.Where(r => ColorScope.Contains(r.Capability.Definition.Id)).Count(r => Good(r.Result)) != ColorScope.Length) {
            item.Note += "；RGB 基线不完整，图像预设对比未试写"; return;
        }
        if (colour && report.Features.Where(f => ColorScope.Skip(1).Contains(f.Definition.Id)).Count(f => f.Backend == "standard-ddc"
            && f.Definition.VcpCode == DeviceControl.Standard.Single(c => c.Id == f.Definition.Id).VcpCode) != 3) {
            item.Note += "；RGB来源不是已知标准通道，未自动切换图像预设"; return;
        }
        var definition = Library.Copy(item.Definition); definition.WriteWhenWindowsHdr = false; definition.Writable = true;
        if (!colour) definition.AffectedFeatures = scope.Where(id => id != item.Definition.Id).ToList();
        // Trial values come exclusively from capabilities; labels do not determine SET.
        definition.Rules = [new() { MatchAnyMaximum = true, Values = values.ToDictionary(v => $"code-{v:x2}", v => v),
            Labels = values.ToDictionary(v => $"code-{v:x2}", v => {
                var rule = item.Definition.Rules.FirstOrDefault(r => r.Values.ContainsValue(v));
                var id = rule?.Values.First(p => p.Value == v).Key;
                return id == null ? $"编码 0x{v:X2}（名称未确认）" : rule!.Labels.GetValueOrDefault(id, id);
            }) }];
        var draft = new AdapterPackage { Id = "user.image-probe", Name = item.Definition.Name + "对比", Author = "Reader",
            Match = new() { Manufacturer = report.Device.Edid.Manufacturer, ProductCode = report.Device.Edid.Product, Name = report.Device.Name },
            Controls = [definition], HdrSafety = report.HdrSafety };
        Packages.Validate(draft);
        var preflight = Library.Copy(baseline); preflight.Readings.Single(r => r.Capability.Definition.Id == item.Definition.Id).Capability.Definition = definition;
        if (DisplayWorkspace.PreflightFailure(DisplayWorkspace.Preflight(new() { DevicePath = report.Device.DevicePath,
            Items = [new() { FeatureId = item.Definition.Id, Provider = detected.Provider, Hash = detected.Hash, Value = $"code-{values.First(v => v != original):x2}" }] }, preflight)) is { } problem) {
            item.WriteStatus = "未试写 / 条件锁定"; item.Note += "；" + problem; return;
        }
        var confirmed = new List<uint>();
        var selectedDefinition = Library.Copy(item.Definition); report.UseDeclaredStandardChoices(selectedDefinition);
        uint? selected = selectedValue == null ? null : ProtocolCodec.Value(selectedDefinition, selectedValue, current.Result.Maximum!.Value, baseline.Device.WindowsHdrEnabled);
        if (selected.HasValue && (!values.Contains(selected.Value) || selected == original)) { item.TestReason = "请选择与当前不同且显示器已声明的档位"; return; }
        foreach (uint value in values.Where(v => v != original && (!selected.HasValue || v == selected))) {
            token.ThrowIfCancellationRequested();
            progress($"正在试调{item.Definition.Name}，检查相关设置后恢复原值…");
            var before = await workspace.Capture(report.Device, token);
            if (!ReaderSurvey.RecoveryMatches(baseline, before)) { item.WriteStatus = "未知 / 对比前原状态已改变"; break; }
            report.RecoveryRequired = true; report.RecoveryBaseline = Library.Copy(before); report.RecoveryObserved = null; item.WriteStatus = "待恢复核查";
            var probe = new ImageProbe { Value = value }; item.Probes.Add(probe);
            Library.Save("reader-surveys", report.Id, report);
            Snapshot? observed = null;
            try {
                var operation = await PresetWrite(before, draft, detected, value, "读取器图像预设对比", token);
                probe.OperationId = operation.Id; probe.Result = operation.Results.Single();
                observed = await workspace.Capture(report.Device, token);
                operation.After = Library.Copy(observed); Library.Save("operations", operation.Id, operation);
                probe.Readings = observed.Readings.Where(r => scope.Contains(r.Capability.Definition.Id)).Select(Library.Copy).ToList();
                var actual = observed.Readings.Single(r => r.Capability.Definition.Id == item.Definition.Id);
                bool accepted = probe.Result.Confirmed && probe.Result.Current == value && Good(actual.Result) && actual.Result.Current == value;
                probe.Note = accepted ? (colour ? CompareColors(before, observed) : "画面档位回读已确认；" + string.Join("；", Comparison.Between(before, observed).Select(d => d.Feature + " " + d.Before + "→" + d.After))) : "档位写入未确认，不能推断预设名称";
                if (accepted) confirmed.Add(value);
            } finally {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                try {
                    var now = await workspace.Capture(report.Device, limit.Token);
                    report.RecoveryObserved = Library.Copy(now);
                    if (!SameImageContext(before, now, scope) || observed != null && !ReaderSurvey.RecoveryMatches(observed, now))
                        throw new IOException("设备、来源、模式或外部状态变化，停止自动恢复");
                    var state = now.Readings.Single(r => r.Capability.Definition.Id == item.Definition.Id).Result;
                    if (!Good(state) || state.Current != original && state.Current != value) throw new IOException("当前档位与原值、试调值均不符，停止覆盖");
                    if (state.Current != original) {
                        var restored = await PresetWrite(now, draft, detected, original, "恢复图像预设对比", limit.Token);
                        if (!restored.Results.Single().Confirmed) throw new IOException("图像预设原档位未恢复确认");
                        now = await workspace.Capture(report.Device, limit.Token);
                        report.RecoveryObserved = Library.Copy(now);
                    }
                    if (!SameImageContext(before, now, scope)) throw new IOException("恢复图像预设后图像来源或范围变化，停止写回增益");
                    if (!colour && scope.Contains("color-preset")) {
                        var savedColor = before.Readings.Single(r => r.Capability.Definition.Id == "color-preset");
                        var actualColor = now.Readings.Single(r => r.Capability.Definition.Id == "color-preset");
                        if (!Good(actualColor.Result)) throw new IOException("色温预设恢复回读失败");
                        if (actualColor.Result.Current != savedColor.Result.Current) {
                            if (savedColor.Capability.Definition.VcpCode != 0x14) throw new IOException("色温预设不是已知标准通道，需人工核查");
                            var colorItem = report.Features.Single(f => f.Definition.Id == "color-preset");
                            if (!colorItem.Definition.Writable || colorItem.Backend != "standard-ddc") throw new IOException("色温来源未开放写入，需手动恢复");
                            var colorDefinition = Library.Copy(colorItem.Definition); report.UseDeclaredStandardChoices(colorDefinition);
                            colorDefinition.Writable = true; colorDefinition.WriteWhenWindowsHdr = false;
                            var oldColor = new Reading { Capability = new() { Definition = colorDefinition }, Result = savedColor.Result }.Semantic(before.Device.WindowsHdrEnabled);
                            var colorDraft = TrialPackage(report, colorDefinition);
                            var restored = await TrialWrite(report, colorItem, actualColor.Capability, colorDefinition, oldColor, now, colorDraft, limit.Token);
                            if (!restored.Results.Single().Confirmed) throw new IOException("原色温预设恢复未确认");
                            now = await workspace.Capture(report.Device, limit.Token); report.RecoveryObserved = Library.Copy(now);
                            if (!SameImageContext(before, now, scope)) throw new IOException("恢复色温后来源或模式变化，停止覆盖");
                        }
                    }
                    // Restore only captured, known standard image values if the restored mode allows it.
                    foreach (string id in (colour ? ColorScope.Skip(1) : new[] { "brightness", "contrast", "red-gain", "green-gain", "blue-gain" }.Where(scope.Contains))) {
                        var saved = before.Readings.Single(r => r.Capability.Definition.Id == id);
                        var actual = now.Readings.Single(r => r.Capability.Definition.Id == id);
                        if (actual.Result.Status != "ok") throw new IOException(id + " 恢复回读失败");
                        if (actual.Result.Current == saved.Result.Current) continue;
                        if (saved.Capability.Definition.VcpCode != DeviceControl.Standard.Single(f => f.Id == id).VcpCode) throw new IOException("RGB来源不是标准通道");
                        var restored = await workspace.SetFeature(now.Device, actual.Capability, saved.Result.Current!.Value.ToString(), now, limit.Token);
                        if (!restored.Results.Single().Confirmed) throw new IOException(id + " 原增益恢复未确认");
                        now = await workspace.Capture(report.Device, limit.Token);
                        report.RecoveryObserved = Library.Copy(now);
                        if (!SameImageContext(before, now, scope)) throw new IOException("恢复期间来源或范围变化，停止后续增益写入");
                    }
                    if (!ReaderSurvey.RecoveryMatches(before, now)) throw new IOException("原值或联动状态未完全恢复，需人工核查；未写入音量等无关项目");
                    probe.Restored = true; report.RecoveryRequired = false;
                } catch (Exception ex) { item.WriteStatus = "恢复未确认"; item.Note += "；" + ex.Message; item.ErrorDetails = ex.ToString(); }
                Library.Save("reader-surveys", report.Id, report);
            }
            if (report.RecoveryRequired || !probe.Result.Confirmed) break;
        }
        if (report.RecoveryRequired) return;
        if (item.Probes.Count == 0) return;
        var acceptedValues = confirmed.Concat(item.Probes.Any(p => p.Restored) ? new[] { original } : []).Distinct().ToList();
        bool all = values.All(acceptedValues.Contains);
        item.WriteStatus = all ? "试写及恢复已验证" : "未知 / 图像预设档位未全部确认，原值已核查";
        if (acceptedValues.Count > 1) {
            item.WriteStatus = "试写及恢复已验证";
            item.TestedValues = acceptedValues;
            item.Definition.Writable = true; item.Definition.Rules = definition.Rules; item.Definition.AffectedFeatures = definition.AffectedFeatures;
        }
        item.Note += colour ? "；逐档 RGB 对比已记录，增益不是实测色温，不能自动命名为具体 K 值或 sRGB" : "；逐档画面设置对比与恢复已记录，未确认名称保留编码";
    }
    static bool SameImageContext(Snapshot before, Snapshot now, IEnumerable<string> scope) => now.Complete && HardwareReports.SameDevice(before.Device, now.Device)
        && before.Device.WindowsHdrEnabled == now.Device.WindowsHdrEnabled && scope.All(id => {
            var a = before.Readings.SingleOrDefault(r => r.Capability.Definition.Id == id);
            var b = now.Readings.SingleOrDefault(r => r.Capability.Definition.Id == id);
            return a != null && b != null && DisplayWorkspace.SameCapability(a.Capability, b.Capability) && a.Result.Maximum == b.Result.Maximum;
        });
    async Task<Operation> PresetWrite(Snapshot before, AdapterPackage draft, Capability capability, uint value, string name, CancellationToken token)
    {
        var current = workspace.Backend.Capabilities(before.Device).SingleOrDefault(c => c.Definition.Id == capability.Definition.Id);
        if (current == null || current.Problem.Length > 0 || !DisplayWorkspace.SameCapability(current, capability)) throw new IOException("图像预设来源已变化，未发送试调");
        var request = new ControlRequest { Device = Library.Copy(before.Device), Action = "write", ControlId = capability.Definition.Id, DraftPackage = draft, Value = $"code-{value:x2}" };
        var op = new Operation { Name = name, Before = Library.Copy(before), After = Library.Copy(before), Requests = [request] };
        Library.Save("operations", op.Id, op);
        ControlResult result;
        try { result = await workspace.Backend.Execute(request, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { result = new() { RequestId = request.RequestId, Status = "unknown", Message = "试调结果未知：" + ex.Message }; }
        op.Results.Add(result); DisplayWorkspace.UpdateFromWrite(op.Before, op.After, capability, result);
        Library.Save("operations", op.Id, op); return op;
    }
    internal static string CompareColors(Snapshot before, Snapshot after)
    {
        var changed = ColorScope.Skip(1).Select(id => (id, a: before.Readings.Single(r => r.Capability.Definition.Id == id), b: after.Readings.Single(r => r.Capability.Definition.Id == id))).ToList();
        if (changed.Any(r => !Good(r.a.Result) || !Good(r.b.Result))) return "档位回读已确认，但 RGB 不完整，无法通过色值判断";
        if (changed.All(r => r.a.Result.Current == r.b.Result.Current)) return "档位回读已确认；RGB 增益无变化，无法通过色值判定预设";
        var red = changed[0]; var blue = changed[2]; string tendency = "";
        if (red.a.Result.Maximum is > 0 && blue.a.Result.Maximum is > 0 && red.a.Result.Current > 0 && blue.a.Result.Current > 0
            && red.b.Result.Current > 0 && blue.b.Result.Current > 0) {
            double oldRatio = red.a.Result.Current!.Value / (double)red.a.Result.Maximum!.Value / (blue.a.Result.Current!.Value / (double)blue.a.Result.Maximum!.Value);
            double newRatio = red.b.Result.Current!.Value / (double)red.b.Result.Maximum!.Value / (blue.b.Result.Current!.Value / (double)blue.b.Result.Maximum!.Value);
            if (newRatio > oldRatio * 1.05) tendency = "；相对偏暖线索（红/蓝增益比升高）";
            else if (newRatio < oldRatio / 1.05) tendency = "；相对偏冷线索（红/蓝增益比降低）";
        }
        return "RGB 增益变化：" + string.Join("，", changed.Select(r => r.a.Capability.Definition.Name + " " + r.a.Result.Current + "→" + r.b.Result.Current)) + tendency
            + "；这不是色度测量，预设名称仍以标准/厂商资料或菜单确认";
    }
}

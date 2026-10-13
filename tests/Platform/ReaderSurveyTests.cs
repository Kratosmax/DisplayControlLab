using MonitorInspector;
using System.Text.Json;

internal static class ReaderSurveyTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var saveId = Library.NewId(); Library.Save("save-lock-test", saveId, new { value = 1 });
        string savePath = Path.Combine(Library.Folder("save-lock-test"), saveId + ".json");
        var held = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(async () => { await Task.Delay(120); held.Dispose(); });
        Library.Save("save-lock-test", saveId, new { value = 2 }); await release;
        check(JsonDocument.Parse(File.ReadAllText(savePath)).RootElement.GetProperty("value").GetInt32() == 2, "记录文件短暂被占用时有界重试，最终原子更新成功");
        File.SetAttributes(savePath, FileAttributes.ReadOnly); bool writeDenied = false;
        try { Library.Save("save-lock-test", saveId, new { value = 3 }); } catch (UnauthorizedAccessException) { writeDenied = true; }
        finally { File.SetAttributes(savePath, FileAttributes.Normal); }
        check(writeDenied && JsonDocument.Parse(File.ReadAllText(savePath)).RootElement.GetProperty("value").GetInt32() == 2, "真实只读拒绝保持原文件，不绕过权限或丢失原值");
        async Task<ReaderSurvey> Detect(SurveyBackend backend, bool writes = true, CancellationToken token = default) =>
            await new ReaderSurveyRunner(new DisplayWorkspace(backend)).Run(backend.Device, _ => Task.FromResult(new DdcResult {
                Capabilities = ["vcp(10 12 E0 E1)"], Features = [
                    new() { Code = "0xE0", Current = 7, Maximum = 100, Declared = "显示器声明", Status = "读取成功" },
                    new() { Code = "0xE1", Declared = "显示器声明", Status = "读取失败" },
                    new() { Code = "0x04", Declared = "显示器声明", Status = "跳过命令类项目" },
                    new() { Code = "0x14", Declared = "显示器声明", Values = "0x05, 0x08, 0x0B, 0x0C", Current = 12, Maximum = 12, Status = "读取成功" },
                    new() { Code = "0xDC", Declared = "显示器声明", Values = backend.PictureChoices, Current = backend.Values.GetValueOrDefault("display-preset"), Maximum = 100, Status = "读取成功" }
                ] }), writes, _ => { }, token, reuseVerified: false);
        var backend = new SurveyBackend(); var report = await Detect(backend);
        check(report.Status == "已完成" && !report.RecoveryRequired && backend.Values["brightness"] == 50 && backend.Values["contrast"] == 50, "自动检测试写后恢复所有测试原值");
        check(backend.Writes.Where(r => r.ControlId is not ("color-preset" or "volume")).Select(r => r.ControlId).SequenceEqual(new[] { "brightness", "brightness", "contrast", "contrast", "red-gain", "red-gain", "green-gain", "green-gain", "blue-gain", "blue-gain" }), "数值试写限定标准图像项并逐项恢复，不触碰电源或输入源");
        check(report.Features.Single(f => f.Definition.Id == "vcp.e0").ReadStatus == "读取已验证" && report.Features.Single(f => f.Definition.Id == "vcp.e1").ReadStatus == "未知", "暴露出的未知码多轮只读验证，失败码保留未知");
        check(report.Features.Single(f => f.Definition.VcpCode == 0x04).TestReason.Contains("不可撤销")
            && backend.Writes.All(w => w.ControlId != "vcp.04"), "高危声明项留下禁止原因，不执行GET/SET且不导出");
        var package = Packages.Parse(JsonSerializer.Serialize(report.Package(), Packages.Json));
        check(package.Controls.Single(f => f.Id == "red-gain") is { Writable: true, Verification: "write-confirmed" }
            && backend.Values["red-gain"] == 50 && backend.Values["green-gain"] == 50 && backend.Values["blue-gain"] == 50, "RGB小幅试调与恢复有证据，不仅停留在读取验证");
        var colorProbe = report.Features.Single(f => f.Definition.Id == "color-preset");
        check(colorProbe.Probes.Select(p => p.Value).SequenceEqual(new uint[] { 5, 8, 11 }) && colorProbe.Probes.All(p => p.Result.Confirmed && p.Restored)
            && backend.Values["color-preset"] == 12, "仅试写声明色温档位，逐档回读并恢复原档位与RGB");
        check(colorProbe.Probes.All(p => p.Note.Contains("无法通过色值判定")), "RGB无变化时不从档位回读伪造色温名称");
        var color = package.Controls.Single(f => f.Id == "color-preset");
        check(color.Rules.Single().Labels.Values.Contains("9300K") && color.Rules.Single().Labels.Values.Contains("用户自定义"), "色温逐档试写保留已知标准名称，不被临时编码覆盖");
        var degraded = Library.Copy(colorProbe.Definition);
        foreach (var rule in degraded.Rules) foreach (string id in rule.Values.Keys) rule.Labels[id] = $"编码 0x{rule.Values[id]:X2}（名称未确认）";
        report.UseDeclaredStandardChoices(degraded);
        check(degraded.Rules.Single().Values.Count == 4 && degraded.Rules.Single().Labels.Values.Contains("9300K")
            && degraded.Rules.Single().Labels.Values.Contains("用户自定义") && degraded.Rules.Single().Labels.Values.Count(l => l.Contains("名称未确认")) == 1,
            "旧未知映射从原标准来源恢复名称，仍只显示四个声明档位，0x0C保持未知");
        check(color.Verification == "write-confirmed" && color.AllowedWriteValues!.Order().SequenceEqual(new uint[] { 5, 8, 11, 12 }), "色温导出只开放已确认试调及恢复的编码");
        check(color.Rules.Single().Values.Values.Order().SequenceEqual(new uint[] { 5, 8, 11, 12 }) && color.Rules.Single().Labels["code-0c"].Contains("名称未确认"), "标准色温按显示器声明限制选项，未知名称保留编码不猜含义");
        var namedReport = Library.Copy(report);
        var beforeNames = Library.Copy(namedReport.Features.Single(f => f.Definition.Id == "color-preset"));
        namedReport.SaveOptionNames("color-preset", new Dictionary<string, string> { ["code-0c"] = "我的预设" });
        var reopened = Library.Get<ReaderSurvey>("reader-surveys", namedReport.Id);
        var namedItem = reopened.Features.Single(f => f.Definition.Id == "color-preset");
        check(reopened.Package().Controls.Single(f => f.Id == "color-preset").Rules.Single().Labels["code-0c"] == "我的预设"
            && namedItem.TestedValues.SequenceEqual(beforeNames.TestedValues) && namedItem.WriteStatus == beforeNames.WriteStatus
            && namedItem.Definition.Rules.Single().Values.SequenceEqual(beforeNames.Definition.Rules.Single().Values), "选项命名落盘重开及导出保留名称，不改变控制码、测试结果或白名单");
        bool emptyRejected = false;
        try { namedReport.SaveOptionNames("color-preset", new Dictionary<string, string> { ["code-0c"] = "  " }); } catch (ArgumentException) { emptyRejected = true; }
        check(emptyRejected && namedReport.Features.Single(f => f.Definition.Id == "color-preset").Definition.Rules.Single().Labels["code-0c"] == "我的预设", "空名称不保存、不丢失已有名称");
        var freshRead = await Detect(backend, false);
        var changedMode = Library.Copy(freshRead); changedMode.Device.WindowsHdrEnabled = true;
        freshRead.InheritSavedResults(namedReport);
        var keptColor = freshRead.Features.Single(f => f.Definition.Id == "color-preset");
        check(keptColor.WriteVerified && keptColor.TestedValues.SequenceEqual(beforeNames.TestedValues)
            && freshRead.Package().Controls.Single(f => f.Id == "color-preset").Rules.Single().Labels["code-0c"] == "我的预设",
            "只读重测相同设备来源模式保留名称和写入证明，可直接导出原已测档位");
        Library.Save("reader-surveys", freshRead.Id, freshRead);
        var cachedRead = await new ReaderSurveyRunner(new DisplayWorkspace(backend)).Run(backend.Device,
            _ => Task.FromResult(Library.Copy(report.Discovery!)), false, _ => { }, default);
        check(cachedRead.Features.Single(f => f.Definition.Id == "color-preset").WriteVerified
            && cachedRead.Package().Controls.Single(f => f.Id == "color-preset").Rules.Single().Labels["code-0c"] == "我的预设",
            "真实Run重新只读检测自动继承已保存证明与名称，不需重新试写");
        changedMode.InheritSavedResults(namedReport);
        var invalidColor = changedMode.Features.Single(f => f.Definition.Id == "color-preset");
        check(!invalidColor.WriteVerified && invalidColor.Definition.Rules.Single().Labels["code-0c"] == "我的预设" && invalidColor.TestReason.Contains("需要重试"),
            "HDR模式改变保留名称但不沿用写入权限，明确提示重试");
        check(package.Controls.Single(f => f.Id == "brightness").Writable && package.Controls.All(f => f.Writable && f.Verification == "write-confirmed") && package.Controls.All(f => f.Id is not ("vcp.e0" or "vcp.e1")) && package.MenuHints.Count == 0, "导出实际可导入适配包，未知语义不会开放写入");
        var path = Path.Combine(Library.Folder("survey-test-exports"), "adapter.json"); File.WriteAllText(path, JsonSerializer.Serialize(package, Packages.Json));
        Packages.Choose(backend.Device, "brightness", "standard");
        Packages.Choose(backend.Device, "external.feature", "external.provider");
        var installed = Packages.Install(path); Packages.Attach(backend.Device, installed, false, selectControls: true);
        check(Packages.VerifiedControls(backend.Device)!.SetEquals(package.Controls.Select(f => f.Id)), "应用成功包后控制器精简列表只包含写入通过项，不回退显示未测试内置功能");
        check(CapabilityCatalog.Resolve(backend.Device).Single(c => c.Definition.Id == "brightness").Provider == installed.Id, "生成的适配包安装绑定后进入真实控制器路由");
        var routes = Packages.State().FeatureRoutes;
        check(package.Controls.All(f => routes.GetValueOrDefault(backend.Device.DevicePath + "|" + f.Id) == installed.Id)
            && routes[backend.Device.DevicePath + "|external.feature"] == "external.provider", "应用检测包一次保存所有功能来源，保留包外功能路由");
        Packages.Detach(backend.Device, installed.Id);
        var profileDevice = Library.Copy(backend.Device); profileDevice.DevicePath += "-whole-package";
        Packages.SelectControlPackage(profileDevice, installed.Id);
        check(Packages.ControlPackageFeatures(profileDevice)!.SetEquals(installed.Controls.Select(f => f.Id))
            && CapabilityCatalog.Resolve(profileDevice).Where(c => installed.Controls.Any(f => f.Id == c.Definition.Id)).All(c => c.Provider == installed.Id),
            "整包切换一次路由全部功能，并仅展示选中包实际验证项");
        Packages.SelectControlPackage(profileDevice, "standard");
        check(CapabilityCatalog.Resolve(profileDevice).Where(c => DeviceControl.Standard.Any(f => f.Id == c.Definition.Id)).All(c => c.Provider == "standard")
            && Packages.State().FeatureRoutes.Keys.All(k => !k.StartsWith(profileDevice.DevicePath + "|")), "切回内置标准统一清除旧逐项路由，不混用旧协议包");
        var recoveryBaseline = report.Rounds[0]; var recoveryCurrent = Library.Copy(recoveryBaseline);
        check(ReaderSurvey.RecoveryMatches(recoveryBaseline, recoveryCurrent), "恢复核查接受同设备同来源的完整原值");
        recoveryCurrent.Complete = false;
        check(!ReaderSurvey.RecoveryMatches(recoveryBaseline, recoveryCurrent), "未完成的恢复采样即使数值相同也不能放行");
        recoveryCurrent = Library.Copy(recoveryBaseline); recoveryCurrent.Device.Edid.Product = "0x9999";
        check(!ReaderSurvey.RecoveryMatches(recoveryBaseline, recoveryCurrent), "相同数值的另一型号不冒充原设备已恢复");
        recoveryCurrent = Library.Copy(recoveryBaseline); recoveryCurrent.Readings[0].Capability.Provider = "different.provider";
        check(!ReaderSurvey.RecoveryMatches(recoveryBaseline, recoveryCurrent) && Comparison.Between(recoveryBaseline, recoveryCurrent).Count == 1, "来源变更即使哈希和数值相同也阻止恢复确认");
        check(Library.Get<ReaderSurvey>("reader-surveys", report.Id).Features.Count == report.Features.Count, "检测原始样本与验证状态落盘可重开");
        check(report.Features.All(f => f.TestReason.Length > 0) && report.Features.Single(f => f.Definition.Id == "volume").WriteVerified,
            "每个项目都有试写状态或跳过原因，音量不再被图像白名单排除");
        check(Packages.VerifiedControls(backend.Device) == null, "解除已验证结果包后控制器退出精简列表");
        var prompt = report.AiPrompt();
        check(prompt.Contains("vcp.e1") && prompt.Contains("禁止猜测 SET") && !prompt.Contains(backend.Device.DevicePath) && !prompt.Contains("survey-private-serial"), "AI提示词携带未知项及协议约束并脱敏");
        var blocks = System.Text.RegularExpressions.Regex.Matches(prompt, "```json\\r?\\n(.*?)\\r?\\n```", System.Text.RegularExpressions.RegexOptions.Singleline);
        var example = Packages.Parse(blocks[0].Groups[1].Value); var baseline = Packages.Parse(blocks[1].Groups[1].Value);
        using var compactEvidence = JsonDocument.Parse(blocks[2].Groups[1].Value);
        check(blocks.Count == 3 && example.Controls.Single().Verification == "declared" && baseline.Match.Matches(backend.Device)
            && compactEvidence.RootElement.GetProperty("features").GetArrayLength() == report.Features.Count,
            "零背景提示词内嵌可解析的格式示例、当前适配包及实测报告，不依赖访问仓库");
        var samples = compactEvidence.RootElement.GetProperty("features").EnumerateArray().Select(f => f.TryGetProperty("samples", out var rows) ? JsonSerializer.Deserialize<List<ControlResult>>(rows.GetRawText(), Packages.Json)! : []).ToList();
        bool sameSamples = report.Features.Select((f, i) => f.Samples.Select(s => (s.Status, s.Current, s.Maximum, s.Text, s.Raw, s.Message)).SequenceEqual(samples[i].Select(s => (s.Status, s.Current, s.Maximum, s.Text, s.Raw, s.Message)))).All(x => x);
        check(report.Rounds.All(r => r.Readings.Count > 0) && sameSamples && compactEvidence.RootElement.GetProperty("features").EnumerateArray().Any(f => f.TryGetProperty("probes", out var p) && p.GetArrayLength() > 0),
            "提示词压缩仅去重复，保留全部功能原始响应，不修改原始记录");
        check(prompt.Contains("Reader（读取器）") && prompt.Contains("Controller（控制器）") && prompt.Contains("当前没有 NVIDIA/Intel")
            && prompt.Contains("拒绝未知字段") && prompt.Contains("hdrSafety") && prompt.Contains("writePrefix") && prompt.Contains("格式通过不代表硬件兼容"),
            "提示词自带项目职责、平台边界、完整字段契约、私有帧和验证约束");
        var unfinished = Library.Copy(report); unfinished.Status = "已取消";
        var incompleteBlocks = System.Text.RegularExpressions.Regex.Matches(unfinished.AiPrompt(), "```json\\r?\\n(.*?)\\r?\\n```", System.Text.RegularExpressions.RegexOptions.Singleline);
        check(incompleteBlocks[1].Groups[1].Value == "null", "检测未完成时仍可生成提示词，但不伪造当前可控包");
        var privateBackend = new SurveyBackend(); privateBackend.Values["vendor.private"] = 0;
        var priorPackage = new AdapterPackage { Id = "test.survey-private", Name = "已验证私有包", Author = "test", Backend = "amd-ddc",
            Match = new() { Manufacturer = "TST", ProductCode = "0x1234", Name = privateBackend.Device.Name },
            Controls = [new() { Id = "vendor.private", Name = "私有功能", ValueType = "enum", Writable = true, Verification = "write-confirmed", WriteWhenWindowsHdr = false,
                Raw = new() { ReadPayload = "01 F8 E1 52", WritePrefix = "03 F8 E1 52", ReplyPrefix = "6E 88 02 00 F8 52", MaximumOffset = 6, CurrentOffset = 8 } }] };
        File.WriteAllText(path, JsonSerializer.Serialize(priorPackage, Packages.Json)); priorPackage = Packages.Install(path); Packages.Attach(privateBackend.Device, priorPackage, false);
        privateBackend.Extra.Add(new() { Definition = priorPackage.Controls[0], Provider = priorPackage.Id, Hash = Packages.ContentHash(priorPackage) });
        var privateReport = await Detect(privateBackend, false);
        check(privateBackend.Writes.Count == 0 && privateReport.Features.Single(f => f.Definition.Id == "vendor.private").TestReason.Length > 0,
            "旧适配声明不冒充本轮试写，未测试必须标原因");
        Packages.Detach(privateBackend.Device, priorPackage.Id);
        backend = new SurveyBackend(); report = await Detect(backend, false);
        bool noVerified = false; try { report.Package(); } catch (InvalidOperationException) { noVerified = true; }
        check(backend.Writes.Count == 0 && noVerified, "仅读结果不导出可控包，不能把未试写标准项开放");
        backend = new SurveyBackend { ReadFailure = "brightness" }; report = await Detect(backend);
        check(report.Package().Controls.All(f => f.Id != "brightness") && report.Features.Single(f => f.Definition.Id == "brightness").TestReason.Length > 0,
            "读取失败项保留失败原因但不进导出包");
        backend = new SurveyBackend { ChangeSourceBeforeTrial = true }; report = await Detect(backend);
        check(backend.Writes.Count == 0 && report.Features.Where(f => f.Definition.Id is "brightness" or "contrast").All(f => f.TestReason.Contains("来源")),
            "读取核验后来源被替换，不把新通道当作原标准通道自动试写");
        backend = new SurveyBackend { NoWriteEffect = true }; report = await Detect(backend);
        check(!report.RecoveryRequired && report.Features.Where(f => f.Definition.Id is "brightness" or "contrast").All(f => f.WriteStatus == "写入未通过"), "SET无效果不标为验证通过，核对原值后结束");
        bool emptyFailed = false; try { report.Package(); } catch (InvalidOperationException) { emptyFailed = true; }
        check(emptyFailed, "全部试写无效果时没有可导出的功能，不伪装只读包");
        backend.NoWriteEffect = false;
        await new ReaderSurveyRunner(new DisplayWorkspace(backend)).Retry(report, "brightness", null, false, _ => { }, default);
        check(report.Features.Single(f => f.Definition.Id == "brightness").WriteVerified && report.Package().Controls.Single().Id == "brightness"
            && Library.Get<ReaderSurvey>("reader-surveys", report.Id).Features.Single(f => f.Definition.Id == "brightness").WriteVerified,
            "失败项单独重试通过后落盘并进入可控包，未通过项目不混入");
        var manualBackend = new SurveyBackend(); var manualReport = await Detect(manualBackend, false);
        await new ReaderSurveyRunner(new DisplayWorkspace(manualBackend)).Retry(manualReport, "input-source", "hdmi-1", false, _ => { }, default);
        check(manualBackend.Writes.Count == 0 && manualReport.Features.Single(f => f.Definition.Id == "input-source").TestReason.Contains("手动"),
            "可能黑屏的输入源自动测试被跳过并明确要求手动确认");
        await new ReaderSurveyRunner(new DisplayWorkspace(manualBackend)).Retry(manualReport, "input-source", "hdmi-1", true, _ => { }, default);
        check(manualBackend.Values["input-source"] == 15 && manualReport.Package().Controls.Single() is { Id: "input-source", Writable: true }
            && manualReport.Package().Controls.Single().AllowedWriteValues!.Order().SequenceEqual(new uint[] { 15, 17 }),
            "用户确认手动档位测试后回读恢复成功，只开放所测输入档位");
        var modelBackend = new SurveyBackend(); modelBackend.Values["vendor.level"] = 30; modelBackend.Values["vendor.mode"] = 0;
        var modelPackage = new AdapterPackage { Id = "test.all-writes", Name = "写入验证模型", Author = "test", Match = new() { Manufacturer = "TST", ProductCode = "0x1234", Name = modelBackend.Device.Name },
            Controls = [new() { Id = "vendor.level", Name = "自定义数值", Group = "audio", Writable = true, VcpCode = 0xE2 },
                new() { Id = "vendor.mode", Name = "自定义档位", ValueType = "enum", Group = "game", Writable = true, VcpCode = 0xE3,
                    Rules = [new() { MatchAnyMaximum = true, Values = new() { ["off"] = 0, ["one"] = 1, ["two"] = 2 } }] }] };
        File.WriteAllText(path, JsonSerializer.Serialize(modelPackage, Packages.Json)); modelPackage = Packages.Install(path); Packages.Attach(modelBackend.Device, modelPackage, false);
        modelBackend.Extra.AddRange(modelPackage.Controls.Select(f => new Capability { Definition = f, Provider = modelPackage.Id, Hash = Packages.ContentHash(modelPackage) }));
        var modelReport = await Detect(modelBackend);
        check(modelReport.Features.Where(f => f.Definition.Id.StartsWith("vendor.")).All(f => f.WriteVerified)
            && modelBackend.Writes.Where(w => w.ControlId == "vendor.mode").Select(w => w.Value).SequenceEqual(new[] { "one", "off", "two", "off" })
            && modelBackend.Values["vendor.level"] == 30, "已知型号数值与枚举不受图像白名单限制，逐档真实SET/回读/恢复");
        check(ReaderSurveyRunner.CannotTest(new() { Definition = new() { Id = "factory-reset", VcpCode = 0x04, Writable = true } }).Contains("不可撤销"),
            "恢复出厂等不可撤销命令拒绝试写，不提供绕过入口");
        modelBackend.Values["vendor.level"] = 30;
        modelReport.Features.Single(f => f.Definition.Id == "vendor.level").Definition.WritableWhen = [new() { FeatureId = "volume", EqualsValue = 12 }];
        await new ReaderSurveyRunner(new DisplayWorkspace(modelBackend)).Retry(modelReport, "vendor.level", null, false, _ => { }, default);
        check(modelReport.Features.Single(f => f.Definition.Id == "vendor.level").WriteVerified, "试写草稿保留可读条件依赖，不因单项测试丢失依赖定义");
        Packages.Detach(modelBackend.Device, modelPackage.Id);
        var discoveredBackend = new SurveyBackend(); discoveredBackend.Values["vcp.6c"] = 30;
        var discoveredReport = await Detect(discoveredBackend, false);
        discoveredReport.Features.Add(new() { Definition = new() { Id = "mccs-version", Name = "MCCS版本", VcpCode = 0xDF, ValueType = "status" }, ReadStatus = "读取已验证", Samples = [new() { Status = "ok", Current = 513, Maximum = 65535 }] });
        discoveredReport.Discovery!.Features.Add(new() { Code = "0x6C", Declared = "显示器声明" });
        discoveredReport.Features.Add(new() { Definition = new() { Id = "vcp.6c", Name = "红色黑电平", VcpCode = 0x6C, ValueType = "number", Writable = true },
            CanExport = true, ReadStatus = "读取已验证", Samples = [new() { Status = "ok", Current = 30, Maximum = 100 }] });
        await new ReaderSurveyRunner(new DisplayWorkspace(discoveredBackend)).Retry(discoveredReport, "vcp.6c", null, false, _ => { }, default);
        check(discoveredBackend.Values["vcp.6c"] == 30 && discoveredReport.Package().Controls.Single().Id == "vcp.6c"
            && discoveredBackend.Writes.Where(w => w.ControlId == "vcp.6c").All(w => w.DraftPackage != null),
            "新发现标准数值项直接使用草稿SET及独立GET/恢复，通过才导出，无需先安装只读包");
        backend = new SurveyBackend { FailRestore = true }; report = await Detect(backend);
        bool noEarlyPass = false; try { report.Package(); } catch (InvalidOperationException) { noEarlyPass = true; }
        check(report.RecoveryRequired && noEarlyPass && backend.Writes.All(r => r.ControlId == "brightness"), "恢复失败停止后续试写，但允许导出受影响项只读的结果");
        int priorWrites = backend.Writes.Count; bool blocked = false;
        try { await Detect(backend); } catch (InvalidOperationException) { blocked = true; }
        check(blocked && backend.Writes.Count == priorWrites, "恢复未确认跨次检测仍拦截，不把测试值当成新原值");
        var readPending = await Detect(backend, false);
        check(readPending.RecoveryRequired && readPending.CanPackage && backend.Writes.Count == priorWrites
            && readPending.RecoveryBaseline!.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result.Current == 50,
            "恢复未确认允许重新只读检测，但继承原基线与限制，不把试调状态当新原值");
        backend.Values["brightness"] = 50; backend.FailRestore = false; report = await Detect(backend);
        check(!report.RecoveryRequired && report.Status == "已完成", "手动恢复到原值后只读核查通过，允许重新检测");
        backend = new SurveyBackend { Collateral = true }; report = await Detect(backend);
        check(report.RecoveryRequired && backend.Values["brightness"] == 50 && backend.Writes.All(r => r.ControlId == "brightness"), "观察到联动即停止，不擅自写其他项目");
        backend = new SurveyBackend { Device = new() { Name = "HDR test", Device = "TEST", DevicePath = "survey-hdr", WindowsHdrEnabled = true, Edid = new() { Manufacturer = "TST", Product = "0x1234" } } }; report = await Detect(backend);
        check(backend.Writes.Where(w => w.ControlId != "volume").Count() == 0, "系统HDR开启时图像互锁仍生效，不影响独立音量测试");
        backend = new SurveyBackend { ColorChangesRgb = true }; report = await Detect(backend);
        check(report.Features.Single(f => f.Definition.Id == "color-preset").Probes.Any(p => p.Note.Contains("RGB 增益变化"))
            && backend.Values["red-gain"] == 50 && backend.Values["blue-gain"] == 50 && !report.RecoveryRequired, "色温档位改变RGB时记录实际差值并恢复，不把增益当色度测量");
        check(report.Features.Single(f => f.Definition.Id == "color-preset").Probes.Any(p => p.Note.Contains("相对偏暖线索"))
            && report.Features.Single(f => f.Definition.Id == "color-preset").Probes.Any(p => p.Note.Contains("相对偏冷线索")), "RGB对比只给相对冷暖线索，不捏造具体色温");
        backend = new SurveyBackend { ColorChangesRgb = true, StickyRgb = true }; report = await Detect(backend);
        check(!report.RecoveryRequired && backend.Values["color-preset"] == 12 && backend.Values["red-gain"] == 50 && backend.Values["blue-gain"] == 50
            && backend.Writes.Count(r => r.ControlId == "red-gain") > 2, "恢复色温档位没有自动恢复RGB时，按原基线和当前条件恢复已知增益");
        backend = new SurveyBackend { ColorChangesRgb = true, StickyRgb = true, ChangeRgbSourceOnColorRestore = true }; report = await Detect(backend);
        check(report.RecoveryRequired && backend.Writes.Count(r => r.ControlId is "red-gain" or "green-gain" or "blue-gain") == 6,
            "色温恢复后RGB来源改变，停止增益写回，不向新来源套用旧基线");
        backend = new SurveyBackend { IgnoreColor = true }; report = await Detect(backend);
        check(!report.RecoveryRequired && report.Package().Controls.All(f => f.Id != "color-preset")
            && report.Features.Single(f => f.Definition.Id == "color-preset").Probes.Count == 1, "色温SET无效果停止档位试写，不导出为已验证可写");
        backend = new SurveyBackend { FailColorRestore = true }; report = await Detect(backend);
        check(report.RecoveryRequired && report.Features.Single(f => f.Definition.Id == "color-preset").Probes.Count == 1, "色温恢复失败停止后续档位并保留恢复门禁");
        backend = new SurveyBackend { ColorCollateral = true }; report = await Detect(backend);
        check(report.RecoveryRequired && report.Features.Single(f => f.Definition.Id == "color-preset").Probes.Count == 1, "色温测试发生RGB以外的联动时停止覆盖并提示人工核查");
        var pendingPackage = Packages.Parse(JsonSerializer.Serialize(report.Package(), Packages.Json));
        check(pendingPackage.Controls.Single(f => f.Id == "brightness").Writable && pendingPackage.Controls.Single(f => f.Id == "contrast").Writable
            && pendingPackage.Controls.All(f => f.Id is not ("color-preset" or "red-gain" or "green-gain" or "blue-gain" or "volume")), "色温恢复未确认仅限制预设/RGB及实测音量联动，亮度对比度仍可导出");
        var unknownRecovery = Library.Copy(report); unknownRecovery.RecoveryObserved = null; unknownRecovery.Features.Single(f => f.Definition.Id == "color-preset").Probes.Clear();
        bool missingEvidence = false; try { unknownRecovery.Package(); } catch (InvalidOperationException) { missingEvidence = true; }
        check(missingEvidence, "恢复证据不足仍可导出读取资料，但不猜哪些功能可安全写入");
        var dependent = Library.Copy(report); dependent.Features.Single(f => f.Definition.Id == "brightness").Definition.WritableWhen = [new() { FeatureId = "color-preset", EqualsValue = 12 }];
        check(dependent.Package().Controls.All(f => f.Id != "brightness"), "受恢复项条件依赖的功能同样保持只读");
        backend = new SurveyBackend { ReadFailure = "color-preset" }; report = await Detect(backend);
        check(!report.RecoveryRequired && report.CanPackage && backend.Writes.All(w => w.ControlId != "color-preset") && report.Package().Controls.Single(f => f.Id == "brightness").Writable,
            "没有稳定色温读数跳过该项，不阻止其他功能与导出");
        backend = new SurveyBackend { PictureChoices = "0x00, 0x01, 0x03", PictureChangesImage = true }; backend.Values["display-preset"] = 0;
        report = await Detect(backend);
        var picture = report.Features.Single(f => f.Definition.Id == "display-preset");
        check(picture.Probes.Select(p => p.Value).SequenceEqual(new uint[] { 1, 3 }) && picture.Probes.All(p => p.Restored)
            && backend.Values["display-preset"] == 0 && backend.Values["brightness"] == 50 && !report.RecoveryRequired, "声明画面档位逐档试写并核查联动，恢复画面预设及亮度");
        check(report.Package().Controls.Single(f => f.Id == "display-preset") is { Writable: true, Verification: "write-confirmed" }
            && picture.TestedValues.Order().SequenceEqual(new uint[] { 0, 1, 3 }), "画面预设通过后只开放实测档位，未确认名称保留编码");
        backend = new SurveyBackend { PictureChoices = "0x00, 0x01, 0x03", PictureChangesColor = true }; backend.Values["display-preset"] = 0;
        report = await Detect(backend, false);
        await new ReaderSurveyRunner(new DisplayWorkspace(backend)).Retry(report, "display-preset", "code-01", true, _ => { }, default);
        check(!report.RecoveryRequired && backend.Values["color-preset"] == 12 && backend.Values["display-preset"] == 0
            && report.Features.Single(f => f.Definition.Id == "display-preset").TestedValues.Order().SequenceEqual(new uint[] { 0, 1 })
            && backend.Writes.Any(w => w.ControlId == "color-preset"), "手动画面单档试写走图像恢复，恢复原色温，仅开放所测与原档位");
        await new ReaderSurveyRunner(new DisplayWorkspace(backend)).Retry(report, "display-preset", "code-03", true, _ => { }, default);
        check(report.Features.Single(f => f.Definition.Id == "display-preset").TestedValues.Order().SequenceEqual(new uint[] { 0, 1, 3 }),
            "连续单档成功试写累计证据，后一次不丢掉之前已通过档位");
        backend = new SurveyBackend { PictureChoices = "0x00, 0x01", PictureChangesImage = true, StickyPictureImage = true }; backend.Values["display-preset"] = 0; report = await Detect(backend);
        check(!report.RecoveryRequired && backend.Values["brightness"] == 50 && backend.Writes.Count(w => w.ControlId == "brightness") == 3,
            "画面预设恢复后亮度仍有残留变化时，按已知标准通道恢复原亮度并完整核查");
        backend = new SurveyBackend { PictureChoices = "0x00, 0x01, 0x0A, 0xFF" }; backend.Values["display-preset"] = 0; report = await Detect(backend);
        check(backend.Writes.Where(w => w.ControlId == "display-preset").All(w => w.Value is "code-01" or "code-00")
            && report.Package().Controls.Single(f => f.Id == "display-preset").AllowedWriteValues!.Order().SequenceEqual(new uint[] { 0, 1 }),
            "画面预设声明含演示或私有档位时只测常见图像档位，不开放未测试值");
        backend = new SurveyBackend(); backend.Values["display-preset"] = 0; report = await Detect(backend);
        check(backend.Writes.All(w => w.ControlId != "display-preset") && report.Features.Single(f => f.Definition.Id == "display-preset").Note.Contains("未声明"), "画面档位未声明不猜SET，不妨碍继续检测");
        backend = new SurveyBackend { PictureChoices = "0x00, 0x01", ReadFailure = "color-preset" }; backend.Values["display-preset"] = 0; report = await Detect(backend);
        check(!report.RecoveryRequired && report.Package().Controls.Single(f => f.Id == "display-preset").Writable
            && backend.Writes.Any(w => w.ControlId == "display-preset") && backend.Writes.All(w => w.ControlId != "color-preset"),
            "色温不可读不会阻止独立画面预设试写，只核查可读取图像项和其他状态变化");
        backend = new SurveyBackend { PictureChoices = "0x00, 0x01", FailPictureRestore = true }; backend.Values["display-preset"] = 0; report = await Detect(backend);
        check(report.RecoveryRequired && report.Features.Single(f => f.Definition.Id == "display-preset").Probes.Count == 1
            && backend.Writes.Count(w => w.ControlId == "color-preset") == 6 && report.Package().Controls.All(f => f.Group != "image"), "画面预设恢复失败限制所有图像项、停止后续色温试写，仍可导出只读结果");
        using var colorCancellation = new CancellationTokenSource();
        backend = new SurveyBackend { CancelColorSet = colorCancellation, ColorChangesRgb = true };
        try { await Detect(backend, token: colorCancellation.Token); } catch (OperationCanceledException) { }
        var colorCancelled = Library.All<ReaderSurvey>("reader-surveys").First(r => r.Device.DevicePath == backend.Device.DevicePath);
        check(colorCancelled.Status == "已取消" && !colorCancelled.RecoveryRequired && backend.Values["color-preset"] == 12
            && backend.Values["red-gain"] == 50 && backend.Values["blue-gain"] == 50, "色温SET后取消也独立限时恢复档位与RGB，不继续后续档位");
        using var cancelled = new CancellationTokenSource();
        backend = new SurveyBackend { CancelAfterSet = cancelled };
        try { await Detect(backend, token: cancelled.Token); } catch (OperationCanceledException) { }
        var saved = Library.All<ReaderSurvey>("reader-surveys").First(r => r.Device.DevicePath == backend.Device.DevicePath && r.Time >= report.Time);
        check(backend.Values["brightness"] == 50 && !saved.RecoveryRequired && backend.Writes.All(r => r.ControlId == "brightness"), "SET后取消仍用独立限时核查并恢复原值");
    }

    sealed class SurveyBackend : IDisplayBackend
    {
        public MonitorData Device { get; set; } = new() { Name = "Survey test", Device = "TEST", DevicePath = "survey-" + Guid.NewGuid().ToString("N"), WindowsHdrEnabled = false, Edid = new() { Manufacturer = "TST", Product = "0x1234", Serial = "survey-private-serial" } };
        public Dictionary<string, uint> Values { get; } = new() { ["brightness"] = 50, ["contrast"] = 50, ["red-gain"] = 50, ["green-gain"] = 50, ["blue-gain"] = 50, ["color-preset"] = 12, ["input-source"] = 15, ["power"] = 1, ["volume"] = 12, ["vcp.e0"] = 7 };
        public List<ControlRequest> Writes { get; } = [];
        public List<Capability> Extra { get; } = [];
        public string? ReadFailure { get; set; }
        public bool FailRestore { get; set; }
        public bool NoWriteEffect { get; set; }
        public bool Collateral { get; set; }
        public CancellationTokenSource? CancelAfterSet { get; set; }
        public bool ChangeSourceBeforeTrial { get; set; }
        public bool ColorChangesRgb { get; set; }
        public bool IgnoreColor { get; set; }
        public bool FailColorRestore { get; set; }
        public bool ColorCollateral { get; set; }
        public bool StickyRgb { get; set; }
        public CancellationTokenSource? CancelColorSet { get; set; }
        public bool ChangeRgbSourceOnColorRestore { get; set; }
        public string PictureChoices { get; set; } = "";
        public bool PictureChangesImage { get; set; }
        public bool PictureChangesColor { get; set; }
        public bool StickyPictureImage { get; set; }
        public bool FailPictureRestore { get; set; }
        bool rgbSourceChanged;
        int catalogueReads;
        public Task<List<MonitorData>> Devices(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(new List<MonitorData> { Library.Copy(Device) }); }
        public List<Capability> Capabilities(MonitorData device) {
            catalogueReads++;
            return DeviceControl.Standard.Where(f => Values.ContainsKey(f.Id)).Select(f => new Capability { Definition = Library.Copy(f),
                Provider = ChangeSourceBeforeTrial && catalogueReads > 3 || rgbSourceChanged && f.Id is "red-gain" or "green-gain" or "blue-gain" ? "changed.provider" : "standard" }).Concat(Extra).ToList();
        }
        public Task<ControlResult> Execute(ControlRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.Action == "read" && (request.ControlId == ReadFailure || request.ControlId == "vcp.e1")) return Task.FromResult(new ControlResult { Status = "error", Message = "test unreadable" });
            if (request.Action == "write") {
                Writes.Add(Library.Copy(request)); var definition = request.DraftPackage?.Controls.Single(f => f.Id == request.ControlId) ?? Capabilities(Device).Single(c => c.Definition.Id == request.ControlId).Definition;
                uint desired = ProtocolCodec.Value(definition, request.Value!, 100, Device.WindowsHdrEnabled);
                if (FailRestore && desired == 50) return Task.FromResult(new ControlResult { Status = "error", Message = "test restore failure" });
                if (FailColorRestore && request.ControlId == "color-preset" && desired == 12) return Task.FromResult(new ControlResult { RequestId = request.RequestId, Status = "error", Message = "color restore failure" });
                if (FailPictureRestore && request.ControlId == "display-preset" && desired == 0) return Task.FromResult(new ControlResult { RequestId = request.RequestId, Status = "error", Message = "picture restore failure" });
                uint before = Values[request.ControlId];
                bool applied = !NoWriteEffect && !(IgnoreColor && request.ControlId == "color-preset");
                if (applied) Values[request.ControlId] = desired;
                if (applied && PictureChangesImage && request.ControlId == "display-preset" && !(StickyPictureImage && desired == 0)) Values["brightness"] = desired == 0 ? 50u : 60u;
                if (applied && PictureChangesColor && request.ControlId == "display-preset") Values["color-preset"] = 5;
                if (applied && ColorChangesRgb && request.ControlId == "color-preset" && !(StickyRgb && desired == 12)) { Values["red-gain"] = desired == 5 ? 70u : desired == 8 ? 30u : 50u; Values["blue-gain"] = desired == 5 ? 30u : desired == 8 ? 70u : 50u; }
                if (applied && ColorCollateral && request.ControlId == "color-preset") Values["volume"] = 13;
                if (Collateral) Values["red-gain"] = 40;
                if (desired != 50) CancelAfterSet?.Cancel();
                if (request.ControlId == "color-preset" && desired != 12) CancelColorSet?.Cancel();
                if (request.ControlId == "color-preset" && desired == 12 && ChangeRgbSourceOnColorRestore) rgbSourceChanged = true;
                return Task.FromResult(new ControlResult { RequestId = request.RequestId, Status = "ok", Sent = true, Confirmed = applied, Before = before, BeforeMaximum = 100, Current = Values[request.ControlId], Maximum = 100 });
            }
            return Task.FromResult(new ControlResult { Status = "ok", Current = Values[request.ControlId], Maximum = 100 });
        }
    }
}

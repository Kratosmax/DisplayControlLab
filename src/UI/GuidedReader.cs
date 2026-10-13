using System.IO;
using System.Windows;
using System.Windows.Controls;
using MonitorInspector;

namespace DisplayControl.UI;

public partial class WorkspaceWindow
{
    async Task RunGuidedReaderTests(string directory, Action<bool, string> check)
    {
        IEnumerable<DependencyObject> Descendants(DependencyObject node) {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        var fixture = (SimulatedDisplayBackend)workspace.Backend;
        var original = Library.Copy(snapshot!); var value = fixture.Values["brightness"];
        fixture.Requests.Clear(); readSessions.Clear();
        Navigate("guided");
        if (reader) check(advancedNavigation?.IsExpanded == false, "读取器默认折叠高级工具");
        check(navigationButtons.All(b => (string)b.Tag is not ("snapshots" or "experiments")), "删除重复的快照与分段实验导航");
        Capture(Path.Combine(directory, "reader-start.png"), "guided", 1040, 700, 1.25);
        var manualSection = Descendants(body).OfType<Expander>().Single(e => Equals(e.Header, "手动查找菜单变化（自动检测没找到时）"));
        manualSection.IsExpanded = true;
        await ReadAndCompare(default); var id = ReadSession!.Id;
        check(Descendants(body).OfType<Expander>().Single(e => Equals(e.Header, "手动查找菜单变化（自动检测没找到时）")).IsExpanded,
            "手动查找菜单展开后，首次读取重建页面仍保持展开");
        fixture.Values["brightness"] = value == 85 ? 84u : 85u;
        await ReadAndCompare(default);
        check(Descendants(body).OfType<Expander>().Single(e => Equals(e.Header, "手动查找菜单变化（自动检测没找到时）")).IsExpanded,
            "手动菜单连续读取不自动折叠");
        var readingsSection = Descendants(body).OfType<Expander>().Single(e => Equals(e.Header, "查看本次所有读数"));
        readingsSection.IsExpanded = true; ShowPage();
        check(Descendants(body).OfType<Expander>().Single(e => Equals(e.Header, "查看本次所有读数")).IsExpanded,
            "嵌套折叠区手动展开状态保留");
        Descendants(body).OfType<Expander>().Single(e => Equals(e.Header, "手动查找菜单变化（自动检测没找到时）")).IsExpanded = false;
        ShowPage();
        check(!Descendants(body).OfType<Expander>().Single(e => Equals(e.Header, "手动查找菜单变化（自动检测没找到时）")).IsExpanded,
            "用户主动收起后也保持收起");
        var saved = Library.Get<Experiment>("experiments", id);
        check(saved.Segments.Count == 2 && saved.Compare(0, 1).Any(d => d.Feature == "亮度"), "两次点击自动保存并找出菜单变化");
        check(fixture.Requests.All(r => r.Action == "read"), "手动对比全程仅读取，不写硬件");
        Capture(Path.Combine(directory, "reader-changes.png"), "guided", 1480, 900, 1);
        using (var cancelled = new CancellationTokenSource()) {
            cancelled.Cancel(); try { await ReadAndCompare(cancelled.Token); } catch (OperationCanceledException) { }
            check(ReadSession!.Segments.Count == 2, "取消读取不替换对比起点");
        }
        suppressDeviceChange = true; devices.SelectedIndex = 1; suppressDeviceChange = false;
        check(ReadSession == null, "切换显示器不混用另一台的对比起点");
        suppressDeviceChange = true; devices.SelectedIndex = 0; suppressDeviceChange = false;
        fixture.Values["brightness"] = value; fixture.Values["hdr-preset"] = 0;
        var start = Descendants(body).OfType<Button>().Single(b => Equals(b.Content, "开始自动检测"));
        start.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(active != null && !devices.IsEnabled && !body.IsEnabled, "自动检测按钮进入忙碌状态，防止重复操作与换屏");
        for (int i = 0; active != null && i < 3000; i++) await Task.Delay(10);
        check(active == null && uiMessage.Contains("检测完成"), "真实自动检测按钮完成并反馈结果");
        var survey = Library.All<ReaderSurvey>("reader-surveys").First(r => HardwareReports.SameDevice(r.Device, device!));
        openedFeaturePanels[device!.DevicePath] = "color-preset";
        ShowPage();
        check(Descendants(body).OfType<TextBlock>().Any(t => t.Text == "测试“色温预设”"), "页面重建保留正在打开的色温选项面板");
        openedFeaturePanels.Remove(device.DevicePath);
        var resultTable = Descendants(body).OfType<DataGrid>().First();
        check(resultTable.Columns.Any(c => Equals(c.Header, "当前状态"))
            && resultTable.Items.Cast<object>().Any(row => (row.GetType().GetProperty("Current")?.GetValue(row) as string)?.Contains("暖色（厂商候选）") == true), "检测结果复用状态名称，AOC色温显示厂商候选而非猜测K值");
        check(survey.Status == "已完成" && !survey.RecoveryRequired && fixture.Values["brightness"] == value, "一键检测完成并恢复亮度原值");
        var package = Packages.Parse(System.Text.Json.JsonSerializer.Serialize(survey.Package(), Packages.Json));
        check(package.Controls.Single(f => f.Id == "brightness").Verification == "write-confirmed" && package.Controls.All(f => f.Id != "color-preset"), "未试写色温不进入控制包，导出仅保留实际通过项");
        check(fixture.Requests.Where(r => r.Action == "write").All(r => r.ControlId is "brightness" or "contrast" or "red-gain" or "green-gain" or "blue-gain" or "volume"), "自动验证覆盖独立音量，仍跳过条件锁定RGB/未声明色温及高风险项");
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) Capture(Path.Combine(directory, $"reader-result-{scale}.png"), "guided", 1040, 700, scale);
        var prompt = survey.AiPrompt(); check(prompt.Contains("实测资料") && !prompt.Contains("\"devicePath\": \"" + device!.DevicePath + "\"") && !prompt.Contains("\"devicePath\":\"" + device.DevicePath + "\""), "私有协议提示词包含证据并隐藏设备路径");
        RunProtocolPromptTests(directory, check, prompt);
        var compared = await new ReaderSurveyRunner(workspace).Run(device!, _ => Task.FromResult(new DdcResult { Features = [new() {
            Code = "0x14", Declared = "显示器声明", Values = "0x05, 0x08, 0x0B", Current = 5, Maximum = 100 }] }), true, Say, default);
        check(compared.Features.Single(f => f.Definition.Id == "color-preset").Probes.Count == 2
            && compared.Features.Single(f => f.Definition.Id == "color-preset").Probes.All(p => p.Restored)
            && fixture.Values["color-preset"] == 5, "真实读取器宿主逐档对比声明色温并恢复，不Show或写硬件");
        ShowPage();
        check(Descendants(body).OfType<Expander>().Any(e => Equals(e.Header, "色温预设：查看试调对比")), "色温对比证据在结果页可直接展开，不必导出给AI");
        var colorEvidence = Descendants(body).OfType<Expander>().Single(e => Equals(e.Header, "色温预设：查看试调对比"));
        ((Panel)colorEvidence.Parent).Children.Remove(colorEvidence);
        var evidenceHost = new Grid { Width = 700, Background = System.Windows.Media.Brushes.White };
        evidenceHost.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DisplayControl.UI;component/Theme.xaml", UriKind.Relative) });
        System.Windows.Documents.TextElement.SetFontFamily(evidenceHost, FontFamily);
        evidenceHost.Children.Add(colorEvidence);
        colorEvidence.IsExpanded = true;
        evidenceHost.Measure(new Size(700, double.PositiveInfinity)); evidenceHost.Arrange(new Rect(0, 0, 700, evidenceHost.DesiredSize.Height)); evidenceHost.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(700, (int)Math.Ceiling(evidenceHost.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var background = new System.Windows.Media.DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(background); bitmap.Render(evidenceHost);
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        check(pixels.Where((pixel, index) => index % 4 != 3 && pixel < 200).Count() > 100, "色温对比展开内容真实渲染，截图不是空白");
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(directory, "color-probe-evidence-expanded.png"))) encoder.Save(output);
        Capture(Path.Combine(directory, "reader-color-probes.png"), "guided", 1480, 1100, 1);
        await RunRecoveryGuideTests(directory, check, original, fixture);
        await RunFeatureTestPanelTests(directory, check, survey, fixture);
        snapshot = original; StoreSnapshot(original, true); readSessions.Clear();
    }

    void GuidedPage()
    {
        if (device == null) { body.Children.Add(Card(Section("先连接显示器", "检查连接线和电源，再点左侧“刷新设备”。"))); return; }
        var intro = Section("看看这台显示器能调什么", "自动检查可读取、可调节的功能，结果可以直接交给控制器使用。");
        var writes = new CheckBox { Content = "测试所有可安全试写的功能，结束后恢复原设置", IsChecked = true, Margin = new Thickness(0, 0, 0, 12) };
        intro.Children.Add(writes);
        intro.Children.Add(Text("检测时请不要操作显示器菜单或其他控制软件。勾选试调后，画面可能短暂变亮或偏色；不会切换 HDR、输入源或电源。", 13, "#64748C"));
        intro.Children.Add(AsyncButton("开始自动检测", async t => {
            if (device == null) return;
            bool testWrites = writes.IsChecked == true;
            if (testWrites && !simulation && !Confirm("会短暂试调亮度、对比度和颜色，再恢复原设置。请暂停其他控制软件，检测时不要操作显示器菜单。无法确认恢复时会暂停并请你检查。开始？")) return;
            var selected = Library.Copy(device);
            try {
                await new ReaderSurveyRunner(workspace).Run(selected, token => simulation ? Task.FromResult(new DdcResult()) : ScanExpanded(selected, token), testWrites, Say, t);
                Say("检测完成，结果已保存。可以直接生成并应用到控制器。");
            } finally { ShowPage(); }
        }, true));
        var report = Library.All<ReaderSurvey>("reader-surveys").FirstOrDefault(r => HardwareReports.SameDevice(r.Device, device));
        report?.RestoreGeneratedStandardDefinitions();
        if (report?.RecoveryRequired == true) writes.IsChecked = false;
        if (report?.Status == "已完成") body.Children.Add(new Expander { Header = "重新检测 / 修改检测选项", Content = Card(intro, "#E9EDFF"), Margin = new Thickness(0, 0, 0, 18) });
        else body.Children.Add(Card(intro, "#E9EDFF"));
        if (report != null) {
            var results = Section(report.RecoveryRequired ? "检测暂停，请先确认设置已恢复" : report.Status, $"{report.Time:MM-dd HH:mm:ss} · 已读取 {report.Features.Count(f => f.ReadStatus == "读取已验证")} 项 · 试调并恢复成功 {report.Features.Count(f => f.WriteStatus == "试写及恢复已验证")} 项");
            if (report.RecoveryRequired) {
                results.Children.Add(Text("可先应用验证通过且无关的功能。点击橙色行确认恢复后，再重试其余项目。", 14, "#9A6612"));
            }
            var actions = Actions();
            var export = Button("导出控制器适配包", () => ExportJson(report.Package(), "adapter.json"), true);
            export.IsEnabled = CanExportVerified(report);
            actions.Children.Add(export);
            var apply = Button("直接应用到控制器", () => {
                var package = report.Package();
                if (!Confirm($"将 {report.Device.DisplayName} 的 {package.Controls.Count} 项检测结果交给控制器使用？未确认的功能不会开放调节，已有适配仍保留。")) return;
                var path = Path.Combine(Library.Folder("generated-adapters"), report.Id + ".json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(package, Packages.Json));
                var installed = Packages.Install(path, true); Packages.Attach(report.Device, installed, false, selectControls: true);
                Say("已应用到当前设备，控制器重新加载后即可使用；原有协议包仍保留。");
            });
            apply.IsEnabled = export.IsEnabled; actions.Children.Add(apply);
            actions.Children.Add(Button("查看 / 复制 AI 提示词", () => { if (IsVisible) CreateProtocolPromptWindow(report.AiPrompt()).ShowDialog(); }));
            results.Children.Add(actions);
            results.Children.Add(Text("只把写入验证通过的功能交给控制器。其他项目留在这里，点击“测试 / 重试”查看原因并继续测试。", 13, "#64748C"));
            var table = Table(); table.Columns.Add(Column("功能", "Definition.Name", 1.1)); table.Columns.Add(Column("用途识别", "Recognition", .7)); table.Columns.Add(Column("读取验证", "ReadStatus", .9)); table.Columns.Add(Column("控制与验证", "ControlStatus", 1.4)); table.Columns.Add(Column("当前状态", "Current", 1));
            var blocked = report.RecoveryBlockedFeatures();
            var exported = new HashSet<string>(); if (CanExportVerified(report)) exported.UnionWith(report.Package().Controls.Select(f => f.Id));
            table.RowHeight = double.NaN; table.MinRowHeight = 40;
            table.ItemsSource = report.Features.Select(f => new { Feature = f, NeedsRecovery = NeedsRecovery(report, f), f.Definition, f.ReadStatus,
                ControlStatus = NeedsRecovery(report, f) ? "待核查 · 点击查看步骤" : blocked.Contains(f.Definition.Id) ? "受恢复影响 · 暂不导出" : f.WriteVerified && !exported.Contains(f.Definition.Id) ? "依赖项未通过 · 暂不导出" : f.ControlStatus, f.Recognition,
                Current = new Reading { Capability = new() { Definition = f.Definition }, Result = f.Samples.LastOrDefault() ?? new() }.Display(report.Device.WindowsHdrEnabled) }).ToList();
            results.Children.Add(table);
            var recoveryDetails = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
            ConfigureRecoveryRows(table, report, recoveryDetails);
            AddFeatureTestColumn(table, report, recoveryDetails);
            if (openedFeaturePanels.TryGetValue(report.Device.DevicePath, out var openedId) && report.Features.FirstOrDefault(f => f.Definition.Id == openedId) is { } opened) {
                recoveryDetails.Children.Add(NeedsRecovery(report, opened) ? RecoveryGuide(report, opened) : FeatureTestPanel(report, opened)); recoveryDetails.Visibility = Visibility.Visible;
            }
            results.Children.Add(recoveryDetails);
            table.Columns[0].MinWidth = 140; table.Columns[1].MinWidth = 90; table.Columns[2].MinWidth = 105; table.Columns[3].MinWidth = 180; table.Columns[4].MinWidth = 90;
            foreach (DataGridTextColumn column in table.Columns.OfType<DataGridTextColumn>()) column.ElementStyle = new Style(typeof(TextBlock)) {
                Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap), new Setter(FrameworkElement.ToolTipProperty, column.Binding), new Setter(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 5)) }
            };
            var details = new StackPanel();
            details.Children.Add(Text($"用途识别：公开标准 {report.Features.Count(f => f.Recognition == "公开标准")} 项 · 厂商候选 {report.Features.Count(f => f.Recognition == "厂商候选")} 项 · 未知 {report.Features.Count(f => f.Recognition == "用途未知")} 项。读取稳定不等于用途或写入已确认。", 13));
            foreach (var f in report.Features) details.Children.Add(Text(f.Definition.Name + "：" + f.Note, 12, "#64748C"));
            foreach (var f in report.Features.Where(f => f.Probes.Count > 0)) {
                var probes = new StackPanel();
                foreach (var probe in f.Probes) probes.Children.Add(Text($"编码 0x{probe.Value:X2} · {(probe.Result.Confirmed ? "回读确认" : "未确认")} · {(probe.Restored ? "原值已恢复" : "恢复未确认")}\n{probe.Note}", 13, "#465671"));
                results.Children.Add(new Expander { Header = f.Definition.Name + "：查看试调对比", Content = probes, Margin = new Thickness(0, 8, 0, 0) });
            }
            details.Children.Add(Text("自动验证只证明当前模式下的通信回读；未知私有命令和实际画面效果仍需资料或人工确认。", 13, "#64748C"));
            results.Children.Add(new Expander { Header = "查看验证依据与限制", Content = details, Margin = new Thickness(0, 12, 0, 0) });
            body.Children.Add(Card(results));
        }
        var manual = new StackPanel(); ManualReaderPage(manual);
        body.Children.Add(new Expander { Header = "手动查找菜单变化（自动检测没找到时）", Content = manual, Margin = new Thickness(0, 0, 0, 18) });
    }

    static bool CanExportVerified(ReaderSurvey report)
    {
        if (!report.CanPackage) return false;
        try { return report.Package().Controls.Count > 0; } catch (InvalidOperationException) { return false; }
    }

    readonly Dictionary<string, Experiment> readSessions = [];
    Experiment? ReadSession => device != null && readSessions.TryGetValue(device.DevicePath, out var value)
        && HardwareReports.SameDevice(device, value.Device) ? value : null;

    void ManualReaderPage(StackPanel target)
    {
        if (device == null) { target.Children.Add(Card(Section("先连接显示器", "检查连接线和电源，再点左侧“刷新设备”。"))); return; }
        var session = ReadSession;
        var displayReport = Library.All<ReaderSurvey>("reader-surveys").FirstOrDefault(r => HardwareReports.SameDevice(r.Device, device));
        var samples = session?.Segments.Where(s => !s.Transition).SelectMany(s => s.Rounds).Where(s => s.Complete).ToList() ?? [];
        var intro = Section(samples.Count == 0 ? "① 先读取一次" : "② 改一项菜单，再读取", samples.Count == 0
            ? $"已选择：{device.DisplayName}。点击下方按钮，记住显示器现在的状态。"
            : "用显示器自己的按键打开菜单，只改一项，等画面稳定后点击下方按钮。每次与上一次读取自动对比。");
        var actions = Actions();
        actions.Children.Add(AsyncButton(samples.Count == 0 ? "开始读取" : "读取并查看变化", ReadAndCompare, true));
        if (samples.Count > 0) {
            actions.Children.Add(Button("重新开始", () => { readSessions.Remove(device.DevicePath); ShowPage(); Say("下一次读取将作为新的起点；已有记录仍保留。"); }));
            actions.Children.Add(Button("导出对比记录", () => {
                string? path = Save("读取记录 ZIP|*.zip", "display-readings.zip"); if (path == null) return;
                if (File.Exists(path)) File.Delete(path);
                DisplayWorkspace.ExportExperiment(session!, path, true); Say("已导出去标识记录，包含读数和功能定义。");
            }));
        }
        intro.Children.Add(actions);
        intro.Children.Add(Text("③ 看结果：变化只说明有关联；想进一步确认，可以把菜单改回原值，再读一次。", 13, "#64748C"));
        target.Children.Add(Card(intro, "#E9EDFF"));
        if (samples.Count > 0) {
            var latest = samples[^1]; int ok = latest.Readings.Count(r => r.Result.Status == "ok");
            var summary = Section(samples.Count == 1 ? "起点已记住，可以去改菜单了" : "本次读取结果", $"{latest.Time:HH:mm:ss} · 读到 {ok} 项 · {latest.Readings.Count - ok} 项未读到 · 已自动保存");
            if (samples.Count > 1) {
                var differences = Comparison.Between(samples[^2], latest);
                foreach (var difference in differences) {
                    var old = samples[^2].Readings.FirstOrDefault(r => r.Capability.Definition.Name == difference.Feature);
                    var current = latest.Readings.FirstOrDefault(r => r.Capability.Definition.Name == difference.Feature);
                    if (old != null) difference.Before = ReaderDisplay(old, samples[^2], displayReport);
                    if (current != null) difference.After = ReaderDisplay(current, latest, displayReport);
                }
                summary.Children.Add(Text(differences.Count == 0 ? "没有观察到变化" : $"发现 {differences.Count} 项变化", 20, "#3D5DD8"));
                if (differences.Count == 0) summary.Children.Add(Text("这项菜单可能没有提供可读取的状态，或需要专用协议。反复读取不一定能找到。", 13, "#64748C"));
                else {
                    var table = Table(); table.Columns.Add(Column("功能", "Feature", 1)); table.Columns.Add(Column("上一次", "Before", 1)); table.Columns.Add(Column("这一次", "After", 1)); table.ItemsSource = differences; summary.Children.Add(table);
                    var evidence = new StackPanel(); foreach (var difference in differences) evidence.Children.Add(Text(difference.Feature + "：" + difference.Evidence, 12, "#64748C"));
                    summary.Children.Add(new Expander { Header = "查看变化依据", Content = evidence, Margin = new Thickness(0, 12, 0, 0) });
                }
            }
            target.Children.Add(Card(summary));
            var details = new StackPanel();
            foreach (var reading in latest.Readings) details.Children.Add(Text(reading.Capability.Definition.Name + "：" + ReaderDisplay(reading, latest, displayReport), 13));
            target.Children.Add(new Expander { Header = "查看本次所有读数", Content = details, Margin = new Thickness(0, 0, 0, 18) });
        }
        var history = new StackPanel();
        foreach (var saved in Library.All<Experiment>("experiments").Where(e => HardwareReports.SameDevice(e.Device, device)).Take(10)) {
            var record = saved;
            history.Children.Add(Button($"{record.Question} · {record.Segments.SelectMany(s => s.Rounds).Count(r => r.Complete)} 次读取", () => { readSessions[device.DevicePath] = record; ShowPage(); Say("已打开记录；再次读取会与这份记录的最后一次比较。"); }));
        }
        if (history.Children.Count == 0) history.Children.Add(Text("还没有保存的记录。完成一次读取后会自动保存。", 13, "#64748C"));
        target.Children.Add(new Expander { Header = "以前的读取记录", Content = history });
    }

    string ReaderDisplay(Reading reading, Snapshot sample, ReaderSurvey? report)
    {
        var item = report?.Features.FirstOrDefault(f => f.Definition.Id == reading.Capability.Definition.Id);
        var origin = report?.Rounds.FirstOrDefault()?.Readings.FirstOrDefault(r => r.Capability.Definition.Id == reading.Capability.Definition.Id)?.Capability;
        var view = Library.Copy(reading);
        if (item != null && origin != null && DisplayWorkspace.SameCapability(origin, reading.Capability)) view.Capability.Definition = Library.Copy(item.Definition);
        string label = view.Display(sample.Device.WindowsHdrEnabled);
        return view.Capability.Definition.ValueType is "enum" or "boolean" && view.Result.Status == "ok" && view.Result.Current.HasValue
            ? label + $" · {view.Result.Current} / 0x{view.Result.Current:X2}" : label;
    }

    async Task ReadAndCompare(CancellationToken token)
    {
        if (device == null) throw new InvalidOperationException("请先选择显示器。");
        var target = Library.Copy(device);
        Say("正在读取，请保持显示器菜单和画面状态不变…");
        var sample = await workspace.Capture(target, token);
        token.ThrowIfCancellationRequested();
        if (!sample.Complete || !HardwareReports.SameDevice(target, sample.Device)) throw new IOException("读取未完成或设备连接发生变化，请重试。");
        var session = ReadSession ?? new Experiment { Question = $"读取记录 {DateTime.Now:MM-dd HH:mm:ss}", Device = Library.Copy(sample.Device) };
        sample.Name = $"第 {session.Segments.Count + 1} 次读取";
        session.Segments.Add(new ExperimentSegment { Name = sample.Name, Rounds = [sample] });
        Library.Save("experiments", session.Id, session);
        readSessions[target.DevicePath] = session;
        StoreSnapshot(sample, true);
        ShowPage(); Say("读取完成并已保存。可以修改一项菜单后再次读取。");
    }
}

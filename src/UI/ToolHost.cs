using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonitorInspector;
using MonitorInspector.Updates;

namespace DisplayControl.UI;
public static class ToolHost
{
    [STAThread]
    public static int Run(bool reader, string[] args)
    {
        try {
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
            using var commandLease = args.Length > 0 && args[0] is not ("--capture" or "--ui-test" or "--simulation" or "--context" or "--updated-from" or "--update-verify-startup") && File.Exists(Path.Combine(AppContext.BaseDirectory, "update-install.json")) ? UpdateIdentity.Lease(AppContext.BaseDirectory, false) : null;
            if (args.Contains("--batch-worker")) {
                string input = Console.In.ReadToEnd(); if (input.Length > 1024 * 1024) throw new InvalidDataException("请求过大");
                var request = JsonSerializer.Deserialize<BatchReadRequest>(input, Packages.Json) ?? throw new InvalidDataException("空请求"); Console.Write(JsonSerializer.Serialize(DeviceControl.ReadBatch(request), Packages.Json)); return 0;
            }
            if (args.Contains("--worker")) {
                string input = Console.In.ReadToEnd(); if (input.Length > 1024 * 1024) throw new InvalidDataException("请求过大");
                var request = JsonSerializer.Deserialize<ControlRequest>(input, Packages.Json) ?? throw new InvalidDataException("空请求"); Console.Write(JsonSerializer.Serialize(DeviceControl.Execute(request), Packages.Json)); return 0;
            }
            if (args.Length == 3 && args[0] is "--ddc" or "--ddc-expanded") { Console.Write(JsonSerializer.Serialize(HardwareReports.Scan(args[1], args[2], args[0] == "--ddc-expanded"))); return 0; }
            if (args.Length == 1 && args[0] == "--devices") { Console.Write(JsonSerializer.Serialize(HardwareReports.Devices(), Packages.Json)); return 0; }
            if (args.Length == 2 && args[0] == "--report") { File.WriteAllText(args[1], JsonSerializer.Serialize(HardwareReports.Devices(), Packages.Json)); return 0; }
            if (args.Length == 2 && args[0] == "--install") { Console.Write(JsonSerializer.Serialize(Packages.Install(args[1]), Packages.Json)); return 0; }
            if (args.Length == 3 && args[0] == "--bind") { Packages.Bind(HardwareReports.Devices().Single(d => d.Device == args[1]), args[2]); return 0; }
            if (args.Length == 3 && args[0] == "--read" || args.Length == 4 && args[0] == "--write") {
                var d = HardwareReports.Devices().Single(d => d.Device == args[1]); var c = CapabilityCatalog.Resolve(d).Single(c => c.Definition.Id == args[2]); if (c.Problem.Length > 0) throw new InvalidOperationException(c.Problem); var request = DisplayWorkspace.Request(d, c, args[0] == "--write" ? "write" : "read", args.Length == 4 ? args[3] : null); var result = ProcessClient.Run(Environment.ProcessPath!, request, worker: true).GetAwaiter().GetResult(); Console.Write(JsonSerializer.Serialize(result, Packages.Json)); return result.Status == "ok" ? 0 : 1;
            }
            if (args.Length == 3 && args[0] == "--update-inspect") { var channel = UpdateIdentity.Read(AppContext.BaseDirectory).Channel; var info = UpdateManifestCodec.ParseAndVerify(File.ReadAllText(args[2]), UpdateTrust.PublicKeyPem, channel); var validated = UpdatePackageValidator.ValidateAsync(args[1], info).GetAwaiter().GetResult(); Console.Write(JsonSerializer.Serialize(validated.Metadata)); return 0; }
            if (args.Length == 1 && args[0] == "--update-check") { var info = new UpdateClient().Check(UpdateSettings.Load(), CancellationToken.None).GetAwaiter().GetResult(); Console.Write(JsonSerializer.Serialize(new { currentVersion = UpdateIdentity.CurrentVersion.ToString(3), availableVersion = info?.Version.ToString(3) })); return 0; }
            if (args.Length == 2 && args[0] == "--update-prepare") { var client = new UpdateClient(); var info = UpdateManifestCodec.ParseAndVerify(File.ReadAllText(args[1]), UpdateTrust.PublicKeyPem, client.Channel); var prepared = client.Prepare(info, UpdateSettings.Load(), null, CancellationToken.None).GetAwaiter().GetResult(); Console.Write(JsonSerializer.Serialize(prepared)); return 0; }
            if (args.Length == 1 && args[0] == "--update-verify-startup") { Console.Write(JsonSerializer.Serialize(new { version = UpdateIdentity.CurrentVersion.ToString(3), result = UpdateIdentity.Startup(AppContext.BaseDirectory) })); return 0; }
            bool capture = args.Contains("--capture"), test = args.Contains("--ui-test");
            if (test && args.Length >= 2) Environment.SetEnvironmentVariable("DISPLAYCONTROL_DATA", Path.Combine(Path.GetFullPath(args[1]), "data-" + Guid.NewGuid().ToString("N")));
            if (capture && args.Contains("--simulation")) Environment.SetEnvironmentVariable("DISPLAYCONTROL_DATA", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "capture-data"));
            string? recovered = null;
            if (!capture && !test && !args.Contains("--simulation")) recovered = UpdateIdentity.Startup(AppContext.BaseDirectory);
            using var runLease = !capture && !test && File.Exists(Path.Combine(AppContext.BaseDirectory, "update-install.json")) ? UpdateIdentity.Lease(AppContext.BaseDirectory, false) : null;
            if (args.Any(a => a.StartsWith("--") && a is not ("--context" or "--simulation" or "--capture" or "--ui-test" or "--updated-from"))) throw new ArgumentException("未知参数。可用 --devices / --read / --write / --install / --bind / --report / --context / --simulation / --capture / --ui-test");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; var window = new WorkspaceWindow(reader, args);
            if (capture || test) {
                int exit = 0;
                app.Dispatcher.InvokeAsync(async () => {
                    try { await window.Initialize(); if (test) await window.RunUiTests(args[1]); else { window.Capture(args[1], args.Length > 2 && !args[2].StartsWith("--") ? args[2] : reader ? "check" : "control", 1480, 900, 1); } }
                    catch (Exception ex) { exit = 1; string dir = test ? args[1] : Path.GetDirectoryName(args[1])!; Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "ui-error.txt"), ex.ToString()); }
                    finally { app.Shutdown(exit); }
                });
                return app.Run();
            }
            string identity = (reader ? "reader" : "controller") + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Packages.Home + Environment.UserName)))[..20];
            using var navigation = new LocalNavigation(identity, id => app.Dispatcher.Invoke(() => { if (id != "show") _ = window.AcceptContext(id); if (!window.IsVisible) window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }));
            string? context = args.Length == 2 && args[0] == "--context" ? args[1] : null;
            if (!navigation.IsOwner) { navigation.Forward(context ?? "show"); return 0; }
            window.Closed += (_, _) => app.Shutdown();
            app.DispatcherUnhandledException += (_, e) => { File.AppendAllText(Path.Combine(Library.Folder("logs"), "ui-errors.txt"), e.Exception + Environment.NewLine); e.Handled = true; };
            if (recovered != null) window.SetStartupUpdateNotice(recovered); window.Show(); return app.Run();
        } catch (Exception ex) { Console.Error.WriteLine(ex); Directory.CreateDirectory(Packages.Home); File.WriteAllText(Path.Combine(Packages.Home, "startup-error.txt"), ex.ToString()); return 1; }
    }
}
public partial class WorkspaceWindow
{
    void SwitchTool(bool targetReader)
    {
        if (targetReader == reader) return; OpenContext(targetReader ? "check" : "control", focusedFeature ?? "", targetReader);
    }
    void OpenContext(string targetPage, string feature, bool? targetReader = null)
    {
        if (device == null) { Say("先选择设备"); return; }
        var c = snapshot?.Readings.FirstOrDefault(r => r.Capability.Definition.Id == feature)?.Capability;
        var context = new NavigationContext { Device = Library.Copy(device), FeatureId = feature, Provider = c?.Provider ?? "", Hash = c?.Hash ?? "", Page = targetPage, SessionId = experiment?.Id };
        Library.Save("contexts", context.Id, context);
        bool tool = targetReader ?? true;
        if (tool == reader) { _ = AcceptContext(context.Id); return; }
        string name = tool ? "DisplayControl.Reader.exe" : "DisplayControl.Controller.exe"; string path = Path.Combine(AppContext.BaseDirectory, name);
        if (!File.Exists(path)) path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../", tool ? "Reader" : "Controller", "bin/Release/net8.0-windows", name));
        if (!File.Exists(path)) { Say("未找到另一工具。请使用包含两个入口的预览包。"); return; }
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true }; start.ArgumentList.Add("--context"); start.ArgumentList.Add(context.Id); Process.Start(start); Say("已带入设备与功能上下文");
    }
    public async Task AcceptContext(string id)
    {
        if (active != null) { pendingContext = id; Say("当前任务结束后打开导航目标。"); return; }
        try {
            var context = Library.Get<NavigationContext>("contexts", id); focusedFeature = context.FeatureId;
            var list = devices.ItemsSource as List<MonitorData> ?? await workspace.Backend.Devices(CancellationToken.None);
            var match = list.SingleOrDefault(d => HardwareReports.SameDevice(d, context.Device));
            if (match == null) { devices.SelectedItem = null; snapshot = null; ShowPage(); banner.Text = "目标设备已离线或连接变化：" + context.Device.Name + "。不会改选第一台设备。"; return; }
            suppressDeviceChange = true; devices.SelectedItem = match; suppressDeviceChange = false; snapshot = deviceSnapshots.GetValueOrDefault(match.DevicePath); page = context.Page; if (context.SessionId != null && page == "experiments") experiment = Library.Get<Experiment>("experiments", context.SessionId); ShowPage();
            if (active == null && (snapshot == null || context.Page == "check")) await Run(t => RefreshValues(t, context.FeatureId.Length > 0 ? new[] { context.FeatureId } : null));
            if (context.Hash.Length > 0 && workspace.Backend.Capabilities(match).All(c => c.Definition.Id != context.FeatureId || c.Hash != context.Hash)) banner.Text = "上下文中的适配包已变化或缺失，请先核对来源。";
            if (page == "control" && focusedFeature.Length > 0) { group = workspace.Backend.Capabilities(match).FirstOrDefault(c => c.Definition.Id == focusedFeature)?.Definition.Group ?? "all"; ShowPage(); }
        } catch (Exception ex) { Say("无法打开上下文：" + ex.Message); }
    }
    public void Capture(string path, string targetPage, int width, int height, double scale)
    {
        Navigate(targetPage); root.Width = width; root.Height = height; root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var ctx = background.RenderOpen()) ctx.DrawRectangle(Background, null, new Rect(0, 0, width, height)); bitmap.Render(background); bitmap.Render(root); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var output = File.Create(path); png.Save(output);
        if (snapshot != null) File.WriteAllText(path + ".snapshot.json", JsonSerializer.Serialize(snapshot, Packages.Json));
    }
    public async Task RunUiTests(string directory)
    {
        Directory.CreateDirectory(directory); var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        Check(simulation && snapshot?.Readings.Count > 0, "真实页面加载模拟设备，未枚举硬件");
        Check(deviceSnapshots.Count == 2 && deviceSnapshots.Values.All(s => s.Readings.Count > 0), "启动预读两台设备，不等待切换才读取");
        var fixture = (SimulatedDisplayBackend)workspace.Backend;
        fixture.Requests.Clear(); devices.SelectedIndex = 1; devices.SelectedIndex = 0;
        Check(fixture.Requests.Count == 0 && snapshot?.Device.Device == "SIM-1", "设备切换使用已加载状态，无额外 DDC 请求");
        var original = snapshot!; var brightness = snapshot!.Readings.Single(r => r.Capability.Definition.Id == "brightness").Capability;
        var featureOrder = snapshot.Readings.Select(r => r.Capability.Definition.Id).ToArray();
        int beforeViewRequests = fixture.Requests.Count;
        SetControlView(true);
        Check(Preferences().CompactControls && compactControls, "紧凑视图选择持久化");
        Capture(Path.Combine(directory, "compact-common.png"), "control", 1480, 900, 1);
        Check(numericEditors.Count == 2 && numericEditors.All(e => e.Slider.IsEnabled), "紧凑常用视图复用亮度对比度编辑器");
        var compactPanel = Descendants(body).OfType<CompactFeaturePanel>().Single();
        var firstCompact = (System.Windows.FrameworkElement)compactPanel.Children[0]; var secondCompact = (System.Windows.FrameworkElement)compactPanel.Children[1];
        Check(firstCompact.ActualHeight <= 52 && Math.Abs(firstCompact.TranslatePoint(new Point(), compactPanel).Y - secondCompact.TranslatePoint(new Point(), compactPanel).Y) < 1 && secondCompact.TranslatePoint(new Point(), compactPanel).X > firstCompact.ActualWidth, "亮度对比度并排半行，健康状态不超过52像素高");
        Check(live["brightness"].Visibility == Visibility.Collapsed && numericEditors.All(e => e.View.ActualHeight <= 32), "紧凑数值项不重复占用状态行");
        var compactBlue = FeatureCard(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "low-blue-light").Capability, true, true);
        var blueSelect = Descendants(compactBlue).OfType<System.Windows.Controls.ComboBox>().Single();
        Check(fixture.Requests.Count == beforeViewRequests, "紧凑下拉框初始化不发送设置");
        blueSelect.SelectedItem = blueSelect.Items.Cast<System.Windows.Controls.ComboBoxItem>().Single(item => (string)item.Tag == "multimedia");
        for (int i = 0; i < 100 && writingDevices.Count > 0; i++) await Task.Delay(10);
        Check(fixture.Values["low-blue-light"] == 1 && fixture.Requests.Count(r => r.Action == "write" && r.ControlId == "low-blue-light") == 1, "紧凑下拉框只发送一次正确语义设置");
        blueSelect.SelectedItem = blueSelect.Items.Cast<System.Windows.Controls.ComboBoxItem>().Single(item => (string)item.Tag == "off");
        for (int i = 0; i < 100 && writingDevices.Count > 0; i++) await Task.Delay(10);
        Check(fixture.Values["low-blue-light"] == 0, "紧凑下拉框可恢复原模式");
        int compactManualSubmissions = 0;
        var compactManual = new NumericEditor(0,100,30,1,true,true,_ => compactManualSubmissions++,true);
        compactManual.Input.Text = "40";
        Check(compactManual.View.Children.Count == 1 && compactManualSubmissions == 0, "紧凑手动应用按钮与数值编辑器同一行且不自动发送");
        compactManual.Apply!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); compactManual.Dispose();
        Check(compactManualSubmissions == 1, "紧凑手动应用只发送一次");
        beforeViewRequests = fixture.Requests.Count;
        SetControlView(false);
        Check(fixture.Requests.Count == beforeViewRequests && !Preferences().CompactControls, "两种视图切换不读写显示器且可恢复详细视图");
        var helpFeature = new ControlDefinition { Id = "dynamic-contrast", Name = "DCR", Description = "当前仅可读取" };
        var helpSection = FeatureSection(helpFeature);
        var helpHeading = (System.Windows.Controls.Grid)helpSection.Children[0];
        var helpButton = (System.Windows.Controls.Button)helpHeading.Children[1];
        var helpDetails = (System.Windows.Controls.Border)helpSection.Children[1];
        Check(helpDetails.Visibility == Visibility.Collapsed && helpButton.IsEnabled, "只读功能的说明默认折叠且可展开");
        var helpRequestCount = fixture.Requests.Count;
        helpButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(helpDetails.Visibility == Visibility.Visible && ((System.Windows.Controls.TextBlock)helpDetails.Child).Text.Contains("动态对比度"), "说明按钮展开通俗功能解释");
        Check(((System.Windows.Controls.Border)FeatureSection(helpFeature).Children[1]).Visibility == Visibility.Visible, "页面重新渲染保留本次会话的说明展开状态");
        helpButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(helpDetails.Visibility == Visibility.Collapsed && fixture.Requests.Count == helpRequestCount, "折叠说明不发送读取或写入请求");
        helpFeature.HelpText = "此型号的专属帮助";
        Check(FeatureHelp.For(helpFeature) == "此型号的专属帮助" && helpFeature.Description == "当前仅可读取", "适配包帮助覆盖通用解释，不覆盖适配限制");
        Check(DeviceControl.Standard.All(f => !FeatureHelp.For(f).Contains("自定义功能")), "全部内置标准功能都有通俗说明");
        await Write(brightness, "80", CancellationToken.None); Check(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result.Current == 80, "控制命令→预检→写入→回读→页面状态");
        Check(snapshot.Readings.Select(r => r.Capability.Definition.Id).SequenceEqual(featureOrder), "真实页面写入前后保持功能顺序");
        var knownMismatch = Library.Copy(snapshot.Readings.Single(r => r.Capability.Definition.Id == "brightness"));
        knownMismatch.Result.Status = "mismatch";
        var normalReading = snapshot.Readings.Single(r => r.Capability.Definition.Id == "brightness");
        snapshot.Readings[snapshot.Readings.IndexOf(normalReading)] = knownMismatch;
        var mismatchCard = FeatureCard(brightness, true, true);
        Check(numericEditors.Last().Slider.IsEnabled, "写入未确认但当前值已核实的亮度仍可继续调节");
        snapshot.Readings[snapshot.Readings.IndexOf(knownMismatch)] = normalReading;
        var op = Library.All<Operation>("operations")[0]; var recovery = DisplayWorkspace.Recovery(op, snapshot); var restored = await workspace.Apply(recovery.Name, snapshot, DisplayWorkspace.Preflight(recovery, snapshot), CancellationToken.None); snapshot = restored.After; Check(snapshot.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result.Current == 85, "持久化操作核查与恢复");
        var save = Library.Copy(snapshot); save.Name = "UI 测试基线"; Library.Save("snapshots", save.Id, save);
        experiment = new() { Question = "HDR A→B→A 模拟实验", Device = device!, FeatureIds = ["brightness", "hdr-preset"] };
        await CaptureSegment("基线 A", CancellationToken.None); ((SimulatedDisplayBackend)workspace.Backend).Values["hdr-preset"] = 3; await CaptureSegment("游戏 B", CancellationToken.None); ((SimulatedDisplayBackend)workspace.Backend).Values["hdr-preset"] = 0; await CaptureSegment("恢复 A", CancellationToken.None);
        var reopened = Library.Get<Experiment>("experiments", experiment.Id); Check(reopened.Segments.Count == 3 && reopened.Compare(0, 1).Count == 1 && reopened.Compare(0, 2).Count == 0, "实验自动暂停、两轮采样、落盘重开与 A→B→A 对比");
        experiment.Segments[1].Name = "修订后的 B"; Library.Save("experiments", experiment.Id, experiment); Check(Library.Get<Experiment>("experiments", experiment.Id).Segments[1].Name == "修订后的 B", "标记修订不丢样本");
        using (var cancellation = new CancellationTokenSource(45)) {
            try { await CaptureSegment("连续段取消测试", cancellation.Token, true); } catch (OperationCanceledException) { }
            Check(Library.Get<Experiment>("experiments", experiment.Id).Segments.Last().Rounds.Any(r => !r.Complete), "连续采集取消保留未完成轮，不作为完整稳定数据");
        }
        selectedPreset = new() { Name = "办公", DevicePath = device!.DevicePath, Items = [new() { FeatureId = "brightness", Value = "80" }] }; Library.Save("presets", selectedPreset.Id, selectedPreset); presetPlan = DisplayWorkspace.Preflight(selectedPreset, snapshot); presetPlanId = selectedPreset.Id;
        NewDraft();
        var draftRoundtrip = Packages.Parse(JsonSerializer.Serialize(draft, Packages.Json));
        Check(draftRoundtrip.Controls[0].Id == "brightness", "可视化草稿与高级 JSON 共用同一模型");
        var mixedDraft = Library.Copy(draft!); mixedDraft.Controls.Add(new() { Id = "vendor.status", Name = "私有状态", ValueType = "status", Backend = "amd-ddc", Raw = new() { ReadPayload = "01 F8 E1 52", ReplyPrefix = "6E 88 02 00 F8 52", CurrentOffset = 8, MaximumOffset = 6 } });
        Packages.Validate(mixedDraft); draft = mixedDraft; draftFeature = "vendor.status";
        Capture(Path.Combine(directory, "mixed-workbench.png"), "workbench", 1480, 900, 1);
        Check(Packages.BackendFor(draft, draft.Controls[0]) == "standard-ddc" && Packages.BackendFor(draft, draft.Controls[1]) == "amd-ddc", "读取器工作台保留同包功能的不同通信通道");
        var context = new NavigationContext { Device = (await workspace.Backend.Devices(default))[1], FeatureId = "vendor.usb-status", Page = "check" }; Library.Save("contexts", context.Id, context); await AcceptContext(context.Id);
        Check(active == null && snapshot?.Device.Device == "SIM-2", "上下文直达设备与功能，等待读取完成，无选择事件竞争");
        context.Device = (await workspace.Backend.Devices(default))[0]; context.FeatureId = "hdr-preset"; context.Page = "control"; Library.Save("contexts", context.Id, context); await AcceptContext(context.Id); group = "common";
        Check(snapshot?.Device.Device == "SIM-1" && snapshot.Readings.Any(r => r.Capability.Definition.Id == "hdr-preset"), "返回控制器保留明确目标，不静默换台");
        var legacyCapability = Library.Copy(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Capability);
        legacyCapability.Definition.ValueType = "number"; // Explicit numeric metadata with named rules still uses mode options.
        var legacyCard = FeatureCard(legacyCapability);
        IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent) {
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>()) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        Check(!Descendants(legacyCard).OfType<System.Windows.Controls.Slider>().Any() && Descendants(legacyCard).OfType<System.Windows.Controls.Button>().Any(b => (string?)b.Content == "游戏"), "命名规则渲染模式按钮而非数字滑块");
        var priorOps = Library.All<Operation>("operations").Count;
        await Write(snapshot.Readings.Single(r => r.Capability.Definition.Id == "brightness").Capability, "101", default);
        Check(uiMessage.Contains("亮度") && uiMessage.Contains("数值超出") && Library.All<Operation>("operations").Count == priorOps, "单项控制预检错误在页面保留具体功能/原因，未发送设置");
        Capture(Path.Combine(directory, "preflight-failure.png"), "control", 1480, 900, 1);
        controlProblems.Clear();
        fixture.Values["brightness"] = 100; await RefreshValues(default, ["brightness"]); fixture.Requests.Clear();
        var level = snapshot!.Readings.Single(r => r.Capability.Definition.Id == "brightness").Capability;
        QueueControlWrite(level, "80"); QueueControlWrite(level, "60"); QueueControlWrite(level, "30");
        for (int i = 0; i < 200 && writingDevices.Count > 0; i++) await Task.Delay(5);
        Check(writingDevices.Count == 0 && fixture.Values["brightness"] == 30 && snapshot!.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result.Current == 30, "快速连续调节最终保持30，不被旧100或中间60覆盖");
        Check(fixture.Requests.Count(r => r.Action == "write") <= 2 && fixture.Requests.All(r => r.ControlId is "brightness" or "hdr-preset"), "连续调节合并为首个及最后目标，未触发全量读取");
        fixture.Requests.Clear(); fixture.FailNextWriteFeature = "brightness";
        await Write(level, "94", default);
        Check(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result.Status == "ok" && fixture.Values["brightness"] == 30 && fixture.Requests.Count(r => r.Action == "write") == 1 && fixture.Requests.All(r => r.ControlId is "brightness" or "hdr-preset"), "写前读取失败自动只核查亮度，不重发94或全量扫描（含HDR互锁）");
        fixture.FailFeature = "brightness"; await Write(level, "94", default); ShowPage();
        var failedCard = FeatureCard(level);
        Check(Descendants(failedCard).OfType<System.Windows.Controls.Slider>().Any(s => !s.IsEnabled) && Descendants(failedCard).OfType<System.Windows.Controls.Button>().Any(b => (string?)b.Content == "重试读取"), "持续失败保留禁用旧值滑块与单项重试，不伪报成功");
        Capture(Path.Combine(directory, "brightness-read-failure.png"), "control", 1480, 900, 1);
        fixture.FailFeature = null; fixture.Requests.Clear(); await RetryFeatureRead(snapshot!.Device, level, default); ShowPage();
        Check(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result.Status == "ok" && fixture.Requests.Count == 1 && fixture.Requests.Single().Action == "read", "单项重试恢复亮度控件，无需全局刷新且不写入");
        int submissions = 0; var oldEditor = new NumericEditor(0,100,100,1,true,false,_ => submissions++);
        oldEditor.Input.Text = "30"; oldEditor.Dispose();
        oldEditor.Input.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, oldEditor.Input, new System.Windows.Controls.TextBox()) { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
        Check(submissions == 0, "销毁控件后的失焦不提交旧草稿");
        var automatic = new NumericEditor(0,100,30,1,true,false,_ => { });
        var explicitApply = new NumericEditor(0,100,30,1,true,true,_ => submissions++);
        Check(automatic.Apply == null && explicitApply.Apply != null, "自动模式不显示应用按钮，手动模式才显示");
        explicitApply.Input.Text = "40"; Check(submissions == 0, "手动模式输入不发送"); explicitApply.Apply!.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Check(submissions == 1, "手动应用只发送一次"); automatic.Dispose(); explicitApply.Dispose();
        // Earlier history/restore tests created operations outside the UI writer.
        // Isolate mode reload request counts from that unrelated synchronization.
        lastOperationCheck = DateTime.UtcNow;
        fixture.Hdr = true; fixture.Requests.Clear(); fixture.FailNextReads["hdr-preset"] = 2; await PollState(true); ShowPage();
        File.WriteAllText(Path.Combine(directory, "hdr-recovery-requests.json"), JsonSerializer.Serialize(fixture.Requests.ToArray(), Packages.Json));
        Check(fixture.Requests.Count(r => r.ControlId == "hdr-preset" && r.Action == "read") == 4 && fixture.Requests.Count(r => r.ControlId == "brightness" && r.Action == "read") == 2 && fixture.Requests.All(r => r.Action == "read"), "HDR暂时失败逐项恢复后完整核查一次，更新早期读到的旧值，零SET");
        Check(snapshot!.Device.WindowsHdrEnabled == true && snapshot.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Result.Maximum == 4 && modeBadge.Text.Contains("HDR"), "外部系统HDR变化后重载最大值4和HDR徽标");
        fixture.Values["hdr-preset"] = 4; await RefreshValues(default, ["hdr-preset"]); ShowPage();
        foreach (string id in new[] { "brightness", "contrast", "color-preset" }) {
            var lockedCard = FeatureCard(snapshot!.Readings.Single(r => r.Capability.Definition.Id == id).Capability);
            Check(Descendants(lockedCard).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text.Contains("HDR 已开启")) && Descendants(lockedCard).OfType<System.Windows.Controls.Slider>().All(s => !s.IsEnabled), "HDR锁定界面" + id);
        }
        Capture(Path.Combine(directory, "display-hdr-locked.png"), "control", 1480, 900, 1);
        fixture.Values["hdr-preset"] = 0; await RefreshValues(default, ["hdr-preset"]);
        fixture.Values["hdr-preset"] = 4; fixture.Requests.Clear();
        await Write(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "contrast").Capability, "40", default);
        Check(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Result.Current == 4 && fixture.Requests.All(r => r.Action == "read"), "外部OSD开启HDR后拒绝旧SDR调节并同步界面互锁，零SET");
        fixture.Values["hdr-preset"] = 0; await RefreshValues(default, ["hdr-preset"]);
        fixture.Values["color-preset"] = 11; fixture.Values["red-gain"] = 42; fixture.Values["green-gain"] = 46; fixture.Values["blue-gain"] = 49;
        await RefreshValues(default, ["color-preset", "red-gain", "green-gain", "blue-gain"]);
        var hdrMode = snapshot!.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Capability;
        await Write(hdrMode, "game", default);
        fixture.Values["color-preset"] = 6; fixture.Values["red-gain"] = 50; fixture.Values["green-gain"] = 50; fixture.Values["blue-gain"] = 50;
        await Write(hdrMode, "off", default);
        Check(fixture.Values["color-preset"] == 11 && fixture.Values["red-gain"] == 42 && fixture.Values["green-gain"] == 46 && fixture.Values["blue-gain"] == 49 && uiMessage.Contains("已恢复"), "控制器HDR开启保存原色温，关闭确认后恢复用户RGB");
        Capture(Path.Combine(directory, "system-hdr-on.png"), "control",1480,900,1);
        await ChangeSystemHdr(default); Check(!fixture.Hdr && snapshot!.Device.WindowsHdrEnabled == false && snapshot.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Result.Maximum == 3, "软件系统HDR关闭后重载SDR范围3");
        fixture.Requests.Clear(); fixture.FailNextReads["hdr-preset"] = 3; fixture.FailNextReads["color-preset"] = 3;
        var changing = ChangeSystemHdr(default);
        for (int i = 0; i < 100 && modeReloading.Count == 0; i++) await Task.Delay(5);
        Check(modeReloading.Contains(device!.DevicePath), "软件切换系统HDR进入恢复阶段");
        var recoveringCard = FeatureCard(fixture.Capabilities(device!).Single(c => c.Definition.Id == "brightness"));
        Check(Descendants(recoveringCard).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == "正在恢复显示状态…") && !systemHdrButton.IsEnabled, "恢复期间显示等待状态并禁用重复HDR切换");
        Capture(Path.Combine(directory, "hdr-recovering.png"), "control", 1480, 900, 1);
        QueueControlWrite(level, "60"); Check(fixture.Requests.All(r => r.ControlId != "brightness" || r.Action != "write"), "恢复期间不接受亮度写入");
        await changing;
        Check(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "hdr-preset").Result.Status == "ok" && snapshot.Readings.Single(r => r.Capability.Definition.Id == "color-preset").Result.Status == "ok" && uiMessage.Contains("已重新读取"), "系统HDR后连续三轮失败仍能恢复且不被切换成功文案覆盖");
        Check(navigation.Children.OfType<System.Windows.Controls.Button>().All(b => (string)b.Tag != "workbench") || reader, "控制器导航不包含协议创作工作台");
        foreach (string p in new[] { "control", "check", "snapshots", "experiments", "presets", "history", "packages", "workbench", "settings" }) {
            Capture(Path.Combine(directory, p + ".png"), p, 1480, 900, 1); Check(File.Exists(Path.Combine(directory, p + ".png")), "真实 WPF 离屏渲染 " + p);
        }
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 }) Capture(Path.Combine(directory, $"control-min-{dpi}.png"), "control", 1040, 660, dpi);
        suppressDeviceChange = true; devices.SelectedIndex = 1; suppressDeviceChange = false; await RefreshValues(CancellationToken.None); group = "all"; Capture(Path.Combine(directory, "second-brand.png"), "control", 1200, 800, 1); Check(snapshot!.Readings.Any(r => r.Result.Text != null), "第二品牌文本状态不需要最大值");
        var action = snapshot.Readings.Single(r => r.Capability.Definition.ValueType == "action").Capability; await Write(action, "", CancellationToken.None); Check(Library.All<Operation>("operations")[0].Results[0].Sent, "动作无读数也可发送并持久化");
        ((SimulatedDisplayBackend)workspace.Backend).FailFeature = "brightness"; await RefreshValues(CancellationToken.None); Capture(Path.Combine(directory, "read-error.png"), "control", 1040, 660, 1);
        Check(snapshot!.Readings.Single(r => r.Capability.Definition.Id == "brightness").Result.Status == "error", "失败态不使用上次成功值"); ((SimulatedDisplayBackend)workspace.Backend).FailFeature = null;
        body.IsEnabled = false; Capture(Path.Combine(directory, "busy.png"), "control", 1040, 660, 1); body.IsEnabled = true;
        ((SimulatedDisplayBackend)workspace.Backend).Offline = true; await RefreshDevices(CancellationToken.None); Capture(Path.Combine(directory, "empty.png"), "control", 1040, 660, 1); Check(device == null, "离线空态不保留可写目标");
        RunUpdateUiTests(directory, Check);
        Check(!IsVisible, "验收窗口从未 Show，不创建前台窗口");
        File.WriteAllText(Path.Combine(directory, "ui-tests.json"), JsonSerializer.Serialize(new { passed, count = passed.Count, simulation = true, visibleWindow = IsVisible, foregroundRequested = false, dataDirectory = Packages.Home }, Packages.Json));
    }
}

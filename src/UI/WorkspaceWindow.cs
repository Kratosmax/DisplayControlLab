using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Text.Json;
using Microsoft.Win32;
using MonitorInspector;

namespace DisplayControl.UI;

public partial class WorkspaceWindow : Window
{
    readonly bool reader;
    readonly bool simulation;
    readonly DisplayWorkspace workspace;
    readonly Grid root = new();
    readonly StackPanel sidebar = new();
    readonly StackPanel navigation = new();
    readonly List<Button> navigationButtons = [];
    Expander? advancedNavigation;
    readonly ListBox devices = new();
    readonly StackPanel body = new();
    readonly ScrollViewer contentScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    string renderContext = "";
    readonly Dictionary<string, bool> expandedSections = [];
    IEnumerable<(Expander Section, string Key)> Expanders(DependencyObject node, string path = "")
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) {
            string key = child is Expander expander ? path + "/" + expander.Header : path;
            if (child is Expander section) yield return (section, key);
            foreach (var nested in Expanders(child, key)) yield return nested;
        }
    }
    readonly TextBlock heading = new();
    readonly TextBlock subtitle = new();
    readonly TextBlock status = new();
    readonly TextBlock banner = new();
    readonly TextBox search = new();
    readonly DispatcherTimer syncTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    string libraryStamp = "";
    bool suppressDeviceChange;
    readonly string? startupContext;
    string? pendingContext;
    readonly Dictionary<string, TextBlock> live = [];
    readonly Dictionary<string, Snapshot> deviceSnapshots = [];
    readonly Dictionary<string, long> deviceRevisions = [];
    readonly List<NumericEditor> numericEditors = [];
    readonly TextBlock modeBadge = new();
    Button systemHdrButton = null!;
    readonly Dictionary<string, string> controlProblems = [];
    CancellationTokenSource? active;
    Snapshot? snapshot;
    MonitorData? device => devices.SelectedItem as MonitorData;
    string page;
    string group = "common";
    string? focusedFeature;
    Preset? selectedPreset;
    List<PlanItem>? presetPlan;
    string? presetPlanId;
    AdapterPackage? draft;
    AdapterPackage? editingSource;
    bool initialized;
    bool manual;
    bool compactControls;
    HdrSafetyPolicy? currentSafety;
    string uiMessage = "";
    public WorkspaceWindow(bool reader, string[] args)
    {
        this.reader = reader; simulation = args.Contains("--simulation") || args.Contains("--ui-test");
        int contextIndex = Array.IndexOf(args, "--context"); startupContext = contextIndex >= 0 && contextIndex + 1 < args.Length ? args[contextIndex + 1] : null;
        workspace = new(simulation ? new SimulatedDisplayBackend() : new SystemDisplayBackend(Environment.ProcessPath!));
        page = reader ? "guided" : "control";
        Title = reader ? "DisplayControl · 读取器" : "DisplayControl · 控制器";
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/DisplayControl.UI;component/Assets/displaycontrol.ico"));
        Width = 1480; Height = 960; MinWidth = 1040; MinHeight = 700;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14; Foreground = Brush("#202A3D"); Background = Brush("#F4F6FB");
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DisplayControl.UI;component/Theme.xaml", UriKind.Relative) });
        BuildShell(); InitializeLiveLifecycle();
        Loaded += async (_, _) => { if (!initialized) await Initialize(); };
        Closing += (_, e) => { if (active != null) { e.Cancel = true; active.Cancel(); Say("正在取消工作；完成后可关闭。已发送设置请先核查。"); } };
        Closed += (_, _) => { syncTimer.Stop(); updateTask?.Cancel(); };
        syncTimer.Tick += async (_, _) => await PollState();

    }
    public static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    static TextBlock Text(string value, double size = 14, string color = "#202A3D") => new() { Text = value, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
    Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), MinHeight = 36 };
        if (primary) { button.Background = Brush("#3D5DD8"); button.Foreground = Brushes.White; }
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { Say(ex.Message); } }; return button;
    }
    Button AsyncButton(string text, Func<CancellationToken, Task> action, bool primary = false) => Button(text, () => _ = Run(action), primary);
    static StackPanel Row() => new() { Orientation = Orientation.Horizontal };
    static WrapPanel Actions() => new() { Margin = new Thickness(0, 4, 0, 8) };
    static Border Card(UIElement content, string color = "#FFFFFF") => new() { Background = Brush(color), BorderBrush = Brush("#DFE5F0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 18), Child = content };
    static StackPanel Section(string title, string? description = null)
    {
        var p = new StackPanel(); p.Children.Add(Text(title, 19)); if (description != null) p.Children.Add(Text(description, 13, "#64748C")); return p;
    }
    void BuildShell()
    {
        root.RowDefinitions.Add(new() { Height = new GridLength(62) }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = new GridLength(38) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(228) }); root.ColumnDefinitions.Add(new());
        var top = new Grid { Background = Brushes.White }; top.ColumnDefinitions.Add(new() { Width = new GridLength(228) }); top.ColumnDefinitions.Add(new());
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/DisplayControl.UI;component/Assets/displaycontrol.png")), Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(new TextBlock { Text = "DisplayControl", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(brand);
        var switches = Row(); switches.Margin = new Thickness(24, 10, 0, 0);
        switches.Children.Add(Button("控制器", () => SwitchTool(false), !reader)); switches.Children.Add(Button("读取器", () => SwitchTool(true), reader));
        switches.Children.Add(Text(simulation ? "模拟设备 · 不连接硬件" : "设备控制与适配工作区", 12, "#64748C")); Grid.SetColumn(switches, 1); top.Children.Add(switches);
        Grid.SetColumnSpan(top, 2); root.Children.Add(top);
        sidebar.Background = Brush("#EDF1F7"); sidebar.Margin = new Thickness(0); var sideContent = new StackPanel { Margin = new Thickness(14, 24, 14, 12) };
        sideContent.Children.Add(Text("显示器", 12, "#64748C"));
        devices.BorderThickness = new Thickness(0); devices.Background = Brushes.Transparent; devices.MinHeight = 90; devices.MaxHeight = 230; devices.FontSize = 13;
        ScrollViewer.SetHorizontalScrollBarVisibility(devices, ScrollBarVisibility.Disabled);
        var template = new DataTemplate(typeof(MonitorData)); var devicePanel = new FrameworkElementFactory(typeof(StackPanel)); devicePanel.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 10, 10, 10));
        var model = new FrameworkElementFactory(typeof(TextBlock)); model.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayName")); model.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); model.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); model.SetBinding(TextBlock.ToolTipProperty, new System.Windows.Data.Binding("Name")); devicePanel.AppendChild(model);
        var connection = new FrameworkElementFactory(typeof(TextBlock)); connection.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Connection")); connection.SetValue(TextBlock.FontSizeProperty, 12.0); connection.SetValue(TextBlock.ForegroundProperty, Brush("#64748C")); connection.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0)); devicePanel.AppendChild(connection); template.VisualTree = devicePanel; devices.ItemTemplate = template;
        devices.SelectionChanged += async (_, _) => { if (!suppressDeviceChange && device != null && initialized && active == null) { snapshot = deviceSnapshots.GetValueOrDefault(device.DevicePath); presetPlan = null; ShowPage(); if (snapshot == null && !reader) await Run(t => RefreshValues(t)); } };
        sideContent.Children.Add(devices); sideContent.Children.Add(AsyncButton("刷新设备", RefreshDevices)); sideContent.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 20) }); sideContent.Children.Add(Text("工作区", 12, "#64748C")); sideContent.Children.Add(navigation); sidebar.Children.Add(sideContent);
        var sidebarScroll = new ScrollViewer { Content = sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brush("#EDF1F7") }; Grid.SetRow(sidebarScroll, 1); root.Children.Add(sidebarScroll);
        var main = new Grid { Margin = new Thickness(30, 28, 30, 12) }; main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new());
        var header = new StackPanel(); heading.FontSize = 28; heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 7); subtitle.FontSize = 13; subtitle.Foreground = Brush("#64748C"); subtitle.TextWrapping = TextWrapping.Wrap; header.Children.Add(heading); header.Children.Add(subtitle);
        var modeBar = Actions(); modeBadge.FontWeight = FontWeights.SemiBold; modeBadge.Padding = new Thickness(10, 6, 10, 6); modeBadge.Margin = new Thickness(0, 0, 12, 8); modeBar.Children.Add(modeBadge);
        systemHdrButton = Button("开启系统 HDR", () => _ = Run(ChangeSystemHdr)); modeBar.Children.Add(systemHdrButton); header.Children.Add(modeBar); main.Children.Add(header);
        banner.TextWrapping = TextWrapping.Wrap; banner.Foreground = Brush("#425A88"); banner.Margin = new Thickness(0, 14, 0, 12); Grid.SetRow(banner, 1); main.Children.Add(banner);
        contentScroll.Content = body; Grid.SetRow(contentScroll, 2); main.Children.Add(contentScroll); Grid.SetColumn(main, 1); Grid.SetRow(main, 1); root.Children.Add(main);
        var footer = new Grid { Background = Brushes.White }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); status.Margin = new Thickness(18, 0, 12, 0); status.VerticalAlignment = VerticalAlignment.Center; status.TextTrimming = TextTrimming.CharacterEllipsis; footer.Children.Add(status);
        var cancel = Button("取消当前任务", () => active?.Cancel()); cancel.Margin = new Thickness(0); cancel.Padding = new Thickness(12, 2, 12, 2); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel); Grid.SetRow(footer, 2); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer); Content = root;
        var advanced = new StackPanel();
        foreach (var item in reader ? new[] { ("guided", "自动检测"), ("settings", "设置与帮助"), ("check", "设备与功能详情"), ("workbench", "协议工作台"), ("packages", "适配包") } : new[] { ("control", "显示器控制"), ("presets", "我的方案"), ("history", "操作历史"), ("packages", "已安装适配包"), ("settings", "设置与帮助") })
        {
            var id = item.Item1; var b = Button(item.Item2, () => Navigate(id)); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 7); b.BorderThickness = new Thickness(0); b.Background = Brushes.Transparent; b.Tag = id; navigationButtons.Add(b);
            (reader && id is not ("guided" or "settings") ? advanced : navigation).Children.Add(b);
        }
        if (reader) { advancedNavigation = new Expander { Header = "高级工具", Content = advanced, Margin = new Thickness(0, 12, 0, 0) }; navigation.Children.Add(advancedNavigation); }
    }
    public async Task Initialize()
    {
        initialized = true;
        manual = Library.All<UserPreferences>("preferences").FirstOrDefault()?.ManualApply ?? false;
        compactControls = Preferences().CompactControls;
        await Run(async t => { await RefreshDevices(t); if (!reader) await WarmDevices(t); });
        if (startupContext != null) await AcceptContext(startupContext);
        if (startupUpdateNotice != null) { updateMessage = startupUpdateNotice; Say(startupUpdateNotice); }
        if (!simulation) { if (!reader) syncTimer.Start(); _ = StartUpdateChecks(); }
    }
    async Task Run(Func<CancellationToken, Task> action)
    {
        if (updateTask != null || installPrepared) { Say("正在更新，请稍候再操作显示器。"); return; }
        if (active != null) { Say("已有任务进行中，可取消后再操作。"); return; }
        active = new CancellationTokenSource(); devices.IsEnabled = false; body.IsEnabled = false;
        try { await action(active.Token); }
        catch (OperationCanceledException) { Say("任务已取消。已发送的指令无法撤回，请重新核查当前值。"); }
        catch (Exception ex) { Say(ex.Message); }
        finally { active.Dispose(); active = null; devices.IsEnabled = true; body.IsEnabled = true; if (pendingContext is { } context) { pendingContext = null; _ = AcceptContext(context); } }
    }
    void Say(string message) { uiMessage = message; status.Text = message; status.ToolTip = message; }
    async Task RefreshDevices(CancellationToken t)
    {
        var previous = device?.DevicePath; var list = await workspace.Backend.Devices(t); var nicknames = Preferences().Nicknames; foreach (var d in list) d.Nickname = nicknames.GetValueOrDefault(d.DevicePath, ""); suppressDeviceChange = true; devices.ItemsSource = list; devices.SelectedItem = list.FirstOrDefault(d => d.DevicePath == previous) ?? list.FirstOrDefault(); suppressDeviceChange = false; snapshot = device == null ? null : deviceSnapshots.GetValueOrDefault(device.DevicePath); ShowPage(); Say($"识别到 {list.Count} 台显示器");
    }
    async Task RefreshValues(CancellationToken t, IEnumerable<string>? scope = null)
    {
        if (device == null) return;
        var target = device; long revision = Revision(target.DevicePath); Say("正在刷新当前设备状态…");
        var update = await workspace.Capture(target, t, scope: scope);
        if (Revision(target.DevicePath) != revision) return;
        StoreSnapshot(update, scope == null);
        presetPlan = null; ShowPage(); Say($"刷新完成 · {update.Readings.Count(r => r.Result.Status == "ok")} 项成功 · {DateTime.Now:HH:mm:ss}");
    }
    void Navigate(string next)
    {
        if (next is "snapshots" or "experiments") next = "guided";
        if (!reader && IsVisible && next is "guided" or "check" or "workbench") { OpenContext(next, focusedFeature ?? "", true); return; }
        page = next; ShowPage();
    }
    void ShowPage()
    {
        foreach (var (section, key) in Expanders(body)) expandedSections[renderContext + "|" + key] = section.IsExpanded;
        string context = $"{device?.DevicePath}|{page}|{group}|{compactControls}";
        double offset = context == renderContext ? contentScroll.VerticalOffset : 0;
        renderContext = context;
        currentSafety = device == null ? null : HdrSafety.Policy(snapshot?.Device ?? device);
        foreach (var editor in numericEditors) editor.Dispose(); numericEditors.Clear();
        body.Children.Clear(); live.Clear();
        UpdateModeBadge();
        foreach (Button b in navigationButtons) { bool selected = (string)b.Tag == page; b.Background = selected ? Brush("#E6EBFF") : Brushes.Transparent; b.Foreground = selected ? Brush("#3D5DD8") : Brush("#202A3D"); }
        if (advancedNavigation != null && page is "check" or "workbench" or "packages") advancedNavigation.IsExpanded = true;
        heading.Text = page switch { "guided" => "自动检测显示器功能", "control" => device?.DisplayName ?? "显示器控制", "check" => "设备与功能详情", "presets" => "我的方案", "history" => "操作历史", "packages" => "适配包", "workbench" => "协议工作台", _ => "设置与帮助" };
        subtitle.Text = device == null ? "连接显示器后点击刷新设备；远程会话可能无法访问 DDC。" : $"{device.Connection} · {device.Resolution} · {device.RefreshRate} · Windows HDR：{snapshot?.Device.Hdr ?? device.Hdr}";
        banner.Text = simulation ? "模拟工作区：这里的值和操作不代表真实显示器支持。" : "标准功能内置；私有功能按需安装。读取失败不等于设备不支持。";
        if (reader && page == "guided" && !simulation) banner.Text = "发现 → 标准 / 厂商识别 → 对比验证 → 生成适配包。结果自动保存；启用试调并确认后才会修改允许的图像项。";
        switch (page) { case "guided": GuidedPage(); break; case "control": ControlsPage(); break; case "check": CheckPage(); break; case "presets": PresetsPage(); break; case "history": HistoryPage(); break; case "packages": PackagesPage(); break; case "workbench": WorkbenchPage(); break; default: SettingsPage(); break; }
        foreach (var (section, key) in Expanders(body)) {
            if (expandedSections.TryGetValue(context + "|" + key, out bool expanded)) section.IsExpanded = expanded;
        }
        contentScroll.UpdateLayout(); contentScroll.ScrollToVerticalOffset(offset);
    }
    void ControlsPage()
    {
        if (device == null) { body.Children.Add(Card(Text("尚未找到显示器。检查连接后刷新；不会自动选择历史离线设备执行设置。"))); return; }
        ControlPackagePicker();
        var tabs = Actions(); foreach (var (id, label) in new[] { ("common", "常用"), ("image", "图像"), ("game", "游戏"), ("audio", "声音"), ("connection", "连接与设备"), ("all", "全部功能") }) { string selected = id; tabs.Children.Add(Button(label, () => { group = selected; ShowPage(); }, id == group)); }
        tabs.Children.Add(AsyncButton("刷新状态", t => RefreshValues(t)));
        tabs.Children.Add(Button("详细视图", () => SetControlView(false), !compactControls));
        tabs.Children.Add(Button("紧凑视图", () => SetControlView(true), compactControls)); body.Children.Add(tabs);
        search.Width = 240; search.Margin = new Thickness(0, 0, 0, 14); search.ToolTip = "搜索名称、功能 id 和来源";
        // A fresh search field avoids reparenting and retains the query across navigation.
        var filter = new TextBox { Text = search.Text, Width = 320, HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 34, Margin = new Thickness(0, 0, 0, 14), ToolTip = "搜索功能名称、id、提供者" };
        var features = new StackPanel(); filter.TextChanged += (_, _) => { search.Text = filter.Text; RenderFeatures(features); }; var searchRow = Row(); var searchLabel = Text("搜索功能", 13, "#64748C"); searchLabel.Margin = new Thickness(0, 0, 14, 14); searchRow.Children.Add(searchLabel); searchRow.Children.Add(filter); body.Children.Add(searchRow);
        if (compactControls) { body.Children.Add(features); RenderFeatures(features); return; }
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 265 });
        features.Margin = new Thickness(0, 0, 20, 0); columns.Children.Add(features); var aside = new StackPanel(); Grid.SetColumn(aside, 1); columns.Children.Add(aside); body.Children.Add(columns); RenderFeatures(features);
        var presets = Section("我的方案", "保存语义选项，下次按当前系统条件解析。"); foreach (var p in Library.All<Preset>("presets").Where(p => p.DevicePath == device.DevicePath).Take(4)) presets.Children.Add(Button(p.Name, () => { selectedPreset = p; Navigate("presets"); }));
        presets.Children.Add(Button("＋ 保存当前状态", () => Navigate("presets"))); aside.Children.Add(Card(presets));
        var adaptation = Section("设备与适配", "Windows HDR 与显示器 HDR 预设是两个独立状态。"); adaptation.Children.Add(Text("Windows HDR：" + (snapshot?.Device.Hdr ?? device.Hdr))); adaptation.Children.Add(Text("私有来源：" + string.Join("、", (snapshot?.Readings ?? []).Where(r => r.Capability.Provider != "standard").Select(r => r.Capability.Provider).Distinct()), 13, "#64748C")); adaptation.Children.Add(Button("管理适配包", () => Navigate("packages"))); adaptation.Children.Add(Button("查看完整功能目录", () => { group = "all"; ShowPage(); })); aside.Children.Add(Card(adaptation));
        var research = Section("想知道设置改变了什么？", "在读取器先读一次，修改菜单后再读，自动对比并保存。"); research.Children.Add(Button("记录一次变化 ↗", () => OpenContext("guided", focusedFeature ?? ""), true)); aside.Children.Add(Card(research, "#E9EDFF"));
        var recent = Library.All<Operation>("operations").FirstOrDefault(o => o.Before.Device.DevicePath == device.DevicePath);
        if (recent != null) { var p = Section("最近操作", recent.ToString()); p.Children.Add(AsyncButton("核查并恢复本次已修改项", t => Restore(recent, t))); p.Children.Add(Button("查看详情", () => Navigate("history"))); aside.Children.Add(Card(p, "#EAF5F0")); }
    }
    void RenderFeatures(StackPanel target)
    {
        target.Children.Clear(); if (device == null) return;
        var capabilities = snapshot?.Device.DevicePath == device.DevicePath && snapshot.Readings.Count > 0 ? snapshot.Readings.Select(r => r.Capability).ToList() : workspace.Backend.Capabilities(device);
        if (!reader && Packages.ControlPackageFeatures(device) is { } verified) capabilities = capabilities.Where(c => verified.Contains(c.Definition.Id)).ToList();
        var visible = capabilities.Where(c => (group == "all" || group == "common" && (c.Definition.Id is "brightness" or "contrast" or "hdr-preset" or "color-preset" || Favorites().Contains(c.Definition.Id)) || group == c.Definition.Group || group == "connection" && c.Definition.Group is "device" or "information" or "osd") && (search.Text.Length == 0 || (c.Definition.Name + c.Definition.Id + c.Provider).Contains(search.Text, StringComparison.OrdinalIgnoreCase)));
        var items = visible.OrderBy(c => c.Definition.Id == "hdr-preset" ? 0 : c.Definition.Id == "color-preset" ? 2 : 1).ToList();
        if (compactControls && group == "common") items = items.OrderBy(c => c.Definition.Id switch { "brightness" => 0, "contrast" => 1, "color-preset" => 2, "hdr-preset" => 3, _ => 4 }).ToList();
        if (group != "all") items = items.Where(c => { string reason = DisplayWorkspace.Conditions(c.Definition.VisibleWhen, snapshot); return reason.Length == 0 || reason.StartsWith("条件未知"); }).ToList();
        if (compactControls) {
            var panel = new CompactFeaturePanel();
            foreach (var c in items) panel.Children.Add(FeatureCard(c, true, true));
            if (items.Count > 0) target.Children.Add(panel);
        }
        else if (group == "common") {
            var common = Section("常用控制", "数字调节结束后应用；预设点击即应用，回读后确认。");
            var pair = new Grid(); pair.ColumnDefinitions.Add(new()); pair.ColumnDefinitions.Add(new()); int column = 0;
            foreach (var c in items.Where(c => c.Definition.Id is "brightness" or "contrast").OrderBy(c => c.Definition.Id == "brightness" ? 0 : 1)) { var widget = FeatureCard(c, true); widget.Margin = new Thickness(column == 0 ? 0 : 16, 0, 0, 8); Grid.SetColumn(widget, column++); pair.Children.Add(widget); }
            common.Children.Add(pair);
            foreach (var c in items.Where(c => c.Definition.Id is not ("brightness" or "contrast"))) { common.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 18), Background = Brush("#E4E9F3") }); common.Children.Add(FeatureCard(c, true)); }
            target.Children.Add(Card(common));
        } else foreach (var c in items) target.Children.Add(FeatureCard(c));
        if (target.Children.Count == 0) target.Children.Add(Card(Text("此组暂无可用适配。可在全部功能中查看菜单线索，或导入匹配的适配包。")));
    }
    Border FeatureCard(Capability c, bool compact = false, bool dense = false)
    {
        var f = c.Definition; var r = snapshot?.Readings.SingleOrDefault(r => r.Capability.Definition.Id == f.Id);
        if (r != null && !DisplayWorkspace.SameCapability(r.Capability, c)) r = null;
        bool recovering = device != null && modeReloading.Contains(device.DevicePath);
        var p = FeatureSection(f, dense); var current = Text(recovering ? "正在恢复显示状态…" : r?.Display(snapshot!.Device.WindowsHdrEnabled) ?? "等待检查…", 17, "#3D5DD8"); live[f.Id] = current; p.Children.Add(current);
        if (!compact) p.Children.Add(Text($"{(c.Provider == "standard" ? "标准 DDC" : c.Provider)} · {c.Version} · {(f.Writable ? "声明可写" : "只读")} · {(r == null ? "未检查" : r.Time.ToString("HH:mm:ss"))}", 12, "#64748C"));
        string problem = c.Problem;
        if (problem.Length == 0) problem = HdrSafety.Problem(currentSafety, HdrSafety.ProtectedId(currentSafety, f), snapshot);
        if (problem.Length == 0) problem = DisplayWorkspace.Conditions(f.WritableWhen, snapshot);
        if (f.WriteWhenWindowsHdr.HasValue && f.WriteWhenWindowsHdr != snapshot?.Device.WindowsHdrEnabled) problem = "当前系统 HDR 条件下未开放写入";
        bool readableValue = r?.Result.Status == "ok" || r?.Result.Status == "mismatch" && r.Result.Current.HasValue && r.Result.Maximum.HasValue;
        bool writable = !recovering && f.Writable && problem.Length == 0 && (readableValue || !f.Readable || f.ValueType == "action");
        if (problem.Length > 0) p.Children.Add(Text(problem, 13, "#9A6612"));
        if (!dense && f.MenuPath.Length > 0) p.Children.Add(Text("显示器菜单：" + f.MenuPath, 12, "#64748C"));
        if (!dense && !string.IsNullOrWhiteSpace(f.Description)) p.Children.Add(Text(f.Description, 13, "#64748C"));
        if (controlProblems.TryGetValue(device!.DevicePath + "|" + f.Id, out var failure)) p.Children.Add(Text(failure, 13, "#9A6612"));
        var editorReading = r?.Result.Status == "ok" ? r : lastKnownReadings.GetValueOrDefault(device.DevicePath + "|" + f.Id);
        if (editorReading != null && !DisplayWorkspace.SameCapability(editorReading.Capability, c)) editorReading = null;
        if (f.Readable && r?.Result.Status is not (null or "ok" or "mismatch")) {
            if (editorReading?.Result.Current is uint previous) p.Children.Add(Text($"上次确认值：{previous}；当前值尚未确认", 12, "#64748C"));
            var retryTarget = Library.Copy(snapshot!.Device);
            p.Children.Add(AsyncButton("重试读取", t => RetryFeatureRead(retryTarget, c, t)));
        }
        var editorPanel = new StackPanel(); bool editorHasCurrent = false; p.Children.Add(editorPanel);
        if (f.ValueType == "number" && f.Rules.Count == 0 && (r?.Result.Maximum ?? editorReading?.Result.Maximum) is uint max) {
            var editor = new NumericEditor(f.Minimum, max, r?.Result.Status is "ok" or "mismatch" ? r.Result.Current ?? f.Minimum : editorReading?.Result.Current ?? f.Minimum, f.Step, writable, manual, value => QueueControlWrite(c, value), dense);
            numericEditors.Add(editor); editorPanel.Children.Add(editor.View);
            editorHasCurrent = true;
            if (f.AllowedWriteValues != null) p.Children.Add(Text("当前仅开放已验证值：" + string.Join("、", f.AllowedWriteValues), 12, "#9A6612"));
        }
        else if (f.ValueType is "enum" or "boolean" || f.Rules.Count > 0) {
            ValueRule? rule = null; if (r?.Result.Maximum is uint m) try { rule = ProtocolCodec.Rule(f, m, snapshot!.Device.WindowsHdrEnabled); } catch (Exception ex) { p.Children.Add(Text(ex.Message)); }
            if (dense && rule != null) {
                var choices = rule.Values.Select(kv => new ComboBoxItem { Content = rule.Labels.GetValueOrDefault(kv.Key, kv.Key), Tag = kv.Key, IsEnabled = writable && (f.AllowedWriteValues == null || f.AllowedWriteValues.Contains(kv.Value)) }).ToList();
                var select = new ComboBox { Style = (Style)FindResource("CompactModeSelect"), ItemsSource = choices, SelectedItem = choices.FirstOrDefault(item => rule.Values[(string)item.Tag] == r?.Result.Current), IsEnabled = writable, MinHeight = 28, Padding = new Thickness(6, 2, 6, 2), MaxDropDownHeight = 320 };
                editorHasCurrent = select.SelectedItem != null;
                select.ToolTip = (select.SelectedItem as ComboBoxItem)?.Content?.ToString();
                System.Windows.Automation.AutomationProperties.SetName(select, f.Name);
                select.SelectionChanged += (_, _) => { select.ToolTip = (select.SelectedItem as ComboBoxItem)?.Content?.ToString(); if (select.SelectedItem is ComboBoxItem { IsEnabled: true, Tag: string id }) QueueControlWrite(c, id); };
                editorPanel.Children.Add(select);
            }
            else {
            var options = Actions(); if (rule != null) foreach (var kv in rule.Values) {
                string id = kv.Key; var b = Button(rule.Labels.GetValueOrDefault(id, id), () => QueueControlWrite(c, id), r?.Result.Current == kv.Value); b.IsEnabled = writable && (f.AllowedWriteValues == null || f.AllowedWriteValues.Contains(kv.Value)); if (!b.IsEnabled) b.ToolTip = "只读、条件不满足或该值未开放写入"; options.Children.Add(b);
            } else options.Children.Add(Text("当前原始值尚无已确认的选项映射。", 13, "#64748C")); editorPanel.Children.Add(options);
            }
        }
        else if (f.Writable && f.ValueType is "action" or "text") {
            var input = new TextBox { MinHeight = 34, Visibility = f.ValueType == "action" ? Visibility.Collapsed : Visibility.Visible }; editorPanel.Children.Add(input); var b = AsyncButton(f.ValueType == "action" ? "执行" : "发送", async t => { if (Confirm("该功能没有回读，不能确认效果或自动恢复。是否发送？")) await Write(c, input.Text, t); }); b.IsEnabled = writable; editorPanel.Children.Add(b);
        }
        TextBlock? linkage = DisplayWorkspace.AffectedScope(f).Count > 0 ? Text("联动核查：" + string.Join("、", DisplayWorkspace.AffectedScope(f)) + "；应用后重新读取，不自动写回。", 12, "#64748C") : null;
        if (!dense && linkage != null) p.Children.Add(linkage);
        var tools = Actions();
        if (reader) { tools.Children.Add(AsyncButton("检查此功能", t => RefreshValues(t, [f.Id]))); tools.Children.Add(Button("记录变化", () => OpenContext("guided", f.Id))); }
        else { tools.Children.Add(Button("在读取器研究 ↗", () => OpenContext("guided", f.Id, true))); tools.Children.Add(Button(Favorites().Contains(f.Id) ? "取消常用" : "加入常用", () => ToggleFavorite(f.Id))); }
        if (dense) {
            var more = new StackPanel();
            more.Children.Add(Text($"{c.Provider} · {c.Version} · {(f.Writable ? "声明可写" : "只读")}", 12, "#64748C"));
            if (f.MenuPath.Length > 0) more.Children.Add(Text("显示器菜单：" + f.MenuPath, 12, "#64748C"));
            if (!string.IsNullOrWhiteSpace(f.Description)) more.Children.Add(Text(f.Description, 13, "#64748C"));
            if (linkage != null) more.Children.Add(linkage);
            more.Children.Add(tools);
            return DenseFeatureLayout(f, p, editorPanel, current, more, r?.Result.Status == "ok" && !recovering, editorHasCurrent);
        }
        if (compact) p.Children.Add(new Expander { Header = reader ? "检查与研究" : "更多", Content = tools, Margin = new Thickness(0, 10, 0, 0) }); else p.Children.Add(tools);
        return compact ? new Border { Child = p } : Card(p);
    }
    async Task Write(Capability capability, string value, CancellationToken t)
    {
        if (device == null) return;
        await WriteFor(snapshot?.Device.DevicePath == device.DevicePath ? snapshot.Device : device, capability, value, t);
    }
    async Task Restore(Operation op, CancellationToken t)
    {
        if (device == null) return; var scope = op.Requests.Select(r => r.ControlId).Concat(op.Before.Readings.Where(r => op.Requests.Any(q => q.ControlId == r.Capability.Definition.Id)).SelectMany(r => r.Capability.Definition.WritableWhen.Select(c => c.FeatureId))).Distinct().ToList(); var now = await workspace.Capture(device, t, scope: scope); var recovery = DisplayWorkspace.Recovery(op, now);
        if (!Confirm("只恢复本次已确认修改且已有原值的 " + recovery.Items.Count + " 项。未采集的联动项不会恢复。继续？")) return;
        var result = await workspace.Apply(recovery.Name, now, DisplayWorkspace.Preflight(recovery, now), t); StoreSnapshot(result.After); ShowPage(); Say("恢复结束；请查看各项回读结果。");
    }
    void CheckPage()
    {
        var p = Section("检查当前设备", "查看已知标准功能和已安装适配包的读数。要比较菜单变化，请回到开始读取。"); var actions = Actions(); actions.Children.Add(AsyncButton("刷新功能详情", t => RefreshValues(t), true)); actions.Children.Add(Button("返回读取与对比", () => Navigate("guided"))); p.Children.Add(actions); body.Children.Add(Card(p));
        if (snapshot != null) {
            foreach (var reading in snapshot.Readings) reading.DisplayHdrContext = snapshot.Device.WindowsHdrEnabled;
            var grid = Table(); grid.Columns.Add(Column("功能", "Capability.Definition.Name", 1.2)); grid.Columns.Add(Column("当前状态", "DisplayText", 1.2)); grid.Columns.Add(Column("原始编码", "Result.Current", .5)); grid.Columns.Add(Column("范围", "Result.Maximum", .5)); grid.Columns.Add(Column("结果", "Result.Message", 1.5)); grid.Columns.Add(Column("来源", "Capability.Provider", 1.2)); grid.ItemsSource = snapshot.Readings; if (focusedFeature != null) { grid.SelectedItem = snapshot.Readings.FirstOrDefault(r => r.Capability.Definition.Id == focusedFeature); if (grid.SelectedItem != null) grid.ScrollIntoView(grid.SelectedItem); } body.Children.Add(Card(grid));
        }
        var advanced = Section("高级诊断", "扩展标准 GET 不能自动发现未知私有报文。完整扫描较慢，部分设备会超时。"); advanced.Children.Add(AsyncButton("执行扩展标准 GET（只读）", async t => {
            if (simulation) { Say("模拟模式不启动硬件扫描"); return; }
            if (device == null || !Confirm("将查询标准 VCP 全码范围（跳过命令项），最长 180 秒。不会写入。继续？")) return;
            var ddc = await ScanExpanded(device, t); var copy = Library.Copy(device); copy.Ddc = ddc;
            string id = Library.NewId(); Library.Save("legacy-observations", id, copy); Say("扩展结果已保存：" + Path.Combine(Library.Folder("legacy-observations"), id + ".json"));
        })); body.Children.Add(Card(advanced));
    }
    static DataGrid Table() => new() { MinHeight = 120, MaxHeight = 470, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    static DataGridTextColumn Column(string title, string binding, double weight) => new() { Header = title, Binding = new System.Windows.Data.Binding(binding), Width = new DataGridLength(weight, DataGridLengthUnitType.Star), MinWidth = 80 };
    void SettingsPage()
    {
        UpdatesSection();
        if (!reader) {
            var p = Section("控制习惯", "数字滑块松开或停止键盘调整后，只提交最新设置；也可以手动应用。"); var toggle = new CheckBox { Content = "数字调节仅在点击“应用”后发送", IsChecked = manual, Margin = new Thickness(0, 8, 0, 16) };
            toggle.Click += (_, _) => { manual = toggle.IsChecked == true; var pref = Preferences(); pref.ManualApply = manual; Library.Save("preferences", pref.Id, pref); Say("设置已保存"); }; p.Children.Add(toggle); body.Children.Add(Card(p));
        }
        var help = Section("如何使用", "1. 选显示器，等待读取；亮度直接拖动，预设直接点击。\n2. 私有功能先在适配包页导入，再应用到这台设备。\n3. 读取器：一键检测 → 查看验证结果 → 导出或直接应用适配包；未知私有功能可生成 AI 提示词。\n4. 方案应用前预检；异常先核查，不盲目重发。\n5. 标准功能读取失败、菜单未适配与当前模式锁定是不同状态。"); help.Children.Add(Text("共享资料：" + Packages.Home, 13, "#64748C")); help.Children.Add(Text("所有测试与后台同步均不会主动激活窗口。切换工具按钮由用户点击后才显示目标。", 13, "#64748C")); body.Children.Add(Card(help));
        var nickname = Section("设备昵称", "昵称只保存在用户资料，不改显示器和协议包。"); var name = new TextBox { Text = device == null ? "" : Preferences().Nicknames.GetValueOrDefault(device.DevicePath, device.Name), MinHeight = 34 }; nickname.Children.Add(name); nickname.Children.Add(Button("保存昵称", () => { if (device == null) return; var pref = Preferences(); pref.Nicknames[device.DevicePath] = name.Text; Library.Save("preferences", pref.Id, pref); device.Nickname = name.Text; devices.Items.Refresh(); ShowPage(); Say("昵称已保存：" + name.Text); })); body.Children.Add(Card(nickname));
    }
    UserPreferences Preferences() => Library.All<UserPreferences>("preferences").FirstOrDefault() ?? new();
    HashSet<string> Favorites() => Preferences().Favorites;
    void ToggleFavorite(string id) { var pref = Preferences(); if (!pref.Favorites.Add(id)) pref.Favorites.Remove(id); Library.Save("preferences", pref.Id, pref); ShowPage(); }
    bool Confirm(string message) => Dialog("确认操作", message, null, null).Accepted;
    string? Prompt(string title, string label, string value) { var result = Dialog(title, label, value, null); return result.Accepted ? result.Value : null; }
    (bool Accepted, string Value) Dialog(string title, string message, string? value, string? detail)
    {
        if (!IsVisible) return (false, "");
        var window = new Window { Title = title, Owner = this, Icon = Icon, Width = 530, MaxHeight = 720, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Foreground, FontFamily = FontFamily, FontSize = 14 };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DisplayControl.UI;component/Theme.xaml", UriKind.Relative) });
        var p = new StackPanel { Margin = new Thickness(26) }; p.Children.Add(Text(message)); if (detail != null) p.Children.Add(Text(detail, 12, "#64748C")); var box = new TextBox { Text = value ?? "", MinHeight = 36, Margin = new Thickness(0, 8, 0, 18), Visibility = value == null ? Visibility.Collapsed : Visibility.Visible }; p.Children.Add(box); var actions = Actions(); actions.Children.Add(Button("确认", () => window.DialogResult = true, true)); actions.Children.Add(Button("取消", () => window.DialogResult = false)); p.Children.Add(actions); window.Content = p; return (window.ShowDialog() == true, box.Text);
    }
    async Task<DdcResult> ScanExpanded(MonitorData selected, CancellationToken token)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--ddc-expanded"); start.ArgumentList.Add(selected.Device); start.ArgumentList.Add(selected.DevicePath);
        using var process = System.Diagnostics.Process.Start(start)!; using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(180000);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token); var error = process.StandardError.ReadToEndAsync(limit.Token);
        try { await Task.WhenAll(output, error, process.WaitForExitAsync(limit.Token)); if (process.ExitCode != 0) throw new IOException(await error); return JsonSerializer.Deserialize<DdcResult>(await output) ?? throw new IOException("空诊断结果"); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
public sealed class UserPreferences
{
    public string Id { get; set; } = "00000000000000000000000000000001";
    public bool ManualApply { get; set; }
    public bool CompactControls { get; set; }
    public HashSet<string> Favorites { get; set; } = [];
    public Dictionary<string, string> Nicknames { get; set; } = [];
}

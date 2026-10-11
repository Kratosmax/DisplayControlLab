using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MonitorInspector;
using MonitorInspector.Updates;

namespace DisplayControl.UI;
public partial class WorkspaceWindow
{
    readonly UpdateClient updater = new();
    CancellationTokenSource? updateTask;
    UpdateInfo? availableUpdate;
    UpdateSettings? checkedNetwork;
    string updateMessage = "尚未检查更新";
    UpdateProgress? updateProgress;
    StackPanel? updatePanel;
    readonly List<(TextBox Address, ComboBox Priority, bool Direct)> routeEditors = [];
    TextBox? httpProxyEditor;
    CheckBox? automaticUpdatesEditor;
    bool installPrepared;
    string? startupUpdateNotice;
    public void SetStartupUpdateNotice(string message) => startupUpdateNotice = message;
    void UpdatesSection()
    {
        updatePanel = Section("软件更新", $"当前版本 {UpdateIdentity.CurrentVersion.ToString(3)} · {updater.Channel} 通道");
        RenderUpdatePanel(); body.Children.Add(Card(updatePanel));
        var settings = UpdateSettings.Load();
        var network = Section("网络与下载线路", "优先级 10 最先、0 禁用；同级按列表顺序回退。GitHub 前缀代理和 HTTP 网络代理可独立使用。");
        automaticUpdatesEditor = new CheckBox { Content = "启动后在后台检查更新", IsChecked = settings.CheckOnStartup, Margin = new Thickness(0, 0, 0, 14) };
        network.Children.Add(automaticUpdatesEditor);
        routeEditors.Clear(); var rows = new StackPanel();
        void AddRoute(UpdateRoute route)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) }; grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(100) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(72) });
            var address = new TextBox { Text = route.IsDirect ? "GitHub 直连（不可删除）" : route.BaseUrl, IsReadOnly = route.IsDirect, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0), ToolTip = "HTTPS 前缀，例如 https://proxy.example/github" };
            var priority = new ComboBox { ItemsSource = Enumerable.Range(0, 11).Reverse(), SelectedItem = route.Priority, Style = (Style)FindResource("CompactModeSelect"), MinHeight = 36, Margin = new Thickness(0, 0, 8, 0), ToolTip = "优先级：0 禁用，10 最高" };
            grid.Children.Add(address); Grid.SetColumn(priority, 1); grid.Children.Add(priority);
            var remove = Button("删除", () => { routeEditors.RemoveAll(r => r.Address == address); rows.Children.Remove(grid); }); remove.IsEnabled = !route.IsDirect; remove.Margin = new Thickness(0); Grid.SetColumn(remove, 2); grid.Children.Add(remove);
            routeEditors.Add((address, priority, route.IsDirect)); rows.Children.Add(grid);
        }
        foreach (var route in settings.Routes) AddRoute(route);
        network.Children.Add(rows); network.Children.Add(Button("添加 GitHub 前缀线路", () => AddRoute(new("", 5))));
        network.Children.Add(Text("HTTP 网络代理", 14));
        httpProxyEditor = new TextBox { Text = settings.HttpProxy ?? "", MinHeight = 36, Margin = new Thickness(0, 0, 0, 8), ToolTip = "http://127.0.0.1:7890；留空为不使用网络代理" }; network.Children.Add(httpProxyEditor);
        network.Children.Add(Text("仅支持无账号的 HTTP 代理。前缀线路会看到完整 GitHub 下载地址；所有线路都执行相同的签名与包校验。", 12, "#64748C"));
        var actions = Actions(); actions.Children.Add(Button("保存网络设置", () => { ReadNetworkEditors().Save(); Say("网络设置已保存，后台检查将使用新配置。"); }));
        actions.Children.Add(Button("用当前设置检查", () => _ = CheckUpdates(ReadNetworkEditors(), false))); network.Children.Add(actions); body.Children.Add(Card(network));
    }
    UpdateSettings ReadNetworkEditors()
    {
        var saved = UpdateSettings.Load(); saved.CheckOnStartup = automaticUpdatesEditor?.IsChecked == true; saved.HttpProxy = httpProxyEditor?.Text;
        saved.Routes = routeEditors.Select(r => new UpdateRoute(r.Direct ? "" : r.Address.Text, (int)(r.Priority.SelectedItem ?? 0), r.Direct)).ToList(); return saved.Normalize();
    }
    void RenderUpdatePanel()
    {
        if (updatePanel == null) return;
        while (updatePanel.Children.Count > 2) updatePanel.Children.RemoveAt(2);
        updatePanel.Children.Add(Text(updateMessage, 14, "#425A88"));
        if (availableUpdate != null) {
            updatePanel.Children.Add(Text($"版本 {availableUpdate.Version.ToString(3)} · {availableUpdate.Channel} · {availableUpdate.Size / 1048576.0:F1} MB", 15));
            updatePanel.Children.Add(new ScrollViewer { Content = Text(availableUpdate.ReleaseNotes, 13), MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        if (updateProgress != null) {
            updatePanel.Children.Add(Text(updateProgress.Stage + " · " + updateProgress.Route, 12, "#64748C"));
            updatePanel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = updateProgress.Percent ?? 0, IsIndeterminate = updateProgress.Percent == null, Height = 6, Margin = new Thickness(0, 0, 0, 14), Foreground = Brush("#3D5DD8") });
        }
        var actions = Actions(); var check = Button("检查更新", () => _ = CheckUpdates(UpdateSettings.Load(), false)); check.IsEnabled = updateTask == null; actions.Children.Add(check);
        if (availableUpdate != null && updateTask == null) {
            var install = Button("更新并重启", () => _ = InstallUpdate(), true); install.IsEnabled = updater.CanInstall; actions.Children.Add(install);
            actions.Children.Add(Button("稍后", () => { availableUpdate = null; updateMessage = "已推迟本次更新，可随时重新检查。"; RenderUpdatePanel(); }));
            actions.Children.Add(Button("跳过此版本", () => { var s = UpdateSettings.Load(); s.SkippedVersion = availableUpdate.Version.ToString(3); s.Save(); availableUpdate = null; updateMessage = "已跳过此版本，手动检查仍可查看。"; RenderUpdatePanel(); }));
        }
        if (updateTask != null) actions.Children.Add(Button("取消下载或检查", () => updateTask.Cancel()));
        actions.Children.Add(Button("打开下载页面", () => Process.Start(new ProcessStartInfo(UpdateTrust.Repository + "/releases/latest") { UseShellExecute = true })));
        updatePanel.Children.Add(actions);
        updatePanel.Children.Add(Text(updater.CanInstall ? "更新前请退出另一工具；正在发送的显示器设置必须完成。更新仅替换软件，保留用户适配包、方案和记录。" : "开发目录不能就地更新，请下载正式 ZIP 或安装包。", 12, "#64748C"));
    }
    async Task CheckUpdates(UpdateSettings settings, bool background)
    {
        if (simulation || updateTask != null) return;
        updateTask = new CancellationTokenSource(); updateMessage = "正在检查更新…"; RenderUpdatePanel();
        try {
            var info = await updater.Check(settings, updateTask.Token);
            if (background && info?.Version.ToString(3) == settings.SkippedVersion) { updateMessage = "已跳过版本 " + settings.SkippedVersion; return; }
            availableUpdate = info; checkedNetwork = settings.Normalize(); updateMessage = info == null ? "已是最新版本" : "发现新版本，签名已验证";
            if (info != null) { Say("发现 DisplayControlLab " + info.Version.ToString(3) + "，可在设置与帮助中更新。"); banner.Text = "发现新版本 " + info.Version.ToString(3) + " · 在设置与帮助中查看更新"; }
        } catch (OperationCanceledException) { updateMessage = "检查已取消"; }
        catch (Exception ex) { updateMessage = "检查失败：" + ex.Message; if (!background) Say(updateMessage); }
        finally { updateTask.Dispose(); updateTask = null; RenderUpdatePanel(); }
    }
    async Task InstallUpdate()
    {
        if (updateTask != null || availableUpdate == null || simulation) return;
        if (active != null || writingDevices.Count > 0 || pendingWrites.Count > 0 || modeReloading.Count > 0) { Say("请等待当前显示器操作完成，再更新软件。"); return; }
        if (!Confirm("将下载并验证更新，然后退出当前工具。请先退出另一工具；更新器不会强制关闭任何程序，60秒内仍有工具运行会取消替换。用户资料保持不变。继续？")) return;
        updateTask = new CancellationTokenSource(); updateMessage = "正在准备更新，尚未修改安装目录。"; RenderUpdatePanel();
        try {
            var prepared = await updater.Prepare(availableUpdate, checkedNetwork ?? UpdateSettings.Load(), new Progress<UpdateProgress>(p => { if (updateProgress == p) return; updateProgress = p; RenderUpdatePanel(); }), updateTask.Token);
            if (active != null || writingDevices.Count > 0 || pendingWrites.Count > 0 || modeReloading.Count > 0) throw new IOException("下载期间有显示器操作，请完成后重新更新");
            if (draft != null) SaveDraft();
            updater.Launch(prepared, reader); installPrepared = true; updateMessage = "已交给更新器，等待工具退出。";
        } catch (OperationCanceledException) { updateMessage = "更新已取消，原安装未修改。"; }
        catch (Exception ex) { updateMessage = "准备更新失败，原安装未修改：" + ex.Message; Say(updateMessage); }
        finally { updateTask.Dispose(); updateTask = null; RenderUpdatePanel(); }
        if (installPrepared) Close();
    }
    void RunUpdateUiTests(string directory, Action<bool, string> check)
    {
        IEnumerable<DependencyObject> Descendants(DependencyObject node) { yield return node; foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) foreach (var descendant in Descendants(child)) yield return descendant; }
        Navigate("settings");
        check(routeEditors.Count == 1 && routeEditors[0].Direct && routeEditors[0].Address.IsReadOnly, "更新网络默认直连只读且不可删除");
        check(automaticUpdatesEditor?.IsChecked == true, "启动后台检查默认开启");
        httpProxyEditor!.Text = "http://127.0.0.1:7890";
        var temporary = ReadNetworkEditors(); check(temporary.HttpProxy == "http://127.0.0.1:7890" && UpdateSettings.Load().HttpProxy == null, "未保存网络编辑不影响后台配置");
        temporary.Save(); check(UpdateSettings.Load().HttpProxy == temporary.HttpProxy, "保存更新网络设置持久化");
        new UpdateSettings().Save(); Navigate("settings");
        var add = Descendants(updatePanel!.Parent as DependencyObject ?? body).OfType<Button>().FirstOrDefault(b => Equals(b.Content, "添加 GitHub 前缀线路"));
        add ??= Descendants(body).OfType<Button>().Single(b => Equals(b.Content, "添加 GitHub 前缀线路"));
        add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(routeEditors.Count == 2, "统一设置页添加前缀线路");
        routeEditors[1].Address.Text = "https://proxy.example/github"; routeEditors[1].Priority.SelectedItem = 5;
        check(ReadNetworkEditors().Routes[1].BaseUrl == "https://proxy.example/github", "前缀支持路径与固定优先级选项");
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 }) Capture(Path.Combine(directory, $"updates-settings-{dpi}.png"), "settings", 1040, 700, dpi);
        availableUpdate = new UpdateInfo(new Version(0, 4, 0), "Lite", new Uri(UpdateTrust.Repository + "/releases/download/v0.4.0/DisplayControlLab-0.4.0-Lite.zip"), 1048576, new string('A', 64), string.Join("\n", Enumerable.Repeat("当前版本功能说明，长文字也应在滚动区域显示。", 30)), "UI-only fixture");
        updateMessage = "发现新版本，签名已验证（界面测试样例）"; updateProgress = new("下载", "GitHub 直连", 37);
        Capture(Path.Combine(directory, "updates-available.png"), "settings", 1480, 900, 1);
        check(Descendants(updatePanel!).OfType<ScrollViewer>().Any(s => s.MaxHeight == 200), "更新公告长度受滚动区域限制");
        check(Descendants(updatePanel!).OfType<ProgressBar>().Single().Value == 37, "更新阶段和确定进度显示");
        check(Descendants(updatePanel!).OfType<Button>().Any(b => Equals(b.Content, "更新并重启")), "两工具统一提供更新安装入口");
        updateProgress = new("连接线路", "测试线路", null); RenderUpdatePanel();
        check(Descendants(updatePanel!).OfType<ProgressBar>().Single().IsIndeterminate, "未知下载大小与连接阶段使用不确定进度");
        updateTask = new CancellationTokenSource(); RenderUpdatePanel();
        check(!Descendants(updatePanel!).OfType<Button>().Single(b => Equals(b.Content, "检查更新")).IsEnabled && Descendants(updatePanel!).OfType<Button>().Any(b => Equals(b.Content, "取消下载或检查")), "更新任务防重复提交且可取消");
        updateTask.Dispose(); updateTask = null; updateProgress = null; RenderUpdatePanel();
        Descendants(updatePanel!).OfType<Button>().Single(b => Equals(b.Content, "跳过此版本")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(availableUpdate == null && UpdateSettings.Load().SkippedVersion == "0.4.0", "跳过版本持久化");
        updateMessage = "检查失败：所有更新线路均失败。GitHub 直连：网络超时";
        Capture(Path.Combine(directory, "updates-network-failure.png"), "settings", 1040, 700, 1);
        check(Descendants(updatePanel!).OfType<Button>().Any(b => Equals(b.Content, "打开下载页面")), "网络失败仍保留手动下载入口");
        new UpdateSettings().Save(); updateMessage = "尚未检查更新"; updateProgress = null; availableUpdate = null;
    }
    async Task StartUpdateChecks()
    {
        if (simulation) return;
        await Task.Delay(4000);
        if (!IsVisible) return;
        try { await Task.Run(UpdateClient.Cleanup); var settings = UpdateSettings.Load(); if (settings.CheckOnStartup) await CheckUpdates(settings, true); }
        catch { /* Cleanup and update checks must never stop device control. */ }
    }
}

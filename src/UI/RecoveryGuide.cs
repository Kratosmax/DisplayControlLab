using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MonitorInspector;

namespace DisplayControl.UI;

public partial class WorkspaceWindow
{
    static bool NeedsRecovery(ReaderSurvey report, SurveyFeature feature) => report.RecoveryRequired
        && feature.WriteStatus is "恢复未确认" or "待恢复核查";

    void ConfigureRecoveryRows(DataGrid table, ReaderSurvey report, StackPanel details)
    {
        var rowStyle = new Style(typeof(DataGridRow), table.TryFindResource(typeof(DataGridRow)) as Style);
        var warning = new DataTrigger { Binding = new Binding("NeedsRecovery"), Value = true };
        warning.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)new BrushConverter().ConvertFromString("#FFF3D6")!));
        warning.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#804E00")!));
        warning.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "点击查看核查步骤；也可选中后按 Enter。"));
        warning.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        rowStyle.Triggers.Add(warning); table.RowStyle = rowStyle;
        var cellStyle = new Style(typeof(DataGridCell), table.TryFindResource(typeof(DataGridCell)) as Style);
        var cellWarning = new DataTrigger { Binding = new Binding("NeedsRecovery"), Value = true };
        cellWarning.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)new BrushConverter().ConvertFromString("#FFF3D6")!));
        cellWarning.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#804E00")!));
        cellStyle.Triggers.Add(cellWarning);
        var selectedWarning = new MultiDataTrigger();
        selectedWarning.Conditions.Add(new Condition(new Binding("NeedsRecovery"), true));
        selectedWarning.Conditions.Add(new Condition(new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) }, true));
        selectedWarning.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)new BrushConverter().ConvertFromString("#FFE0A3")!));
        cellStyle.Triggers.Add(selectedWarning); table.CellStyle = cellStyle;
        void Open(object? item) {
            if (item?.GetType().GetProperty("Feature")?.GetValue(item) is not SurveyFeature feature || !NeedsRecovery(report, feature)) return;
            details.Children.Clear(); details.Children.Add(RecoveryGuide(report, feature)); details.Visibility = Visibility.Visible;
            details.BringIntoView();
        }
        table.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, e) => {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(table, source) is DataGridRow row) Open(row.Item);
        }), true);
        table.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && table.SelectedItem != null) { Open(table.SelectedItem); e.Handled = true; } };
    }

    StackPanel RecoveryGuide(ReaderSurvey report, SurveyFeature feature)
    {
        var baseline = report.RecoveryBaseline;
        var before = baseline?.Readings.FirstOrDefault(r => r.Capability.Definition.Id == feature.Definition.Id);
        var original = before?.Result ?? feature.Samples.FirstOrDefault();
        string Value(ControlDefinition definition, ControlResult? result, bool? hdr) => result == null ? "未记录"
            : new Reading { Capability = new() { Definition = definition }, Result = result }.Display(hdr)
                + (result.Current.HasValue ? $"（原始数值 {result.Current} / 编码 0x{result.Current:X2}）" : "");
        var content = Section("把“" + feature.Definition.Name + "”改回原来的设置", report.Device.DisplayName + " · 检测已暂停，等待你确认恢复。");
        var originalName = original == null ? "" : new Reading { Capability = new() { Definition = before?.Capability.Definition ?? feature.Definition }, Result = original }.Display(report.Device.WindowsHdrEnabled);
        bool known = original?.Status == "ok" && original.Current.HasValue && !new[] { "编码", "未识别", "未知", "待查", "候选" }.Any(originalName.Contains);
        content.Children.Add(Text(known ? "原来的设置：" + originalName : original?.Current == null
            ? "没有记下原来的设置，暂时无法确认恢复。请先停止试调。"
            : "软件没认出原选项的名称，需要你按检测前的设置改回。", 14, "#9A6612"));
        content.Children.Add(Text(string.IsNullOrWhiteSpace(feature.Definition.MenuPath)
            ? $"1. 用显示器按键打开菜单，把“{feature.Definition.Name}”相关选项改回检测前的设置。"
            : $"1. 用显示器按键打开“{feature.Definition.MenuPath}”，改回上面的设置。", 14));
        content.Children.Add(Text("2. 关闭显示器菜单，等画面稳定，再点下面的按钮。", 14));
        var feedback = Text("只检查，不会修改显示器。", 13, "#64748C");
        var technical = new StackPanel();
        var verificationDetails = new StackPanel();
        var actions = Actions();
        var verify = AsyncButton("我已改回，检查一下", t => VerifyRecovery(report, feedback, verificationDetails, t), true);
        verify.IsEnabled = baseline is { Complete: true, Readings.Count: > 0 };
        actions.Children.Add(verify);
        actions.Children.Add(Button("不记得原来的设置", () => feedback.Text = "先停止试调，不要猜选项或恢复出厂。记录已保存，可展开技术详情查证。"));
        content.Children.Add(actions); content.Children.Add(feedback);
        technical.Children.Add(Text($"检测记录 {report.Time:MM-dd HH:mm:ss}；以下为保存的读数，不是实时状态。", 13));
        technical.Children.Add(Text("试调前：" + Value(before?.Capability.Definition ?? feature.Definition, original, report.Device.WindowsHdrEnabled), 13));
        var operationId = feature.Probes.LastOrDefault(p => !string.IsNullOrWhiteSpace(p.OperationId))?.OperationId ?? feature.OperationId;
        var operation = operationId == null ? null : Library.All<Operation>("operations").FirstOrDefault(o => o.Id == operationId);
        var after = operation?.After;
        if (baseline != null && after != null && HardwareReports.SameDevice(baseline.Device, after.Device)) {
            var trial = after.Readings.FirstOrDefault(r => r.Capability.Definition.Id == feature.Definition.Id);
            technical.Children.Add(Text("保存的试调后状态：" + Value(trial?.Capability.Definition ?? feature.Definition, trial?.Result, after.Device.WindowsHdrEnabled), 13));
            technical.Children.Add(Text("试调后观察到的变化（含联动）：", 13));
            var changes = Comparison.Between(baseline, after);
            foreach (var difference in changes) technical.Children.Add(Text($"• {difference.Feature}：{difference.Before} → {difference.After}\n  {difference.Evidence}", 13));
            if (changes.Count == 0) technical.Children.Add(Text("保存记录没有显示数值差异；来源、连接或读取失败也可能导致恢复未确认。", 13));
        } else technical.Children.Add(Text("缺少完整的试调后记录，无法确定联动项目。", 13));
        technical.Children.Add(Text("读数联动不等于实际设置改变；多个控制码可能返回相同状态。未知编码和未确认的菜单路径不能作为恢复依据。", 13));
        technical.Children.Add(Text("停止原因：" + feature.Note + (feature.ErrorDetails.Length > 0 ? "\n" + feature.ErrorDetails : ""), 13, "#64748C"));
        technical.Children.Add(verificationDetails);
        content.Children.Add(new Expander { Header = "技术详情（可选）", Content = technical, Margin = new Thickness(0, 12, 0, 0) });
        return content;
    }

    async Task VerifyRecovery(ReaderSurvey report, TextBlock feedback, StackPanel technical, CancellationToken token)
    {
        feedback.Text = "正在检查，请不要操作显示器菜单…";
        technical.Children.Clear();
        try {
            var current = await workspace.Capture(Library.Copy(report.Device), token);
            token.ThrowIfCancellationRequested();
            report.RecoveryObserved = Library.Copy(current);
            if (!ReaderSurvey.RecoveryMatches(report.RecoveryBaseline, current)) {
                Library.Save("reader-surveys", report.Id, report);
                feedback.Text = current.Complete ? "还没确认恢复。请核对原来的设置；不记得就先停止试调。" : "没读到完整状态，请关闭显示器菜单后重试。";
                if (report.RecoveryBaseline != null) foreach (var difference in Comparison.Between(report.RecoveryBaseline, current))
                    technical.Children.Add(Text($"本次检查：{difference.Feature} · {difference.Before} → {difference.After}\n{difference.Evidence}", 13));
                Say(feedback.Text); return;
            }
            report.RecoveryRequired = false; report.Status = "原值已核查恢复，请重新检测";
            Library.Save("reader-surveys", report.Id, report);
            ShowPage(); Say("已确认恢复。可以继续检测了。");
        } catch (OperationCanceledException) { feedback.Text = "检查已取消，尚未确认恢复。"; throw; }
        catch (Exception ex) {
            feedback.Text = "检查没完成。请确认连接正常，再试一次。";
            technical.Children.Add(Text(ex.ToString(), 13)); Say(feedback.Text);
        }
    }

    async Task RunRecoveryGuideTests(string directory, Action<bool, string> check, Snapshot sample, SimulatedDisplayBackend fixture)
    {
        IEnumerable<DependencyObject> Children(DependencyObject node) {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) { yield return child; foreach (var nested in Children(child)) yield return nested; }
        }
        var baseline = Library.Copy(sample);
        var color = baseline.Readings.Single(r => r.Capability.Definition.Id == "color-preset");
        color.Capability.Definition = Library.Copy(DeviceControl.Standard.Single(f => f.Id == "color-preset"));
        color.Result.Current = 12; color.Result.Maximum = 12;
        var volume = baseline.Readings.FirstOrDefault(r => r.Capability.Definition.Id == "volume");
        if (volume == null) { volume = new Reading { Capability = new() { Definition = Library.Copy(DeviceControl.Standard.Single(f => f.Id == "volume")) } }; baseline.Readings.Add(volume); }
        volume.Result = new() { Status = "ok", Current = 12, Maximum = 255 };
        var operation = new Operation { Before = Library.Copy(baseline), After = Library.Copy(baseline) };
        operation.After.Readings.Single(r => r.Capability.Definition.Id == "color-preset").Result.Current = 5;
        operation.After.Readings.Single(r => r.Capability.Definition.Id == "volume").Result.Current = 5;
        Library.Save("operations", operation.Id, operation);
        var feature = new SurveyFeature { Definition = color.Capability.Definition, WriteStatus = "恢复未确认", Samples = [color.Result],
            Probes = [new() { OperationId = operation.Id }], Note = "观察到色温/RGB以外的联动，需人工核查" };
        var report = new ReaderSurvey { Device = baseline.Device, RecoveryRequired = true, RecoveryBaseline = baseline };
        int requests = fixture.Requests.Count;
        var table = Table(); table.Columns.Add(Column("功能", "Feature.Definition.Name", 1)); table.Columns.Add(Column("操作", "Hint", 1));
        table.ItemsSource = new[] { new { Feature = feature, NeedsRecovery = true, Hint = "待核查 · 点击查看步骤" },
            new { Feature = new SurveyFeature { Definition = new() { Name = "正常项目" } }, NeedsRecovery = false, Hint = "已验证" } };
        var details = new StackPanel { Visibility = Visibility.Collapsed };
        ConfigureRecoveryRows(table, report, details);
        var host = new StackPanel(); host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DisplayControl.UI;component/Theme.xaml", UriKind.Relative) });
        host.Children.Add(table); host.Children.Add(details);
        host.Measure(new Size(860, double.PositiveInfinity)); host.Arrange(new Rect(0, 0, 860, host.DesiredSize.Height)); host.UpdateLayout();
        var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
        check(((SolidColorBrush)row.Background).Color == (Color)ColorConverter.ConvertFromString("#FFF3D6"), "待核查行使用橙色强调，并提示点击查看步骤");
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        var text = string.Join("\n", Children(details).OfType<TextBlock>().Select(t => t.Text));
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "recovery-guide-text.txt"), details.Visibility + "\n" + text);
        check(details.Visibility == Visibility.Visible && text.Contains("原始数值 12") && text.Contains("音量") && text.Contains("原始数值 5"), "点击待核查行展开真实原值和保存的联动差异，优先使用色温探测操作记录");
        var guide = (StackPanel)details.Children[0];
        var defaultText = string.Join("\n", guide.Children.OfType<TextBlock>().Select(t => t.Text));
        check(defaultText.Length < 350 && !defaultText.Contains("编码") && !defaultText.Contains("System.")
            && Children(guide).OfType<Expander>().Single().IsExpanded == false, "核查默认只显示简短操作，编码/联动/报错默认折叠");
        check(defaultText.Contains("显示器按键") && defaultText.Contains("软件没认出") && Children(guide).OfType<Button>().Any(b => Equals(b.Content, "我已改回，检查一下")), "未知名称直说，提供就地只读检查按钮，无需取消勾选再找检测入口");
        Children(guide).OfType<Button>().Single(b => Equals(b.Content, "不记得原来的设置")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(guide.Children.OfType<TextBlock>().Any(t => t.Text.Contains("先停止试调")), "不记得原选项时提供明确反馈，不要求猜码或恢复出厂");
        table.SelectedItem = table.Items[0]; table.UpdateLayout();
        var cells = VisualCells(row).ToList();
        check(cells.Any(c => c.IsSelected) && cells.Where(c => c.IsSelected).All(c => ((SolidColorBrush)c.Background).Color == (Color)ColorConverter.ConvertFromString("#FFE0A3")), "待核查行选中后仍为强调色，不被通用蓝色覆盖");
        check(!NeedsRecovery(new ReaderSurvey(), feature), "已解除恢复门禁的报告不继续标为待核查");
        var missing = RecoveryGuide(new ReaderSurvey { Device = sample.Device, RecoveryRequired = true }, new SurveyFeature { Definition = feature.Definition });
        check(Children(missing).OfType<TextBlock>().Any(t => t.Text.Contains("没有记下"))
            && !Children(missing).OfType<Button>().Single(b => Equals(b.Content, "我已改回，检查一下")).IsEnabled, "缺基线时明确无法确认，禁用检查按钮，不猜恢复目标");
        check(fixture.Requests.Count == requests, "打开核查引导不读取或写入硬件");
        foreach (int width in new[] { 860, 620 }) {
            host.Measure(new Size(width, double.PositiveInfinity)); host.Arrange(new Rect(0, 0, width, host.DesiredSize.Height)); host.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            bitmap.Render(background); bitmap.Render(host);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = System.IO.File.Create(System.IO.Path.Combine(directory, $"recovery-guide-{width}.png")); encoder.Save(output);
        }
        feature.ReadStatus = "读取已验证"; feature.CanExport = true;
        report.Features = [feature, new() { Definition = Library.Copy(DeviceControl.Standard.Single(f => f.Id == "brightness")), ReadStatus = "读取已验证", WriteStatus = "试写及恢复已验证", CanExport = true, Samples = [new() { Status = "ok", Current = 50, Maximum = 100 }] }];
        report.Rounds = [Library.Copy(baseline), Library.Copy(baseline), Library.Copy(baseline)];
        report.Status = "需要核查恢复"; report.RecoveryObserved = Library.Copy(operation.After);
        Library.Save("reader-surveys", report.Id, report); ShowPage();
        check(Children(body).OfType<Button>().Where(b => Equals(b.Content, "导出控制器适配包") || Equals(b.Content, "直接应用到控制器")).Count(b => b.IsEnabled) == 2,
            "待核查恢复时应用和导出按钮仍可用，不把整个结果锁住");
        check(report.Package().Controls.Single(f => f.Id == "brightness").Writable && report.Package().Controls.All(f => f.Id != "color-preset"),
            "实际结果页对应包仅限制受影响项，保留无关亮度调节");
        Capture(System.IO.Path.Combine(directory, "reader-partial-result.png"), "guided", 1040, 700, 1.25);
        Library.Delete("reader-surveys", report.Id);
        Library.Delete("operations", operation.Id);
        var recovery = new ReaderSurvey { Device = Library.Copy(sample.Device), RecoveryRequired = true, RecoveryBaseline = await workspace.Capture(sample.Device, default) };
        Library.Save("reader-surveys", recovery.Id, recovery);
        var recoveredGuide = RecoveryGuide(recovery, feature);
        var buttons = Children(recoveredGuide).OfType<Button>().ToList();
        var checkButton = buttons.Single(b => Equals(b.Content, "我已改回，检查一下"));
        var brightness = fixture.Values["brightness"]; fixture.Values["brightness"] = brightness == 85 ? 84u : 85u;
        fixture.Requests.Clear();
        checkButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        for (int i = 0; active != null && i < 3000; i++) await Task.Delay(10);
        check(recovery.RecoveryRequired && Children(recoveredGuide).OfType<TextBlock>().Any(t => t.Text.Contains("还没确认恢复")), "就地检查不匹配时显示简短反馈，保留恢复门禁");
        fixture.Values["brightness"] = brightness;
        checkButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        for (int i = 0; active != null && i < 3000; i++) await Task.Delay(10);
        check(!Library.Get<ReaderSurvey>("reader-surveys", recovery.Id).RecoveryRequired && uiMessage.Contains("已确认恢复"), "按钮只读核查完整基线匹配后保存确认状态");
        check(fixture.Requests.Count > 0 && fixture.Requests.All(r => r.Action == "read"), "核查按钮仅发送读取，不试写、不恢复设置、不重新扫描");
        Library.Delete("reader-surveys", recovery.Id);
    }

    static IEnumerable<DataGridCell> VisualCells(DependencyObject node)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is DataGridCell cell) yield return cell;
            foreach (var nested in VisualCells(child)) yield return nested;
        }
    }
}

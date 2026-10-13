using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MonitorInspector;

namespace DisplayControl.UI;

public partial class WorkspaceWindow
{
    readonly Dictionary<string, string> openedFeaturePanels = [];
    readonly Dictionary<string, Reading> featurePanelReadings = [];
    void AddFeatureTestColumn(DataGrid table, ReaderSurvey report, StackPanel details)
    {
        var factory = new FrameworkElementFactory(typeof(Button));
        factory.SetValue(ContentControl.ContentProperty, "测试 / 重试");
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
        factory.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler((sender, _) => {
            var context = ((Button)sender).DataContext;
            if (context?.GetType().GetProperty("Feature")?.GetValue(context) is not SurveyFeature item) return;
            openedFeaturePanels[report.Device.DevicePath] = item.Definition.Id;
            details.Children.Clear(); details.Children.Add(FeatureTestPanel(report, item)); details.Visibility = Visibility.Visible; details.BringIntoView();
        }));
        table.Columns.Add(new DataGridTemplateColumn { Header = "操作", CellTemplate = new DataTemplate { VisualTree = factory }, Width = 135, MinWidth = 135 });
    }

    StackPanel FeatureTestPanel(ReaderSurvey report, SurveyFeature item)
    {
        var content = Section("测试“" + item.Definition.Name + "”", item.TestReason);
        var feedback = Text("测试会短暂改变设置，完成后检查并恢复原值。", 13, "#64748C");
        var choice = new ComboBox { MinWidth = 210, Margin = new Thickness(0, 8, 0, 8), DisplayMemberPath = "Label", SelectedValuePath = "Id" };
        var definition = Library.Copy(item.Definition);
        if (definition.ValueType == "enum" && item.Backend == "standard-ddc") report.UseDeclaredStandardChoices(definition);
        var values = definition.Rules.SelectMany(r => r.Values.Select(v => new { Id = v.Key, Label = r.Labels.GetValueOrDefault(v.Key, v.Key) })).DistinctBy(v => v.Id).ToList();
        var recent = ReadSession?.Segments.Where(s => !s.Transition).SelectMany(s => s.Rounds).LastOrDefault(s => s.Complete);
        var recentReading = recent?.Readings.FirstOrDefault(r => r.Capability.Definition.Id == item.Definition.Id && r.Result.Status == "ok");
        string readingKey = report.Device.DevicePath + "|" + item.Definition.Id;
        if (featurePanelReadings.TryGetValue(readingKey, out var singleReading) && (recentReading == null || singleReading.Time > recentReading.Time)) recentReading = singleReading;
        uint? currentValue = recentReading?.Result.Current ?? item.Current;
        var observedRows = new List<(uint Code, Grid Row, TextBlock Label, string Prefix)>();
        TextBlock? observedHint = null;
        void UpdateObserved(uint? value) {
            currentValue = value;
            choice.SelectedValue = definition.Rules.SelectMany(r => r.Values).FirstOrDefault(v => v.Value == value).Key;
            if (observedHint != null) observedHint.Text = value.HasValue ? $"最近一次读取：编码 {value} / 0x{value:X2}。蓝色行就是刚读到的选项。" : "未读到有效值，保留名称，不能确认当前选项。";
            foreach (var row in observedRows) {
                bool selected = row.Code == value;
                row.Row.Background = selected ? Brush("#E9EDFF") : System.Windows.Media.Brushes.Transparent;
                row.Label.Foreground = Brush(selected ? "#3D5DD8" : "#202A3D"); row.Label.Text = row.Prefix + (selected ? "\n← 最近读到这一项" : "");
            }
        }
        var currentId = definition.Rules.SelectMany(r => r.Values).FirstOrDefault(v => v.Value == currentValue).Key;
        if (values.Count > 0) { choice.ItemsSource = values; choice.SelectedValue = currentId; if (choice.SelectedIndex < 0) choice.SelectedIndex = 0; content.Children.Add(choice); }
        if (values.Count > 0 && definition.ValueType is "enum" or "boolean") {
            var naming = new StackPanel();
            naming.Children.Add(Text("按显示器菜单填写名称。保存后，再应用到控制器或导出适配包。", 13, "#64748C"));
            observedHint = Text(currentValue.HasValue ? $"{(recentReading != null ? "最近一次读取" : "检测时读取")}：编码 {currentValue} / 0x{currentValue:X2}。改菜单后点“重新读取此项”，蓝色行会更新。" : "点击“重新读取此项”确认当前选项。", 13, "#425A88"); naming.Children.Add(observedHint);
            var editors = new Dictionary<string, TextBox>();
            foreach (var value in values) {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new() { Width = new GridLength(190) }); row.ColumnDefinitions.Add(new());
                var code = definition.Rules.First(r => r.Values.ContainsKey(value.Id)).Values[value.Id];
                bool observed = code == currentValue;
                string prefix = $"选项 {values.IndexOf(value) + 1} · {code} / 0x{code:X2}";
                var label = Text(prefix + (observed ? "\n← 最近读到这一项" : ""), 13, observed ? "#3D5DD8" : "#202A3D"); row.Children.Add(label); observedRows.Add((code, row, label, prefix));
                if (observed) row.Background = Brush("#E9EDFF");
                var editor = new TextBox { Text = value.Label, MaxLength = 80, MinHeight = 36, ToolTip = "例如：标准、电影、游戏" };
                Grid.SetColumn(editor, 1); row.Children.Add(editor); editors.Add(value.Id, editor); naming.Children.Add(row);
            }
            var saved = Text("", 13, "#326A7C");
            naming.Children.Add(Button("保存选项名称", () => {
                if (active != null || updateTask != null || installPrepared) { saved.Text = "请等当前任务完成后再保存。"; return; }
                try {
                    report.SaveOptionNames(item.Definition.Id, editors.ToDictionary(e => e.Key, e => e.Value.Text));
                    definition = Library.Copy(item.Definition);
                    var selected = choice.SelectedValue;
                    choice.ItemsSource = definition.Rules.SelectMany(r => r.Values.Select(v => new { Id = v.Key, Label = r.Labels.GetValueOrDefault(v.Key, v.Key) })).DistinctBy(v => v.Id).ToList();
                    choice.SelectedValue = selected;
                    saved.Text = "名称已保存。点击“直接应用到控制器”或重新导出即可使用。";
                    Say("选项名称已保存，测试结果保持不变。");
                } catch (Exception ex) { saved.Text = "未保存：" + ex.Message; }
            }));
            naming.Children.Add(saved);
            content.Children.Add(new Expander { Header = "给选项命名", Content = naming, Margin = new Thickness(0, 4, 0, 12) });
        }
        var number = new TextBox { MinWidth = 170, Margin = new Thickness(0, 8, 0, 8), Text = item.Current?.ToString() ?? "" };
        if (definition.ValueType == "number") { content.Children.Add(Text("手动测试值", 13)); content.Children.Add(number); }
        var actions = Actions();
        async Task Test(bool manual, CancellationToken token) {
            if (!simulation && !Confirm(ReaderSurveyRunner.NeedsManualConfirmation(item)
                ? "此功能可能黑屏、切换输入、关闭菜单或改变模式。只测试所选值，再尝试恢复；恢复失败需使用显示器按键处理。仍要测试？"
                : "会试调此功能并恢复原设置。测试时不要操作菜单或其他控制软件。开始？")) return;
            try {
                await new ReaderSurveyRunner(workspace).Retry(report, item.Definition.Id, manual ? definition.ValueType == "number" ? number.Text : choice.SelectedValue as string : null,
                    manual, Say, token);
                Say(item.TestReason);
            } finally { ShowPage(); }
        }
        var retry = AsyncButton("自动测试 / 重试", t => Test(false, t), true);
        var manualTest = AsyncButton("测试所选值", t => Test(true, t));
        var cannot = ReaderSurveyRunner.CannotTest(item);
        retry.IsEnabled = cannot.Length == 0 && !ReaderSurveyRunner.NeedsManualConfirmation(item) && !report.RecoveryRequired;
        manualTest.IsEnabled = cannot.Length == 0 && !report.RecoveryRequired && (definition.ValueType == "number" || values.Count > 0);
        actions.Children.Add(retry); actions.Children.Add(manualTest); content.Children.Add(actions);
        if (report.RecoveryRequired) feedback.Text = "先按橙色行确认恢复，再试写。";
        else if (cannot.Length > 0) feedback.Text = cannot + "。可在高级工具更新协议，再回到这里测试。";
        else if (ReaderSurveyRunner.NeedsManualConfirmation(item)) feedback.Text = "此项不自动试写。选择档位后，可由你确认进行一次测试。";
        content.Children.Add(feedback);
        content.Children.Add(Button("重新读取此项", () => _ = Run(async token => {
            var now = await workspace.Capture(report.Device, token, scope: [item.Definition.Id]);
            var reading = now.Readings.FirstOrDefault(r => r.Capability.Definition.Id == item.Definition.Id);
            if (reading?.Result.Status == "ok") { featurePanelReadings[readingKey] = Library.Copy(reading); UpdateObserved(reading.Result.Current); }
            else UpdateObserved(null);
            feedback.Text = reading?.Result.Status == "ok" ? "已读到当前设置，可以重试写入。" : "仍未读到，请检查连接或更新协议后重试。";
        })));
        return content;
    }

    async Task RunFeatureTestPanelTests(string directory, Action<bool, string> check, ReaderSurvey report, SimulatedDisplayBackend fixture)
    {
        IEnumerable<DependencyObject> Children(DependencyObject node) {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) { yield return child; foreach (var nested in Children(child)) yield return nested; }
        }
        var item = report.Features.Single(f => f.Definition.Id == "brightness");
        var panel = FeatureTestPanel(report, item);
        var retry = Children(panel).OfType<Button>().Single(b => Equals(b.Content, "自动测试 / 重试"));
        fixture.FailNextWriteFeature = "brightness";
        retry.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        for (int i = 0; active != null && i < 3000; i++) await Task.Delay(10);
        check(!item.WriteVerified && item.TestReason.Contains("未通过"), "真实重试按钮测试失败后撤销可写验证并保留原因");
        retry.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        for (int i = 0; active != null && i < 3000; i++) await Task.Delay(10);
        check(item.WriteVerified && report.Package().Controls.Any(c => c.Id == "brightness"), "真实重试按钮成功后保存并重新进入控制包");
        var unknown = FeatureTestPanel(report, new() { Definition = new() { Id = "unknown", Name = "未知功能", ValueType = "status" } });
        check(Children(unknown).OfType<Button>().Where(b => Equals(b.Content, "自动测试 / 重试") || Equals(b.Content, "测试所选值")).All(b => !b.IsEnabled)
            && Children(unknown).OfType<Button>().Any(b => Equals(b.Content, "重新读取此项")), "未知写入协议说明不能试写，仍提供重新读取入口");
        var surface = new StackPanel { Width = 620, Background = System.Windows.Media.Brushes.White };
        surface.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DisplayControl.UI;component/Theme.xaml", UriKind.Relative) });
        surface.Children.Add(FeatureTestPanel(report, item)); surface.Children.Add(unknown);
        var colorDefinition = Library.Copy(DeviceControl.Standard.Single(f => f.Id == "color-preset"));
        colorDefinition.Rules = [new() { MatchAnyMaximum = true,
            Values = new() { ["code-05"] = 5, ["code-08"] = 8, ["code-0b"] = 11, ["code-0c"] = 12 },
            Labels = new() { ["code-05"] = "编码 0x05（名称未确认）", ["code-08"] = "编码 0x08（名称未确认）", ["code-0b"] = "编码 0x0B（名称未确认）", ["code-0c"] = "编码 0x0C（名称未确认）" } }];
        var colorReport = new ReaderSurvey { Device = report.Device,
            Discovery = new() { Features = [new() { Code = "0x14", Declared = "显示器声明", Values = "0x05, 0x08, 0x0B, 0x0C" }] },
            Rounds = [new() { Readings = [new() { Capability = new() { Provider = "standard", Definition = colorDefinition } }] }] };
        var colorPanel = FeatureTestPanel(colorReport, new() { Definition = colorDefinition });
        var colorChoices = Children(colorPanel).OfType<ComboBox>().Single().Items.Cast<object>().Select(o => o.GetType().GetProperty("Label")!.GetValue(o)?.ToString()).ToList();
        check(colorChoices.Count == 4 && colorChoices.Contains("6500K") && colorChoices.Contains("9300K") && colorChoices.Contains("用户自定义")
            && colorChoices.Count(l => l!.Contains("名称未确认")) == 1, "旧色温记录的真实测试下拉恢复三项名称，私有0x0C不猜名");
        surface.Children.Add(colorPanel);
        var warmSession = ReadSession;
        readSessions[device!.DevicePath] = new() { Device = report.Device, Segments = [new() { Rounds = [new() { Device = report.Device,
            Readings = [new() { Capability = new() { Definition = colorDefinition }, Result = new() { Status = "ok", Current = 11, Maximum = 12 } }] }] }] };
        var warmPanel = FeatureTestPanel(colorReport, new() { Definition = colorDefinition });
        var warmChoice = Children(warmPanel).OfType<ComboBox>().Single();
        check(Equals(warmChoice.SelectedValue, "code-0b") && Children(warmPanel).OfType<TextBlock>().Any(t => t.Text.Contains("11 / 0x0B") && t.Text.Contains("最近读到")),
            "最近读取编码11自动选中0x0B并强调同一命名行，无需换算");
        surface.Children.Add(warmPanel);
        var warmFeature = new SurveyFeature { Definition = colorDefinition };
        colorReport.Features = [warmFeature];
        colorReport.SaveOptionNames("color-preset", new Dictionary<string, string> { ["code-0b"] = "暖色" });
        var oldReading = new Reading { Capability = new() { Provider = "standard", Definition = Library.Copy(colorDefinition) }, Result = new() { Status = "ok", Current = 11, Maximum = 12 } };
        check(ReaderDisplay(oldReading, new() { Device = report.Device }, colorReport).Contains("暖色 · 11 / 0x0B"), "对比结果按最新保存名称显示并保留原编码，不使用快照旧标签");
        var rereadPanel = FeatureTestPanel(colorReport, warmFeature);
        var beforeBody = body.Children.Count; uint oldColor = fixture.Values["color-preset"]; fixture.Values["color-preset"] = 8;
        Children(rereadPanel).OfType<Button>().Single(b => Equals(b.Content, "重新读取此项")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        for (int i = 0; active != null && i < 3000; i++) await Task.Delay(10);
        check(Equals(Children(rereadPanel).OfType<ComboBox>().Single().SelectedValue, "code-08")
            && Children(rereadPanel).OfType<TextBlock>().Any(t => t.Text.Contains("8 / 0x08") && t.Text.Contains("最近读到")) && body.Children.Count == beforeBody,
            "单项重读就地移动强调行并更新选中值，不重建页面");
        fixture.Values["color-preset"] = oldColor; featurePanelReadings.Remove(report.Device.DevicePath + "|color-preset");
        Library.Delete("reader-surveys", colorReport.Id);
        if (warmSession != null) readSessions[device.DevicePath] = warmSession; else readSessions.Remove(device.DevicePath);
        var picture = new SurveyFeature { Definition = new() { Id = "display-preset", Name = "画面预设", ValueType = "enum", VcpCode = 0xDC, Writable = true,
            Rules = [new() { MatchAnyMaximum = true, Values = new() { ["code-00"] = 0, ["code-01"] = 1, ["code-02"] = 2 },
                Labels = new() { ["code-00"] = "编码 0x00（名称未确认）", ["code-01"] = "编码 0x01（名称未确认）", ["code-02"] = "编码 0x02（名称未确认）" } }] },
            ReadStatus = "读取已验证", WriteStatus = "试写及恢复已验证", CanExport = true, TestedValues = [0, 1, 2] };
        var pictureReport = new ReaderSurvey { Device = report.Device, Status = "已完成", Features = [picture] };
        var picturePanel = FeatureTestPanel(pictureReport, picture);
        var namesPanel = Children(picturePanel).OfType<Expander>().Single(e => Equals(e.Header, "给选项命名"));
        namesPanel.IsExpanded = true;
        var nameEditors = Children(namesPanel).OfType<TextBox>().ToList();
        foreach (var pair in nameEditors.Zip(new[] { "标准", "电影", "游戏" })) pair.First.Text = pair.Second;
        int priorRequests = fixture.Requests.Count;
        Children(namesPanel).OfType<Button>().Single(b => Equals(b.Content, "保存选项名称")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(nameEditors.Count == 3 && pictureReport.Package().Controls.Single().Rules.Single().Labels.Values.SequenceEqual(new[] { "标准", "电影", "游戏" })
            && Library.Get<ReaderSurvey>("reader-surveys", pictureReport.Id).Features.Single().Definition.Rules.Single().Labels["code-02"] == "游戏"
            && fixture.Requests.Count == priorRequests && picture.TestedValues.SequenceEqual(new uint[] { 0, 1, 2 }), "画面预设三档真实保存按钮落盘并导出自定名称，不发送显示器命令或更改验证范围");
        surface.Children.Add(picturePanel);
        surface.Measure(new Size(620, double.PositiveInfinity)); surface.Arrange(new Rect(0, 0, 620, surface.DesiredSize.Height)); surface.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(620, (int)Math.Ceiling(surface.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(directory, "feature-test-panel.png")); encoder.Save(output);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MonitorInspector;

namespace DisplayControl.UI;

public partial class WorkspaceWindow
{
    void AddFeatureTestColumn(DataGrid table, ReaderSurvey report, StackPanel details)
    {
        var factory = new FrameworkElementFactory(typeof(Button));
        factory.SetValue(ContentControl.ContentProperty, "测试 / 重试");
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
        factory.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler((sender, _) => {
            var context = ((Button)sender).DataContext;
            if (context?.GetType().GetProperty("Feature")?.GetValue(context) is not SurveyFeature item) return;
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
        if (values.Count > 0) { choice.ItemsSource = values; choice.SelectedIndex = 0; content.Children.Add(choice); }
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
        surface.Measure(new Size(620, double.PositiveInfinity)); surface.Arrange(new Rect(0, 0, 620, surface.DesiredSize.Height)); surface.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(620, (int)Math.Ceiling(surface.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(directory, "feature-test-panel.png")); encoder.Save(output);
    }
}

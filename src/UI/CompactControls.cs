using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using MonitorInspector;

namespace DisplayControl.UI;
public partial class WorkspaceWindow
{
    void ControlPackagePicker()
    {
        var target = Library.Copy(device!);
        var packages = Packages.List().Where(p => p.Kind == "protocol" && p.Match.Matches(target)).ToList();
        var state = Packages.State();
        string? selected = state.ControlPackageSelections.GetValueOrDefault(target.DevicePath) ?? state.VerifiedControlProfiles.GetValueOrDefault(target.DevicePath);
        var choices = packages.Select(p => new { Id = p.Id, Name = p.Name + " · " + p.Version }).Prepend(new { Id = "standard", Name = "内置标准协议" }).ToList();
        string name = choices.FirstOrDefault(p => p.Id == selected)?.Name ?? (selected == null ? "选择统一协议包" : "所选协议包已不可用");
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var options = new StackPanel { Visibility = Visibility.Collapsed };
        var select = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = selected ?? "standard", MinHeight = 36, Margin = new Thickness(0, 0, 0, 8) };
        options.Children.Add(Text("统一切换当前显示器的功能来源。未覆盖的功能不会混入控制页。", 13, "#64748C"));
        options.Children.Add(select);
        options.Children.Add(AsyncButton("使用此协议包", async token => {
            if (select.SelectedValue is not string id) return;
            if (writingDevices.Count > 0 || pendingWrites.Count > 0 || modeReloading.Count > 0) { Say("请等当前设置完成后再切换协议包。"); return; }
            Packages.SelectControlPackage(target, id); deviceSnapshots.Remove(target.DevicePath); snapshot = null;
            await RefreshValues(token); Say("已统一切换协议包，当前状态已重新读取。");
        }, true));
        options.Children.Add(Button("管理协议包", () => Navigate("packages")));
        panel.Children.Add(Button("协议包：" + name, () => options.Visibility = options.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        panel.Children.Add(options); body.Children.Add(panel);
    }
    void SetControlView(bool compact)
    {
        compactControls = compact;
        var pref = Preferences(); pref.CompactControls = compact;
        MonitorInspector.Library.Save("preferences", pref.Id, pref);
        ShowPage();
    }
    readonly HashSet<string> expandedCompactDetails = [];
    Border DenseFeatureLayout(ControlDefinition feature, StackPanel details, StackPanel editor, TextBlock current, StackPanel more, bool known, bool editorHasCurrent)
    {
        var title = (Grid)details.Children[0];
        var explanation = (Border)details.Children[1];
        details.Children.Remove(title); details.Children.Remove(explanation); details.Children.Remove(editor);
        var name = (TextBlock)title.Children[0]; var help = (Button)title.Children[1];
        title.Children.Clear();
        name.Text = feature.Id switch { "hdr-preset" => "HDR 预设", "dynamic-contrast" => "DCR", _ => feature.Name };
        name.FontSize = 13; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = feature.Name; name.Margin = new Thickness(0, 0, 8, 0);
        help.Width = 24; help.MinHeight = 28; help.Padding = new Thickness(2); help.Margin = new Thickness(6, 0, 0, 0);
        more.Visibility = expandedCompactDetails.Contains(feature.Id) ? Visibility.Visible : Visibility.Collapsed;
        more.Margin = new Thickness(0, 8, 0, 0);
        var menu = new Button { Content = "⋯", Width = 24, MinHeight = 28, Padding = new Thickness(2), Margin = new Thickness(2, 0, 0, 0), ToolTip = "更多：来源、适配说明、收藏与研究" };
        AutomationProperties.SetName(menu, feature.Name + "：更多操作与适配信息");
        menu.Click += (_, _) => { bool expand = more.Visibility != Visibility.Visible; more.Visibility = expand ? Visibility.Visible : Visibility.Collapsed; if (expand) expandedCompactDetails.Add(feature.Id); else expandedCompactDetails.Remove(feature.Id); };
        current.FontSize = 12; current.Margin = new Thickness(0, 6, 0, 0);
        if (known && editorHasCurrent) current.Visibility = Visibility.Collapsed;
        if (known && editor.Children.Count == 0) { details.Children.Remove(current); current.Margin = new Thickness(0); editor.Children.Add(current); }
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(92) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(name); Grid.SetColumn(editor, 1); row.Children.Add(editor); Grid.SetColumn(help, 2); row.Children.Add(help); Grid.SetColumn(menu, 3); row.Children.Add(menu);
        var content = new StackPanel(); content.Children.Add(row); content.Children.Add(explanation); content.Children.Add(details); content.Children.Add(more);
        explanation.Margin = new Thickness(0, 8, 0, 4);
        var card = Card(content); card.Padding = new Thickness(10, 8, 10, 8); card.Margin = new Thickness(0);
        return card;
    }
}

// Row-major order stays stable; each pair expands only as much as its contents require.
internal sealed class CompactFeaturePanel : Panel
{
    const double Gap = 8;
    int columns = 1;
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 680 : availableSize.Width;
        columns = width >= 680 ? 2 : 1;
        double cellWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns), height = 0;
        for (int i = 0; i < InternalChildren.Count; i += columns) {
            double rowHeight = 0;
            for (int col = 0; col < columns && i + col < InternalChildren.Count; col++) { var child = InternalChildren[i + col]; child.Measure(new Size(cellWidth, double.PositiveInfinity)); rowHeight = Math.Max(rowHeight, child.DesiredSize.Height); }
            height += rowHeight + (i == 0 ? 0 : Gap);
        }
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns), top = 0;
        for (int i = 0; i < InternalChildren.Count; i += columns) {
            double height = 0;
            for (int col = 0; col < columns && i + col < InternalChildren.Count; col++) height = Math.Max(height, InternalChildren[i + col].DesiredSize.Height);
            for (int col = 0; col < columns && i + col < InternalChildren.Count; col++) InternalChildren[i + col].Arrange(new Rect(col * (width + Gap), top, width, height));
            top += height + Gap;
        }
        return finalSize;
    }
}

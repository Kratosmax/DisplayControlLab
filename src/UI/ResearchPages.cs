using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MonitorInspector;

namespace DisplayControl.UI;
public partial class WorkspaceWindow
{
    void RenderDifferences(StackPanel target, List<Difference> differences)
    {
        target.Children.Clear(); var p = Section("变化证据", differences.Count == 0 ? "采集范围内没有观察到变化；这不证明所有私有状态都没有变化。" : $"发现 {differences.Count} 项差异。关联不等于因果，未知语义不自动命名。");
        var table = Table(); table.Columns.Add(Column("功能", "Feature", .8)); table.Columns.Add(Column("之前", "Before", 1)); table.Columns.Add(Column("之后", "After", 1)); table.Columns.Add(Column("证据与边界", "Evidence", 2)); table.ItemsSource = differences; p.Children.Add(table); target.Children.Add(Card(p));
    }
    void PresetsPage()
    {
        var save = Section("保存当前可控状态", "只包含读取成功且可写的项目。模式保存稳定选项 id，不把 HDR 游戏固定写成 3。"); var name = new TextBox { Text = selectedPreset?.Name ?? "办公", MinHeight = 36, Margin = new Thickness(0, 0, 0, 12) }; save.Children.Add(name); var checks = new WrapPanel();
        foreach (var r in snapshot?.Readings.Where(r => r.Result.Status == "ok" && r.Capability.Definition.Writable) ?? []) { var cb = new CheckBox { Content = r.Capability.Definition.Name + "：" + r.Display(snapshot!.Device.WindowsHdrEnabled), Tag = r, IsChecked = true, Margin = new Thickness(0, 0, 18, 12) }; checks.Children.Add(cb); }
        save.Children.Add(checks); save.Children.Add(Button("保存为新方案", () => { if (snapshot == null || device == null) throw new InvalidOperationException("先检查设备"); var preset = new Preset { Name = name.Text, DevicePath = device.DevicePath, Items = checks.Children.Cast<CheckBox>().Where(x => x.IsChecked == true).Select(x => { var r = (Reading)x.Tag; return new PresetItem { FeatureId = r.Capability.Definition.Id, Value = r.Semantic(snapshot.Device.WindowsHdrEnabled), Provider = r.Capability.Provider, Hash = r.Capability.Hash }; }).ToList() }; if (preset.Items.Count == 0) throw new InvalidOperationException("至少选择一项"); Library.Save("presets", preset.Id, preset); selectedPreset = preset; ShowPage(); Say("方案已保存"); }, true)); body.Children.Add(Card(save));
        var p = Section("已保存方案", "预检失败不发送；执行中一项失败就停止后续。可选项需要明确勾选后才跳过。"); var list = new ComboBox { ItemsSource = Library.All<Preset>("presets"), SelectedValuePath = "Id", SelectedValue = selectedPreset?.Id, MinHeight = 36, Margin = new Thickness(0, 0, 0, 12) }; list.SelectionChanged += (_, _) => { selectedPreset = list.SelectedItem as Preset; presetPlan = null; ShowPage(); }; p.Children.Add(list);
        var tools = Actions(); tools.Children.Add(AsyncButton("预检当前差异", async t => { if (selectedPreset == null || device == null) throw new InvalidOperationException("选择方案和设备"); var scope = selectedPreset.Items.Select(i => i.FeatureId).Concat((snapshot?.Readings ?? []).Where(r => selectedPreset.Items.Any(i => i.FeatureId == r.Capability.Definition.Id)).SelectMany(r => r.Capability.Definition.WritableWhen.Select(c => c.FeatureId))).Distinct().ToList(); StoreSnapshot(await workspace.Capture(device, t, scope: scope)); presetPlan = DisplayWorkspace.Preflight(selectedPreset, snapshot!); presetPlanId = selectedPreset.Id; ShowPage(); Say(DisplayWorkspace.PreflightFailure(presetPlan) ?? "预检通过，可应用"); }, true));
        tools.Children.Add(AsyncButton("应用已预检方案", async t => { if (selectedPreset == null || presetPlan == null || presetPlanId != selectedPreset.Id || snapshot == null) throw new InvalidOperationException("先预检方案"); if (DisplayWorkspace.PreflightFailure(presetPlan) is { } failure) throw new InvalidOperationException(failure); var op = await workspace.Apply(selectedPreset.Name, snapshot, presetPlan, t); StoreSnapshot(op.After); ownOperations.Add(op.Id); presetPlan = null; Navigate("history"); Say("方案结束；已确认/失败项目见操作详情"); }));
        tools.Children.Add(Button("重命名", () => { if (selectedPreset == null) return; var value = Prompt("重命名方案", "方案名", selectedPreset.Name); if (value != null) { selectedPreset.Name = value; Library.Save("presets", selectedPreset.Id, selectedPreset); ShowPage(); } }));
        tools.Children.Add(Button("复制", () => { if (selectedPreset == null) return; var copy = Library.Copy(selectedPreset); copy.Id = Library.NewId(); copy.Name += " 副本"; Library.Save("presets", copy.Id, copy); selectedPreset = copy; ShowPage(); })); tools.Children.Add(Button("导出", () => { if (selectedPreset != null) ExportJson(selectedPreset, "preset.json"); })); tools.Children.Add(Button("导入", () => { string? path = Open("方案 JSON|*.json"); if (path == null) return; if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("方案过大"); var preset = JsonSerializer.Deserialize<Preset>(File.ReadAllText(path), Packages.Json) ?? throw new InvalidDataException("空方案"); preset.Id = Library.NewId(); Library.Save("presets", preset.Id, preset); selectedPreset = preset; ShowPage(); })); tools.Children.Add(Button("删除", () => { if (selectedPreset != null && Confirm("删除方案“" + selectedPreset.Name + "”？")) { Library.Delete("presets", selectedPreset.Id); selectedPreset = null; presetPlan = null; ShowPage(); } })); p.Children.Add(tools);
        if (selectedPreset != null) {
            var editing = new Expander { Header = "编辑方案项目（目标值 / 必需项）", Margin = new Thickness(0, 0, 0, 14) }; var rows = new StackPanel();
            foreach (var item in selectedPreset.Items) { var row = Row(); var label = Text(item.FeatureId, 13); label.Width = 170; var input = new TextBox { Text = item.Value, Width = 130, MinHeight = 32, Margin = new Thickness(0, 0, 12, 8) }; var required = new CheckBox { Content = "必需", IsChecked = item.Required, VerticalAlignment = VerticalAlignment.Center }; input.TextChanged += (_, _) => { item.Value = input.Text; presetPlan = null; }; required.Click += (_, _) => { item.Required = required.IsChecked == true; presetPlan = null; }; row.Children.Add(label); row.Children.Add(input); row.Children.Add(required); rows.Children.Add(row); }
            rows.Children.Add(Button("保存编辑", () => { Library.Save("presets", selectedPreset.Id, selectedPreset); presetPlan = null; ShowPage(); Say("已保存，应用前重新预检"); })); editing.Content = rows; p.Children.Add(editing);
        }
        if (presetPlan != null) foreach (var row in presetPlan) p.Children.Add(Text(row.ToString(), 14, row.Problem.Length > 0 ? "#9A6612" : "#27795E")); body.Children.Add(Card(p));
    }
    void HistoryPage()
    {
        var p = Section("操作与可核查恢复", "写入前先落盘意图；无法确认的结果先核查，不自动重发。恢复只处理有原值且已确认的范围。"); var history = Library.All<Operation>("operations").Where(o => device == null || o.Before.Device.DevicePath == device.DevicePath).ToList(); var list = new ListBox { ItemsSource = history, MaxHeight = 230, MinHeight = 80 }; var details = new StackPanel();
        void ShowOperation(Operation op) {
            details.Children.Clear(); details.Children.Add(Text(op.Name, 20)); details.Children.Add(Text("设备：" + op.Before.Device.Name + " · " + op.Time.ToString("G"), 13, "#64748C"));
            for (int i = 0; i < op.Requests.Count; i++) { var request = op.Requests[i]; var result = op.Results.SingleOrDefault(r => r.RequestId == request.RequestId); details.Children.Add(Text($"{request.ControlId} → {request.Value}　{result?.Message ?? "发送意图已记录，最终状态未知"}", 14, result?.Confirmed == true ? "#27795E" : "#9A6612")); details.Children.Add(Text($"来源 {request.PackageId ?? "standard"} · 已发送 {result?.Sent} · 已确认 {result?.Confirmed} · id {request.RequestId}", 12, "#64748C")); }
            var actions = Actions(); actions.Children.Add(AsyncButton("重新核查当前值", t => RefreshValues(t))); actions.Children.Add(AsyncButton("核查并恢复已修改项", t => Restore(op, t))); actions.Children.Add(Button("比较操作前后 ↗", () => { var container = new StackPanel(); RenderDifferences(container, Comparison.Between(op.Before, op.After)); details.Children.Add(container); })); actions.Children.Add(Button("导出操作证据", () => ExportJson(op, "operation.json"))); details.Children.Add(actions);
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is Operation op) ShowOperation(op); }; p.Children.Add(list); body.Children.Add(Card(p)); body.Children.Add(Card(details)); if (history.Count > 0) list.SelectedIndex = 0; else details.Children.Add(Text("尚无操作。读取设备不会产生可恢复的写操作。"));
        var systemHistory = Section("系统 HDR 记录", "系统颜色模式由标题栏按钮切换；其黑屏和联动状态独立记录。");
        foreach (var record in Library.All<JsonElement>("system-operations").Take(12)) {
            if (!record.TryGetProperty("request", out var request) || !record.TryGetProperty("result", out var result)) continue;
            string name = request.GetProperty("device").GetProperty("name").GetString() ?? "显示器";
            if (device != null && request.GetProperty("device").GetProperty("devicePath").GetString() != device.DevicePath) continue;
            systemHistory.Children.Add(Text($"{name} · {(request.GetProperty("value").GetString() == "on" ? "开启" : "关闭")}系统 HDR · {result.GetProperty("message").GetString()}",13));
        }
        body.Children.Add(Card(systemHistory));
    }
    string? Open(string filter) { var dialog = new OpenFileDialog { Filter = filter }; return dialog.ShowDialog(this) == true ? dialog.FileName : null; }
    string? Save(string filter, string name) { var dialog = new SaveFileDialog { Filter = filter, FileName = name }; return dialog.ShowDialog(this) == true ? dialog.FileName : null; }
    void ExportJson<T>(T value, string name) { string? file = Save("JSON|*.json", name); if (file != null) { File.WriteAllText(file, JsonSerializer.Serialize(value, Packages.Json)); Say("已导出：" + file); } }
}

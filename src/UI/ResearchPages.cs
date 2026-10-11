using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MonitorInspector;

namespace DisplayControl.UI;
public partial class WorkspaceWindow
{
    void SnapshotsPage()
    {
        var p = Section("把两次设备检查放在一起", "同时比较数值、范围、失败状态、提供者和系统 HDR；失败不会被当成 0。"); var a = new ComboBox { ItemsSource = Library.All<Snapshot>("snapshots"), MinHeight = 36, Margin = new Thickness(0, 5, 0, 12) }; var b = new ComboBox { ItemsSource = Library.All<Snapshot>("snapshots"), MinHeight = 36, Margin = new Thickness(0, 5, 0, 12) }; p.Children.Add(Text("之前")); p.Children.Add(a); p.Children.Add(Text("之后")); p.Children.Add(b);
        var result = new StackPanel(); var actions = Actions(); actions.Children.Add(Button("比较", () => { if (a.SelectedItem is not Snapshot first || b.SelectedItem is not Snapshot second) throw new InvalidOperationException("选择两个快照"); if (first.Device.DevicePath != second.Device.DevicePath) throw new InvalidOperationException("不能把不同显示器当作同一设备前后对比"); RenderDifferences(result, Comparison.Between(first, second)); }));
        actions.Children.Add(Button("导入旧记录（只读）", () => {
            var file = Open("旧记录 JSON|*.json"); if (file == null) return;
            if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException("记录过大");
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var raw = doc.RootElement.TryGetProperty("raw", out var property) ? property.GetRawText() : doc.RootElement.GetRawText();
            var monitor = JsonSerializer.Deserialize<MonitorData>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("旧记录无效");
            if (monitor.Ddc == null) throw new InvalidDataException("旧记录未包含 Ddc 样本；原文件未修改");
            var s = new Snapshot { Name = "旧记录（只读导入）", Device = monitor, Time = monitor.Ddc.ReadAt, Readings = monitor.Ddc.Features.Select(f => new Reading { Capability = new() { Definition = new() { Id = f.Code, Name = f.Name }, Provider = "legacy-observation" }, Result = new() { Status = f.Current.HasValue ? "ok" : "error", Current = f.Current, Maximum = f.Maximum, Message = f.Status, Raw = f.Values }, Time = f.ReadAt }).ToList() };
            Library.Save("snapshots", s.Id, s); ShowPage(); Say("旧记录已导入为只读快照，不作为可写协议");
        })); actions.Children.Add(Button("导出所选快照", () => { if (a.SelectedItem is Snapshot selected) ExportJson(selected, "snapshot.json"); })); actions.Children.Add(Button("删除所选快照", () => { if (a.SelectedItem is Snapshot selected && Confirm("删除快照“" + selected.Name + "”？")) { Library.Delete("snapshots", selected.Id); ShowPage(); } })); p.Children.Add(actions); body.Children.Add(Card(p)); body.Children.Add(result);
    }
    void RenderDifferences(StackPanel target, List<Difference> differences)
    {
        target.Children.Clear(); var p = Section("变化证据", differences.Count == 0 ? "采集范围内没有观察到变化；这不证明所有私有状态都没有变化。" : $"发现 {differences.Count} 项差异。关联不等于因果，未知语义不自动命名。");
        var table = Table(); table.Columns.Add(Column("功能", "Feature", .8)); table.Columns.Add(Column("之前", "Before", 1)); table.Columns.Add(Column("之后", "After", 1)); table.Columns.Add(Column("证据与边界", "Evidence", 2)); table.ItemsSource = differences; p.Children.Add(table); target.Children.Add(Card(p));
    }
    void ExperimentsPage()
    {
        var intro = Section("分段记录菜单切换", "采集时保持菜单不变。每段采集两轮后自动暂停，再手动切换菜单；不会在切换途中误采基线。");
        var history = new ComboBox { ItemsSource = Library.All<Experiment>("experiments"), MinHeight = 36, SelectedValuePath = "Id", SelectedValue = experiment?.Id, Margin = new Thickness(0, 0, 0, 12) };
        history.SelectionChanged += (_, _) => { if (history.SelectedItem is Experiment selected) { experiment = selected; ShowPage(); } }; intro.Children.Add(history);
        var question = new TextBox { Text = experiment?.Question ?? (focusedFeature == null ? "OSD 菜单切换前后有哪些变化？" : "检查 " + focusedFeature + " 的菜单切换"), MinHeight = 36, Margin = new Thickness(0, 0, 0, 12) }; intro.Children.Add(question);
        var scope = new WrapPanel(); var selectedScope = new HashSet<string>(experiment?.FeatureIds ?? snapshot?.Readings.Select(r => r.Capability.Definition.Id).ToList() ?? []);
        foreach (var c in device == null ? [] : workspace.Backend.Capabilities(device)) { var box = new CheckBox { Content = c.Definition.Name, IsChecked = selectedScope.Count == 0 || selectedScope.Contains(c.Definition.Id), Tag = c.Definition.Id, Margin = new Thickness(0, 0, 16, 10) }; scope.Children.Add(box); }
        intro.Children.Add(new Expander { Header = "采样范围（已知功能）", Content = scope, Margin = new Thickness(0, 0, 0, 12) });
        var expanded = new CheckBox { Content = "额外记录扩展标准 GET（每轮最长 180 秒；不能抓取未知私有报文）", IsChecked = experiment?.Expanded == true, Margin = new Thickness(0, 0, 0, 12) }; intro.Children.Add(expanded);
        var actions = Actions(); actions.Children.Add(Button("新建实验", () => { if (device == null) return; experiment = new() { Question = question.Text, Device = Library.Copy(device), Expanded = expanded.IsChecked == true, FeatureIds = scope.Children.Cast<CheckBox>().Where(x => x.IsChecked == true).Select(x => (string)x.Tag).ToList() }; if (experiment.FeatureIds.Count == 0) throw new InvalidOperationException("至少选择一项采样功能"); Library.Save("experiments", experiment.Id, experiment); ShowPage(); Say("实验已建立，开始采集基线 A。"); }));
        actions.Children.Add(Button("保存问题与范围", () => { if (experiment == null) return; if (experiment.Segments.Count > 0) throw new InvalidOperationException("已有样本的实验不更改采集范围；可另建实验"); experiment.Question = question.Text; experiment.FeatureIds = scope.Children.Cast<CheckBox>().Where(x => x.IsChecked == true).Select(x => (string)x.Tag).ToList(); Library.Save("experiments", experiment.Id, experiment); Say("已保存"); })); intro.Children.Add(actions); body.Children.Add(Card(intro));
        if (experiment == null) return;
        expanded.IsEnabled = experiment.Segments.Count == 0;
        expanded.Click += (_, _) => { experiment.Expanded = expanded.IsChecked == true; Library.Save("experiments", experiment.Id, experiment); };
        var sampling = Section("先稳定采集，再切换菜单", $"实验设备：{experiment.Device.Name}。样本固定设备与每轮包内容，不随侧栏选择更换。");
        sampling.Children.Add(Text("① 采集 A　→　② 暂停后操作 OSD　→　③ 采集 B　→　④ 切回 A 再采集", 15, "#3D5DD8"));
        var name = new TextBox { Text = experiment.Segments.Count == 0 ? "基线 A" : experiment.Segments.Count == 1 ? "切换后 B" : "恢复 A", MinHeight = 36, Margin = new Thickness(0, 8, 0, 12) }; sampling.Children.Add(name); var controls = Actions();
        controls.Children.Add(AsyncButton("开始采集此状态（2 轮）", t => CaptureSegment(name.Text, t), true)); controls.Children.Add(AsyncButton("连续采集（直到取消）", t => CaptureSegment(name.Text, t, true))); controls.Children.Add(Button("准备切换 / 暂停", () => { active?.Cancel(); Say("等待当前请求取消并落盘；任务结束后再操作 OSD。之后点击“开始采集此状态”。"); })); controls.Children.Add(Button("导出完整证据 ZIP", () => ExportExperiment(false))); controls.Children.Add(Button("导出去标识证据", () => ExportExperiment(true))); sampling.Children.Add(controls); body.Children.Add(Card(sampling));
        var segments = new ListBox { ItemsSource = experiment.Segments, MinHeight = 90, MaxHeight = 200, Margin = new Thickness(0, 0, 0, 12) }; var correction = Section("采集段与标记修订", "名称只是用户标记。过渡段不参与稳定段比较；原始样本保留。"); correction.Children.Add(segments); var tools = Actions(); tools.Children.Add(Button("修改段名称", () => { if (segments.SelectedItem is ExperimentSegment s) { string? value = Prompt("修订标记", "段名称", s.Name); if (value != null) { experiment.MarkerRevisions.Add($"{DateTimeOffset.Now:O} 段名称 {s.Name} → {value}"); s.Name = value; Library.Save("experiments", experiment.Id, experiment); ShowPage(); } } })); tools.Children.Add(Button("标记/取消过渡段", () => { if (segments.SelectedItem is ExperimentSegment s) { experiment.MarkerRevisions.Add($"{DateTimeOffset.Now:O} {s.Name} 过渡标记 {!s.Transition}"); s.Transition = !s.Transition; Library.Save("experiments", experiment.Id, experiment); ShowPage(); } })); correction.Children.Add(tools);
        var rounds = new ListBox { MinHeight = 60, MaxHeight = 160, DisplayMemberPath = "Name" }; segments.SelectionChanged += (_, _) => { rounds.ItemsSource = (segments.SelectedItem as ExperimentSegment)?.Rounds; }; correction.Children.Add(new Expander { Header = "修订段边界（保留样本与修改记录）", Content = rounds });
        var boundaries = Actions();
        void MoveRound(int direction) { if (segments.SelectedItem is not ExperimentSegment source || rounds.SelectedItem is not Snapshot round) throw new InvalidOperationException("先选中段与轮次"); int index = experiment.Segments.IndexOf(source) + direction; if (index < 0 || index >= experiment.Segments.Count) throw new InvalidOperationException("没有相邻段"); var destination = experiment.Segments[index]; source.Rounds.Remove(round); destination.Rounds.Add(round); destination.Rounds = destination.Rounds.OrderBy(r => r.Time).ToList(); experiment.MarkerRevisions.Add($"{DateTimeOffset.Now:O} 轮次 {round.Id}：{source.Name} → {destination.Name}"); Library.Save("experiments", experiment.Id, experiment); ShowPage(); }
        boundaries.Children.Add(Button("所选轮移至前段", () => MoveRound(-1))); boundaries.Children.Add(Button("所选轮移至后段", () => MoveRound(1))); correction.Children.Add(boundaries);
        var compareA = new ComboBox { ItemsSource = experiment.Segments, MinHeight = 36, Margin = new Thickness(0, 5, 0, 10) }; var compareB = new ComboBox { ItemsSource = experiment.Segments, MinHeight = 36, Margin = new Thickness(0, 5, 0, 10) }; correction.Children.Add(compareA); correction.Children.Add(compareB); var result = new StackPanel(); correction.Children.Add(Button("比较稳定段", () => { if (compareA.SelectedIndex < 0 || compareB.SelectedIndex < 0) throw new InvalidOperationException("请选择两个段"); RenderDifferences(result, experiment.Compare(compareA.SelectedIndex, compareB.SelectedIndex)); })); body.Children.Add(Card(correction)); body.Children.Add(result);
        var firstTime = experiment.Segments.SelectMany(s => s.Rounds).Select(s => s.Time).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
        var operations = Library.All<Operation>("operations").Where(o => o.Before.Device.DevicePath == experiment.Device.DevicePath && o.Time >= firstTime).ToList();
        var timeline = Section("同设备的软件操作", "根据持久化操作 id 和时间关联，避免误归因给 OSD。每次重开会话重新读取。"); foreach (var op in operations) timeline.Children.Add(Text(op.ToString() + " · id " + op.Id, 12, "#64748C")); if (operations.Count == 0) timeline.Children.Add(Text("采样开始后尚无已记录的软件操作。", 13, "#64748C")); body.Children.Add(Card(timeline));
    }
    async Task CaptureSegment(string name, CancellationToken t, bool continuous = false)
    {
        if (experiment == null) throw new InvalidOperationException("先新建实验");
        if (experiment.Expanded && !simulation && !Confirm("扩展 GET 每轮最长 180 秒，仅做读取。会跳过命令类项目；不保证能读到私有状态。继续？")) return;
        var segment = new ExperimentSegment { Name = name }; experiment.Segments.Add(segment); Library.Save("experiments", experiment.Id, experiment);
        try {
            for (int i = 0; continuous || i < 2; i++) {
                var partial = new Snapshot { Device = Library.Copy(experiment.Device), Name = $"第 {i + 1} 轮 · {DateTime.Now:HH:mm:ss}", Complete = false }; segment.Rounds.Add(partial);
                Say($"采集 {name} · 第 {i + 1} 轮，请保持菜单状态");
                var sample = await workspace.Capture(experiment.Device, t, r => { partial.Readings.Add(r); Library.Save("experiments", experiment.Id, experiment); Say($"{name} 第 {i + 1} 轮 · {r.Capability.Definition.Name}"); }, experiment.FeatureIds);
                if (experiment.Expanded && !simulation) {
                    var ddc = await ScanExpanded(sample.Device, t);
                    foreach (var f in ddc.Features) sample.Readings.Add(new Reading { Capability = new() { Definition = new() { Id = "expanded." + f.Code, Name = f.Name, ValueType = "status" }, Provider = "expanded-standard-get" }, Result = new() { Current = f.Current, Maximum = f.Maximum, Status = f.Current.HasValue ? "ok" : "error", Message = f.Status, Raw = f.Values }, Time = f.ReadAt });
                }
                sample.Name = partial.Name; sample.Id = partial.Id; segment.Rounds[segment.Rounds.Count - 1] = sample; Library.Save("experiments", experiment.Id, experiment);
            }
        } finally { Library.Save("experiments", experiment.Id, experiment); ShowPage(); }
        Say($"{name} 已采到 {segment.Rounds.Count(r => r.Complete)} 完整轮，已暂停。现在可以切换 OSD，再命名下一段。");
    }
    void ExportExperiment(bool anonymize)
    {
        if (experiment == null) return; string? file = Save("实验 ZIP|*.zip", "display-experiment.zip"); if (file == null) return; if (File.Exists(file)) File.Delete(file); DisplayWorkspace.ExportExperiment(experiment, file, anonymize); Say("证据已导出（含原始样本，不生成未知协议）");
    }
    void PresetsPage()
    {
        var save = Section("保存当前可控状态", "只包含读取成功且可写的项目。模式保存稳定选项 id，不把 HDR 游戏固定写成 3。"); var name = new TextBox { Text = selectedPreset?.Name ?? "办公", MinHeight = 36, Margin = new Thickness(0, 0, 0, 12) }; save.Children.Add(name); var checks = new WrapPanel();
        foreach (var r in snapshot?.Readings.Where(r => r.Result.Status == "ok" && r.Capability.Definition.Writable) ?? []) { var cb = new CheckBox { Content = r.Capability.Definition.Name + "：" + r.Display(snapshot!.Device.WindowsHdrEnabled), Tag = r, IsChecked = true, Margin = new Thickness(0, 0, 18, 12) }; checks.Children.Add(cb); }
        save.Children.Add(checks); save.Children.Add(Button("保存为新方案", () => { if (snapshot == null || device == null) throw new InvalidOperationException("先检查设备"); var preset = new Preset { Name = name.Text, DevicePath = device.DevicePath, Items = checks.Children.Cast<CheckBox>().Where(x => x.IsChecked == true).Select(x => { var r = (Reading)x.Tag; return new PresetItem { FeatureId = r.Capability.Definition.Id, Value = r.Semantic(snapshot.Device.WindowsHdrEnabled), Provider = r.Capability.Provider, Hash = r.Capability.Hash }; }).ToList() }; if (preset.Items.Count == 0) throw new InvalidOperationException("至少选择一项"); Library.Save("presets", preset.Id, preset); selectedPreset = preset; ShowPage(); Say("方案已保存"); }, true)); body.Children.Add(Card(save));
        var p = Section("已保存方案", "预检失败不发送；执行中一项失败就停止后续。可选项需要明确勾选后才跳过。"); var list = new ComboBox { ItemsSource = Library.All<Preset>("presets"), SelectedValuePath = "Id", SelectedValue = selectedPreset?.Id, MinHeight = 36, Margin = new Thickness(0, 0, 0, 12) }; list.SelectionChanged += (_, _) => { selectedPreset = list.SelectedItem as Preset; presetPlan = null; ShowPage(); }; p.Children.Add(list);
        var tools = Actions(); tools.Children.Add(AsyncButton("预检当前差异", async t => { if (selectedPreset == null || device == null) throw new InvalidOperationException("选择方案和设备"); var scope = selectedPreset.Items.Select(i => i.FeatureId).Concat((snapshot?.Readings ?? []).Where(r => selectedPreset.Items.Any(i => i.FeatureId == r.Capability.Definition.Id)).SelectMany(r => r.Capability.Definition.WritableWhen.Select(c => c.FeatureId))).Distinct().ToList(); snapshot = DisplayWorkspace.Merge(snapshot, await workspace.Capture(device, t, scope: scope)); deviceSnapshots[device.DevicePath] = snapshot; presetPlan = DisplayWorkspace.Preflight(selectedPreset, snapshot); presetPlanId = selectedPreset.Id; ShowPage(); Say(DisplayWorkspace.PreflightFailure(presetPlan) ?? "预检通过，可应用"); }, true));
        tools.Children.Add(AsyncButton("应用已预检方案", async t => { if (selectedPreset == null || presetPlan == null || presetPlanId != selectedPreset.Id || snapshot == null) throw new InvalidOperationException("先预检方案"); if (DisplayWorkspace.PreflightFailure(presetPlan) is { } failure) throw new InvalidOperationException(failure); var op = await workspace.Apply(selectedPreset.Name, snapshot, presetPlan, t); snapshot = op.After; presetPlan = null; Navigate("history"); Say("方案结束；已确认/失败项目见操作详情"); }));
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

using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MonitorInspector;

namespace DisplayControl.UI;
public partial class WorkspaceWindow
{
    string? draftFeature;
    void PackagesPage()
    {
        var intro = Section("按需安装设备支持", reader ? "标准功能内置。这里可以制作、编辑、验证和分享私有协议或代码插件。" : "控制器导入和使用适配包；协议制作、编辑与验证在读取器完成。"); var tools = Actions();
        tools.Children.Add(Button("导入适配包", () => {
            string? file = Open("适配包 JSON / ZIP|*.json;*.zip"); if (file == null) return;
            AdapterPackage preview;
            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) preview = Packages.Parse(File.ReadAllText(file));
            else { using var zip = ZipFile.OpenRead(file); var entry = zip.GetEntry("adapter.json") ?? throw new InvalidDataException("包根目录缺少 adapter.json"); if (entry.Length > 1024 * 1024) throw new InvalidDataException("清单过大"); using var input = new StreamReader(entry.Open()); preview = Packages.Parse(input.ReadToEnd()); }
            var prior = Packages.List().SingleOrDefault(p => p.Id == preview.Id);
            string message = $"{preview.Name} · {preview.Version}\n适用：{preview.Match.Manufacturer} / {preview.Match.ProductCode}\n新增功能：{string.Join("、", preview.Controls.Select(f => f.Name))}\n依赖：{string.Join("、", preview.Dependencies)}\n" + (prior == null ? "安装后不自动执行，需要应用到设备。" : $"将替换已安装版本 {prior.Version}，插件需重新信任；已有方案可能需要重新保存。");
            if (preview.OverridesStandard.Count > 0) message += "\n默认型号定义：" + string.Join("、", preview.OverridesStandard) + "；已有明确来源选择保留。";
            if (!Confirm(message)) return; var installed = Packages.Install(file, prior != null); ShowPage(); Say("已安装 " + installed.Name + "，未自动应用到设备");
        }, true)); if (reader) tools.Children.Add(Button("创建协议", () => { NewDraft(); Navigate("workbench"); })); if (reader) tools.Children.Add(Button("生成插件项目模板", () => { string? file = Save("插件模板 ZIP|*.zip", "plugin-template.zip"); if (file == null) return; string source = Path.Combine(AppContext.BaseDirectory, "templates", "plugin-template.zip"); if (!File.Exists(source)) throw new IOException("源码运行环境缺少模板，请使用预览包或 examples/plugin-template"); File.Copy(source, file, true); Say("模板已导出；按 README 实现独立插件程序"); })); intro.Children.Add(tools); body.Children.Add(Card(intro));
        var installedPackages = Packages.List(); if (installedPackages.Count == 0) body.Children.Add(Card(Text("尚未安装私有适配包。标准亮度与对比度仍可用。")));
        foreach (var package in installedPackages) {
            bool attached = device != null && Packages.ForDevice(device).Any(p => p.Id == package.Id); bool disabled = Packages.State().Disabled.Contains(package.Id);
            var p = Section(package.Name, $"{package.Version} · {package.Author} · {(package.Kind == "plugin" ? "独立代码插件" : "声明式协议")} · {(disabled ? "已停用" : "已启用")}\n适用 {package.Match.Manufacturer} / {package.Match.ProductCode} · {(device == null ? "未选择设备" : package.Match.Matches(device) ? "匹配当前设备" : "不匹配当前设备")}");
            p.Children.Add(Text("依赖：" + (package.Dependencies.Count == 0 ? "无额外声明" : string.Join("、", package.Dependencies)), 13, "#64748C")); p.Children.Add(Text("功能：" + string.Join("、", package.Controls.Select(f => f.Name)), 13));
            if (package.Controls.Any(f => Packages.BackendFor(package, f) == "amd-ddc") && !File.Exists(Path.Combine(Environment.SystemDirectory, "atiadlxx.dll"))) p.Children.Add(Text("未找到 AMD ADL：此通道的私有功能不可用。标准项仍可读取；写入仍需满足模式安全条件。", 13, "#9A6612"));
            if (package.Kind == "plugin") p.Children.Add(Text(Packages.IsTrusted(package) ? "当前代码内容已获信任" : "代码未经信任或升级内容已变，应用到设备时需确认。", 13, "#9A6612"));
            var actions = Actions(); actions.Children.Add(Button(attached ? "从当前设备移除" : "应用到当前设备", () => {
                if (device == null) throw new InvalidOperationException("先选择设备");
                if (attached) Packages.Detach(device, package.Id);
                else { if (package.Kind == "plugin" && !Confirm($"{package.Name}\n这是可执行代码，拥有当前用户权限。\n作者 {package.Author}\n代码 SHA256：{Packages.ContentHash(package)}\n只信任当前内容，是否继续？")) return; Packages.Attach(device, package, package.Kind == "plugin"); }
                snapshot = null; ShowPage(); _ = Run(t => RefreshValues(t));
            }, !attached)); actions.Children.Add(Button(disabled ? "启用" : "停用", () => { Packages.Enable(package.Id, disabled); snapshot = null; ShowPage(); }));
            if (reader) actions.Children.Add(Button("编辑用户副本", () => { editingSource = package; draft = Library.Copy(package); draft.Id += ".user-" + Guid.NewGuid().ToString("N")[..6]; draft.Name += " 用户副本"; draftFeature = draft.Controls[0].Id; SaveDraft(); Navigate("workbench"); }));
            if (reader) actions.Children.Add(Button("导出完整包", () => { string? file = Save("适配包 ZIP|*.zip", package.Id + ".zip"); if (file == null) return; if (File.Exists(file)) File.Delete(file); Packages.Export(package, file); Say("完整适配包已导出"); })); actions.Children.Add(Button("卸载", () => { if (Confirm("卸载 " + package.Name + "？设备绑定会失效，实验与历史保留。")) { Packages.Uninstall(package); snapshot = null; ShowPage(); } })); if (reader && package.Kind == "plugin") actions.Children.Add(AsyncButton("检查运行依赖与能力", async t => { if (device == null) throw new InvalidOperationException("先选择设备"); var result = await workspace.Backend.Execute(new ControlRequest { Device = device, PackageId = package.Id, ExpectedHash = Packages.ContentHash(package), Action = "probe" }, t); string id = Library.NewId(); Library.Save("plugin-diagnostics", id, result); Say(result.Message + (result.Capabilities == null ? "" : " · 运行时功能 " + result.Capabilities.Count + " 项；描述不会自动覆盖已安装清单")); })); p.Children.Add(actions); body.Children.Add(Card(p));
        }
        foreach (string error in Packages.Broken()) body.Children.Add(Card(Text("损坏的适配包已隔离，不影响其他功能：" + error, 13, "#9A6612")));
    }
    void NewDraft()
    {
        editingSource = null; draft = new() { Id = "user.protocol-" + Guid.NewGuid().ToString("N")[..8], Name = "我的显示器协议", Author = Environment.UserName, Match = new() { Manufacturer = device?.Edid.Manufacturer ?? "TST", ProductCode = device?.Edid.Product ?? "0x0001" }, Controls = [new() { Id = "brightness", Name = "亮度", Writable = true, VcpCode = 16 }] }; draftFeature = "brightness"; SaveDraft();
    }
    readonly string draftId = "00000000000000000000000000000002";
    void SaveDraft() { if (draft != null) Library.Save("drafts", draftId, draft); }
    void WorkbenchPage()
    {
        draft ??= Library.All<AdapterPackage>("drafts").FirstOrDefault();
        var start = Section("制作可分享的适配包", "已知指令 → 表单与条件映射 → 回包回放 → 主动测试 → 安装或导出。没有已知指令时只记录线索，不推测报文。"); var tools = Actions(); tools.Children.Add(Button("新建标准协议", () => { NewDraft(); ShowPage(); })); tools.Children.Add(Button("新建私有协议", () => { NewDraft(); draft!.Backend = "amd-ddc"; draft.Controls[0].Id = "vendor.feature"; draft.Controls[0].Name = "待验证私有功能"; draft.Controls[0].Writable = false; draft.Controls[0].VcpCode = null; draft.Controls[0].Raw = new(); SaveDraft(); ShowPage(); }));
        tools.Children.Add(Button("打开协议定义", () => { string? file = Open("协议 JSON|*.json"); if (file == null) return; draft = Packages.Parse(File.ReadAllText(file)); editingSource = Packages.List().FirstOrDefault(p => p.Id == draft.Id); draftFeature = draft.Controls[0].Id; SaveDraft(); ShowPage(); })); start.Children.Add(tools); body.Children.Add(Card(start)); if (draft == null) return;
        var info = Section("基本信息与适用设备", "草稿自动保存；编辑副本不会修改已安装包。协议只作用于指定 EDID 身份。");
        Field(info, "包 id", draft.Id, v => draft.Id = v); Field(info, "显示名称", draft.Name, v => draft.Name = v); Field(info, "作者", draft.Author, v => draft.Author = v); Field(info, "版本", draft.Version, v => draft.Version = v);
        Field(info, "EDID 厂商", draft.Match.Manufacturer, v => draft.Match.Manufacturer = v); Field(info, "产品代码", draft.Match.ProductCode, v => draft.Match.ProductCode = v); Field(info, "型号限定（可空）", draft.Match.Name, v => draft.Match.Name = v); Field(info, "依赖说明（逗号分隔）", string.Join(",", draft.Dependencies), v => draft.Dependencies = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());
        Field(info, "默认采用的标准项 id", string.Join(",", draft.OverridesStandard), value => draft.OverridesStandard = Split(value));
        info.Children.Add(Text($"类型：{draft.Kind} · 默认通道：{draft.Backend}；可在每个功能中单独选择", 13, "#64748C")); body.Children.Add(Card(new Expander { Header = "1. 基本信息与适用设备", Content = info, IsExpanded = false }));
        var feature = Section("功能与协议", "数字、枚举、开关、状态、文本与动作使用明确类型；文本/动作/只写由独立插件实现。"); var choose = new ComboBox { ItemsSource = draft.Controls, DisplayMemberPath = "Name", MinHeight = 36, SelectedItem = draft.Controls.FirstOrDefault(f => f.Id == draftFeature) ?? draft.Controls[0], Margin = new Thickness(0, 0, 0, 12) }; choose.SelectionChanged += (_, _) => { if (choose.SelectedItem is ControlDefinition f) { draftFeature = f.Id; ShowPage(); } }; feature.Children.Add(choose);
        var f = (ControlDefinition)choose.SelectedItem; draftFeature = f.Id; Field(feature, "功能 id", f.Id, v => { f.Id = v; draftFeature = v; }); Field(feature, "功能名称", f.Name, v => f.Name = v); Field(feature, "分组 id", f.Group, v => f.Group = v); Field(feature, "单位", f.Unit, v => f.Unit = v);
        Choice(feature, "值类型", ["number", "enum", "boolean", "status", "text", "action"], f.ValueType, v => f.ValueType = v); var flags = Actions(); var read = new CheckBox { Content = "允许读取", IsChecked = f.Readable, Margin = new Thickness(0, 0, 20, 8) }; var write = new CheckBox { Content = "允许写入/执行", IsChecked = f.Writable, Margin = new Thickness(0, 0, 20, 8) }; read.Click += (_, _) => { f.Readable = read.IsChecked == true; SaveDraft(); }; write.Click += (_, _) => { f.Writable = write.IsChecked == true; SaveDraft(); }; flags.Children.Add(read); flags.Children.Add(write); feature.Children.Add(flags);
        Field(feature, "最小值", f.Minimum.ToString(), v => f.Minimum = uint.Parse(v)); Field(feature, "步长", f.Step.ToString(), v => f.Step = uint.Parse(v)); Field(feature, "写入白名单（可空）", f.AllowedWriteValues == null ? "" : string.Join(",", f.AllowedWriteValues), v => f.AllowedWriteValues = v.Length == 0 ? null : v.Split(',').Select(uint.Parse).ToList());
        Choice(feature, "写入系统 HDR 条件", ["不限", "关闭", "开启"], f.WriteWhenWindowsHdr == null ? "不限" : f.WriteWhenWindowsHdr.Value ? "开启" : "关闭", v => f.WriteWhenWindowsHdr = v == "不限" ? null : v == "开启");
        Field(feature, "影响项 id（逗号分隔）", string.Join(",", f.AffectedFeatures), v => f.AffectedFeatures = Split(v)); Field(feature, "前置写入项 id", string.Join(",", f.WriteAfter), v => f.WriteAfter = Split(v));
        Field(feature, "显示器菜单路径", f.MenuPath, v => f.MenuPath = v);
        Field(feature, "功能说明 / 适配限制", f.Description, v => f.Description = v);
        Field(feature, "帮助说明（点击展开）", f.HelpText, v => f.HelpText = v);
        ConditionEditor(feature, "显示条件", f.VisibleWhen);
        ConditionEditor(feature, "读取条件", f.ReadableWhen);
        ConditionEditor(feature, "写入条件", f.WritableWhen);
        if (draft.Kind == "protocol") Choice(feature, "此功能通信通道", ["继承包默认", "standard-ddc", "amd-ddc"], f.Backend ?? "继承包默认", value => { f.Backend = value == "继承包默认" ? null : value; bool standard = Packages.BackendFor(draft, f) == "standard-ddc"; f.VcpCode = standard ? (byte)16 : null; f.Raw = standard ? null : new(); f.Writable = false; SaveDraft(); ShowPage(); });
        if (draft.Kind == "protocol" && Packages.BackendFor(draft, f) == "standard-ddc") Field(feature, "VCP 十六进制码", f.VcpCode?.ToString("X2") ?? "", v => f.VcpCode = Convert.ToByte(v, 16));
        if (draft.Kind == "protocol" && Packages.BackendFor(draft, f) == "amd-ddc") {
            f.Raw ??= new(); var raw = f.Raw;
            Field(feature, "GET payload（十六进制）", raw.ReadPayload, v => raw.ReadPayload = v); Field(feature, "SET prefix（十六进制）", raw.WritePrefix, v => raw.WritePrefix = v); Field(feature, "响应前缀", raw.ReplyPrefix, v => raw.ReplyPrefix = v); Field(feature, "响应字节数", raw.ReplyLength.ToString(), v => raw.ReplyLength = int.Parse(v)); Field(feature, "当前值偏移", raw.CurrentOffset.ToString(), v => raw.CurrentOffset = int.Parse(v)); Field(feature, "最大值偏移", raw.MaximumOffset.ToString(), v => raw.MaximumOffset = int.Parse(v)); Field(feature, "XOR 校验种子", raw.ChecksumSeed.ToString("X2"), v => raw.ChecksumSeed = Convert.ToByte(v, 16));
        }
        if (draft.Kind == "plugin") Field(feature, "插件入口（包内 exe）", draft.EntryPoint ?? "", v => draft.EntryPoint = v);
        var crud = Actions(); crud.Children.Add(Button("新增功能", () => { var next = new ControlDefinition { Id = "vendor.feature-" + draft.Controls.Count, Name = "新功能", VcpCode = draft.Backend == "standard-ddc" ? (byte)16 : null, Raw = draft.Backend == "amd-ddc" ? new() : null }; draft.Controls.Add(next); draftFeature = next.Id; SaveDraft(); ShowPage(); })); crud.Children.Add(Button("删除此功能", () => { if (draft.Controls.Count <= 1) throw new InvalidOperationException("至少保留一个功能"); draft.Controls.Remove(f); draftFeature = null; SaveDraft(); ShowPage(); })); feature.Children.Add(crud); body.Children.Add(Card(new Expander { Header = "2. 功能与协议", Content = feature, IsExpanded = true }));
        var rules = Section("条件与选项映射", "选项 id 保持稳定（例如 game），显示名可翻译，设备编码可以随系统 HDR 和范围变化。白名单以设备编码限制。");
        foreach (var rule in f.Rules) {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 18) }; Field(block, "范围字段 maximum", rule.Maximum.ToString(), v => rule.Maximum = uint.Parse(v)); Choice(block, "系统 HDR", ["不限", "关闭", "开启"], rule.WindowsHdr == null ? "不限" : rule.WindowsHdr.Value ? "开启" : "关闭", v => rule.WindowsHdr = v == "不限" ? null : v == "开启");
            var mapping = new TextBox { Text = string.Join("\n", rule.Values.Select(kv => kv.Key + "|" + rule.Labels.GetValueOrDefault(kv.Key, kv.Key) + "|" + kv.Value)), AcceptsReturn = true, MinHeight = 100, Margin = new Thickness(0, 5, 0, 10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            block.Children.Add(Text("每行：稳定 id | 显示名称 | 数字编码", 12, "#64748C")); block.Children.Add(mapping); block.Children.Add(Button("保存此映射", () => { var values = new Dictionary<string, uint>(); var labels = new Dictionary<string, string>(); foreach (var line in mapping.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)) { var parts = line.Trim().Split('|'); if (parts.Length != 3) throw new InvalidDataException("每行需要 id|名称|编码"); values.Add(parts[0], uint.Parse(parts[2])); labels.Add(parts[0], parts[1]); } rule.Values = values; rule.Labels = labels; SaveDraft(); Say("映射已保存，尚未进行硬件验证"); })); block.Children.Add(Button("删除此条件", () => { f.Rules.Remove(rule); SaveDraft(); ShowPage(); })); rules.Children.Add(block);
        }
        rules.Children.Add(Button("添加条件规则", () => { f.Rules.Add(new() { Maximum = 100, Values = new() { ["option"] = 0 }, Labels = new() { ["option"] = "新选项" } }); SaveDraft(); ShowPage(); })); body.Children.Add(Card(new Expander { Header = "3. 条件与选项映射", Content = rules, IsExpanded = f.Rules.Count > 0 }));
        var replay = Section("样例回放与主动验证", "回放只验证解析，不证明设备支持。主动测试与完整证据分开保存。"); var bytes = new TextBox { MinHeight = 36, Margin = new Thickness(0, 0, 0, 12), ToolTip = "粘贴完整响应十六进制" }; replay.Children.Add(bytes); var results = Text("尚未回放", 13, "#64748C"); replay.Children.Add(results); var testing = Actions();
        testing.Children.Add(Button("回放此响应", () => { Packages.Validate(draft); if (f.Raw == null) throw new InvalidOperationException("标准功能无需私有响应解析；可主动测试读取"); var value = ProtocolCodec.Parse(f.Raw, Packages.Hex(bytes.Text)); var rule = ProtocolCodec.Rule(f, value.Maximum, device?.WindowsHdrEnabled); results.Text = $"当前 {value.Current} · 范围 {value.Maximum} · 命中规则 {(rule == null ? "无" : string.Join("、", rule.Values.Where(x => x.Value == value.Current).Select(x => rule.Labels.GetValueOrDefault(x.Key, x.Key))))}"; }));
        testing.Children.Add(AsyncButton("测试读取草稿", async t => { var result = await TestDraft(f, "read", null, t); results.Text = result.Message + $" · 当前 {result.Current} / 范围 {result.Maximum}"; }));
        testing.Children.Add(AsyncButton("测试写入并核查", async t => { if (!f.Writable) throw new InvalidOperationException("草稿未声明可写"); string? value = Prompt("协议写入测试", "目标语义 id 或数值；仅发送已知指令", ""); if (value == null) return; if (!Confirm("将向当前设备发送草稿指令。可能影响其他设置；有原值时可在测试后选择恢复。效果需要你观察确认。继续？")) return; var before = f.Readable ? await TestDraft(f, "read", null, t) : null; if (f.Readable && before?.Status != "ok") throw new InvalidOperationException("原值读取失败，不开始写入测试"); var result = await TestDraft(f, "write", value, t); results.Text = result.Message; if (before?.Current != null && result.Confirmed && Confirm("已回读确认。是否恢复测试前值？只恢复此功能，不包括未采集联动项。")) { var rule = ProtocolCodec.Rule(f, before.Maximum ?? 0, device?.WindowsHdrEnabled); string old = rule?.Values.FirstOrDefault(x => x.Value == before.Current).Key ?? before.Current.ToString()!; var restored = await TestDraft(f, "write", old, t); results.Text += "；恢复：" + restored.Message; } })); replay.Children.Add(testing); body.Children.Add(Card(new Expander { Header = "4. 样例回放与主动验证", Content = replay, IsExpanded = true }));
        var delivery = Section("校验与交付", "格式校验不会提升验证级别；用户副本安装前预览新增/覆盖项。代码插件副本保留完整二进制。"); var actions = Actions(); actions.Children.Add(Button("校验定义", () => { Packages.Validate(draft); ValidateDependencies(draft); Say("定义校验通过，未代表硬件支持或效果验证"); })); actions.Children.Add(Button("导出定义 JSON", () => { Packages.Validate(draft); ExportJson(draft, draft.Id + ".json"); })); actions.Children.Add(Button("安装并试用", () => InstallDraft(false), true)); actions.Children.Add(Button("导出完整 ZIP", () => InstallDraft(true))); delivery.Children.Add(actions);
        var json = new TextBox { Text = JsonSerializer.Serialize(draft, Packages.Json), AcceptsReturn = true, AcceptsTab = true, Height = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") }; var advanced = new StackPanel(); advanced.Children.Add(json); advanced.Children.Add(Button("将 JSON 应用到表单", () => { draft = Packages.Parse(json.Text); SaveDraft(); ShowPage(); })); delivery.Children.Add(new Expander { Header = "高级 JSON（与表单同一模型）", Content = advanced }); body.Children.Add(Card(delivery));
    }
    static List<string> Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    void ConditionEditor(StackPanel panel, string name, List<FeatureCondition> conditions)
    {
        var content = new StackPanel();
        var input = new TextBox { Text = string.Join("\n", conditions.Select(c => c.FeatureId + "|" + c.EqualsValue + "|" + c.Reason)), AcceptsReturn = true, MinHeight = 60, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        content.Children.Add(Text("每行：依赖功能 id | 原始值 | 不满足时的说明；空表示不限", 12, "#64748C")); content.Children.Add(input);
        content.Children.Add(Button("保存" + name, () => { var parsed = new List<FeatureCondition>(); foreach (var line in input.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)) { var fields = line.Trim().Split('|'); if (fields.Length != 3) throw new InvalidDataException("条件格式应为 id|值|说明"); parsed.Add(new() { FeatureId = fields[0], EqualsValue = uint.Parse(fields[1]), Reason = fields[2] }); } conditions.Clear(); conditions.AddRange(parsed); SaveDraft(); Say(name + "已保存，校验时检查依赖环路"); }));
        panel.Children.Add(new Expander { Header = name + "（" + conditions.Count + "）", Content = content, Margin = new Thickness(0, 0, 0, 10) });
    }
    void Field(StackPanel panel, string label, string value, Action<string> save)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(205) }); row.ColumnDefinitions.Add(new()); var title = Text(label, 13, "#64748C"); title.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(title); var box = new TextBox { Text = value, MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center }; Grid.SetColumn(box, 1); row.Children.Add(box); box.LostKeyboardFocus += (_, _) => { try { save(box.Text); SaveDraft(); } catch (Exception ex) { Say(label + "：" + ex.Message); } }; panel.Children.Add(row);
    }
    void Choice(StackPanel panel, string label, string[] items, string current, Action<string> save)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(205) }); row.ColumnDefinitions.Add(new()); row.Children.Add(Text(label, 13, "#64748C")); var box = new ComboBox { ItemsSource = items, SelectedItem = current, MinHeight = 34 }; box.SelectionChanged += (_, _) => { save((string)box.SelectedItem); SaveDraft(); }; Grid.SetColumn(box, 1); row.Children.Add(box); panel.Children.Add(row);
    }
    static void ValidateDependencies(AdapterPackage package)
    {
        var pending = package.Controls.ToDictionary(f => f.Id, f => f.WriteAfter.ToList()); var done = new HashSet<string>();
        while (pending.Count > 0) { var ready = pending.FirstOrDefault(kv => kv.Value.All(id => done.Contains(id) || !pending.ContainsKey(id))); if (ready.Key == null) throw new InvalidDataException("功能写入顺序有环路"); done.Add(ready.Key); pending.Remove(ready.Key); }
    }
    async Task<ControlResult> TestDraft(ControlDefinition f, string action, string? value, CancellationToken t)
    {
        if (draft == null || device == null) throw new InvalidOperationException("先选择设备和草稿"); Packages.Validate(draft); ValidateDependencies(draft);
        if (simulation) throw new InvalidOperationException("草稿实机测试不在模拟设备执行；可以使用离线样例回放");
        if (draft.Kind == "plugin") throw new InvalidOperationException("代码插件先完整安装并确认信任，再在控制页测试；不会运行未知草稿代码");
        var fresh = (await workspace.Backend.Devices(t)).SingleOrDefault(d => d.DevicePath == device.DevicePath && d.Device == device.Device) ?? throw new IOException("设备连接变化");
        var request = new ControlRequest { Device = fresh, ControlId = f.Id, Action = action, Value = value, DraftPackage = Library.Copy(draft) }; var result = await workspace.Backend.Execute(request, t); string id = Library.NewId(); string draftHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft, Packages.Json)))); Library.Save("protocol-tests", id, new { draft = Library.Copy(draft), draftHash, device = fresh, request, result, time = DateTimeOffset.Now, visualConfirmation = "尚未人工确认" }); Say("测试证据已保存：" + id); return result;
    }
    void InstallDraft(bool export)
    {
        if (draft == null) return; Packages.Validate(draft); ValidateDependencies(draft); SaveDraft(); string? destination = export ? Save("适配包 ZIP|*.zip", draft.Id + ".zip") : null; if (export && destination == null) return;
        if (!export && !Confirm("安装 " + draft.Name + "\n功能：" + string.Join("、", draft.Controls.Select(f => f.Name)) + "\n尚未绑定设备，不自动发送指令。继续？")) return;
        string stage = Path.Combine(Library.Folder("temp"), Library.NewId()); Directory.CreateDirectory(stage);
        try {
            if (draft.Kind == "plugin") { if (editingSource == null) throw new InvalidOperationException("插件副本需要已安装的完整代码来源"); foreach (var file in Directory.GetFiles(editingSource.DirectoryPath, "*", SearchOption.AllDirectories)) { string target = Path.Combine(stage, Path.GetRelativePath(editingSource.DirectoryPath, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); } }
            File.WriteAllText(Path.Combine(stage, "adapter.json"), JsonSerializer.Serialize(draft, Packages.Json)); string zip = stage + ".zip";
            try { ZipFile.CreateFromDirectory(stage, zip); if (export) { File.Copy(zip, destination!, true); Say("完整适配包已导出"); } else { var package = Packages.Install(zip); if (device != null && package.Match.Matches(device) && package.Kind == "protocol") { Packages.Attach(device, package, false); focusedFeature = draftFeature; group = draft.Controls.First().Group; Navigate(reader ? "check" : "control"); _ = Run(t => RefreshValues(t)); } else Navigate("packages"); } } finally { if (File.Exists(zip)) File.Delete(zip); }
        } finally { Directory.Delete(stage, true); }
    }
}

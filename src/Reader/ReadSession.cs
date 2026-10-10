namespace MonitorInspector;

// Baselines are per physical connection. A failed/cancelled read never replaces one.
internal sealed class ReadSession
{
    private readonly Dictionary<string, DdcResult> baselines = new();
    private readonly Dictionary<string, List<ChangeRecord>> history = new();
    internal List<ChangeRecord> History(string path) => history.TryGetValue(path, out var entries) ? entries : history[path] = [];
    internal string Observe(MonitorData monitor, DdcResult result, string operation)
    {
        monitor.Ddc = result; monitor.Changes = History(monitor.DevicePath);
        foreach (var row in result.Features) row.Changed = false;
        if (!result.Features.Any(x => x.Current.HasValue)) return result.Status + "；没有更新内存基线。";
        if (!baselines.TryGetValue(monitor.DevicePath, out var baseline))
        {
            baselines[monitor.DevicePath] = result;
            return "已记住当前值。现在手动操作显示器，再点击“读取并比较”。";
        }
        var prior = baseline.Features.ToDictionary(x => (x.Physical, x.Code));
        int changed = 0, unavailable = 0;
        var merged = baseline.Features.ToDictionary(x => (x.Physical, x.Code));
        foreach (var row in result.Features)
        {
            var key = (row.Physical, row.Code);
            if (!row.Current.HasValue) { if (prior.TryGetValue(key, out var known) && known.Current.HasValue) unavailable++; continue; }
            if (prior.TryGetValue(key, out var before) && before.Current.HasValue && before.Current != row.Current)
            {
                row.Changed = true; changed++;
                monitor.Changes.Add(new() { Time = result.ReadAt, Operation = operation.Trim(), Code = row.Code, Name = row.Name, Physical = row.Physical, Before = before.Current, After = row.Current, Note = "数值变化线索，尚未验证功能因果关系" });
            }
            merged[key] = row;
        }
        baselines[monitor.DevicePath] = new() { Features = merged.Values.ToList(), ReadAt = result.ReadAt };
        return $"{(changed > 0 ? $"发现 {changed} 项数值变化，已自动记录。" : "未发现已知控制项数值变化。")}{(unavailable > 0 ? $" {unavailable} 项本次未读到，上次读数保留。" : "")}";
    }
    internal string Describe(MonitorData monitor)
    {
        var entries = History(monitor.DevicePath);
        if (entries.Count == 0) return "尚未发现数值变化。第一次读取会记住当前值；操作显示器后，再点“读取并比较”。\n\n读取结果和变化会自动写入程序目录 logs/current.jsonl；每次正常启动清空上一轮日志。\n\n本工具比较读取结果，不截获通信。隐藏、只写或未开放的控制项可能读不到。";
        return string.Join("\n\n", entries.AsEnumerable().Reverse().Select(x => $"{x.Time:HH:mm:ss}  ·  {(x.Operation.Length > 0 ? x.Operation : "未填写操作说明")}\n{x.Code}  {x.Name}：{x.Before} → {x.After}\n{x.Physical}")) + "\n\n以上都是数值变化线索，不能自动证明功能对应关系。已自动写入 logs/current.jsonl；下次正常启动清空。";
    }
}

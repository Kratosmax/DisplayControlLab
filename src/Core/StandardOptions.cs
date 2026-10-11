namespace MonitorInspector;
public static class StandardOptions
{
    public static Dictionary<string, uint> Values(string id) => id switch {
        "color-preset" => new() { ["srgb"] = 1, ["5000k"] = 4, ["6500k"] = 5, ["7500k"] = 6, ["9300k"] = 8, ["user"] = 11 },
        "mute" => new() { ["muted"] = 1, ["audible"] = 2 },
        "power" => new() { ["on"] = 1, ["standby"] = 2, ["suspend"] = 3, ["off-soft"] = 4, ["off"] = 5 },
        "input-source" => new() { ["vga"] = 1, ["dvi-1"] = 3, ["dp-1"] = 15, ["dp-2"] = 16, ["hdmi-1"] = 17, ["hdmi-2"] = 18, ["hdmi-3"] = 19, ["usb-c-1"] = 21 },
        "osd" => new() { ["disabled"] = 1, ["enabled"] = 2 },
        "osd-language" => new() { ["english"] = 2, ["french"] = 3, ["german"] = 4, ["italian"] = 5, ["japanese"] = 6, ["korean"] = 7, ["portuguese"] = 8, ["russian"] = 9, ["spanish"] = 10, ["swedish"] = 11, ["traditional-chinese"] = 1, ["turkish"] = 12, ["simplified-chinese"] = 13 },
        _ => []
    };
    public static Dictionary<string, string> Labels(string id) => id switch {
        "color-preset" => new() { ["srgb"] = "sRGB", ["5000k"] = "5000K", ["6500k"] = "6500K", ["7500k"] = "7500K（标准定义）", ["9300k"] = "9300K", ["user"] = "用户自定义" },
        "mute" => new() { ["muted"] = "静音", ["audible"] = "未静音" },
        "power" => new() { ["on"] = "开启", ["standby"] = "待机", ["suspend"] = "挂起", ["off-soft"] = "软关闭", ["off"] = "关闭" },
        "input-source" => new() { ["vga"] = "VGA", ["dvi-1"] = "DVI 1", ["dp-1"] = "DisplayPort 1", ["dp-2"] = "DisplayPort 2", ["hdmi-1"] = "HDMI 1", ["hdmi-2"] = "HDMI 2", ["hdmi-3"] = "HDMI 3", ["usb-c-1"] = "USB-C 1" },
        "osd" => new() { ["disabled"] = "OSD 禁用", ["enabled"] = "OSD 启用" },
        "osd-language" => new() { ["english"] = "英语", ["french"] = "法语", ["german"] = "德语", ["italian"] = "意大利语", ["japanese"] = "日语", ["korean"] = "韩语", ["portuguese"] = "葡萄牙语", ["russian"] = "俄语", ["spanish"] = "西班牙语", ["swedish"] = "瑞典语", ["traditional-chinese"] = "繁体中文", ["turkish"] = "土耳其语", ["simplified-chinese"] = "简体中文" },
        _ => []
    };
    public static ControlDefinition Feature(string id, string name, byte code, string type, string group, bool writable)
    {
        var definition = new ControlDefinition { Id = id, Name = name, VcpCode = code, ValueType = type, Group = group, Writable = writable };
        var values = Values(id); if (values.Count > 0) definition.Rules.Add(new() { MatchAnyMaximum = true, Values = values, Labels = Labels(id) });
        if (id == "color-preset") { definition.Writable = true; definition.AffectedFeatures = ["red-gain", "green-gain", "blue-gain"]; }
        return definition;
    }
}

using MonitorInspector;
using System.Text.Json;

internal static class VcpRecognitionTests
{
    public static void Run(Action<bool, string> check)
    {
        var caps = Parsers.Capabilities("(prot(monitor)vcp(14(05 08 0B 0C) 60(0F 11 1B) EA(FE00 FE01) DF)mccs_ver(2.1))");
        check(caps[0xea].SequenceEqual(new ushort[] { 0xfe00, 0xfe01 }) && caps.ContainsKey(0xdf) && caps[0x14].Count == 4,
            "Dell四位枚举值不导致整份能力声明丢失，后续标准项仍解析");
        bool invalid = false; try { Parsers.Capabilities("vcp(100(01))"); } catch (FormatException) { invalid = true; }
        check(invalid, "枚举值支持两字节，不放宽控制码的一字节边界");
        invalid = false; try { Parsers.Capabilities("vcp(EA(10000))"); } catch (FormatException) { invalid = true; }
        check(invalid, "超出两字节的枚举值仍拒绝，不默默截断");
        SurveyFeature Feature(byte code, uint current, uint maximum = 255) => new() {
            Definition = new() { Id = $"vcp.{code:x2}", Name = "未知", VcpCode = code, ValueType = "status" }, CanExport = true, ReadStatus = "读取已验证",
            Samples = Enumerable.Range(0, 3).Select(_ => new ControlResult { Status = "ok", Current = current, Maximum = maximum }).ToList()
        };
        ReaderSurvey Report(string manufacturer = "TST") => new() { Status = "已完成", Device = new() { Name = "Catalog test", Device = "TEST", DevicePath = "catalog-test",
            WindowsHdrEnabled = false, Edid = new() { Manufacturer = manufacturer, Product = "0x1234" } }, Discovery = new(),
            Features = [new() { Definition = Library.Copy(DeviceControl.Standard.Single(f => f.Id == "mccs-version")), ReadStatus = "读取已验证", CanExport = true,
                Samples = [new() { Status = "ok", Current = 513, Maximum = 65535 }] }] };
        check(VcpRecognition.StandardCount >= 70 && VcpNames.Name(0x3e) == "采样时钟相位" && VcpNames.Name(0x9b) == "六轴色相：红"
            && VcpNames.Name(0x9d) == "六轴色相：绿" && VcpNames.Name(0x9f) == "六轴色相：蓝", "扩展标准码名称按公开目录核对，不把相邻地址错当同项");
        var report = Report(); report.Features.AddRange([Feature(0x6c, 128), Feature(0x87, 50, 100), Feature(0x72, 120), Feature(0xae, 6000), Feature(0xb6, 3), Feature(0xc0, 40402), Feature(0xc6, 17868), Feature(0xe8, 7)]);
        report.Discovery!.Features.Add(new() { Code = "0x6C", Declared = "显示器声明" });
        report.Discovery.Features.Add(new() { Code = "0x87", Declared = "显示器声明" });
        report.RestoreGeneratedStandardDefinitions();
        var package = new AdapterPackage { Controls = report.Features.Select(f => f.Definition).ToList() };
        check(package.Controls.Single(f => f.Id == "vcp.6c") is { Name: "红色黑电平", Writable: true, ValueType: "number", WriteWhenWindowsHdr: false },
            "声明、版本与有效范围允许标准黑电平进入可测试定义，未试写不进入控制包");
        check(package.Controls.Single(f => f.Id == "vcp.72") is { Writable: false, ValueType: "status" } && package.Controls.Single(f => f.Id == "vcp.e8").Name == "未知", "复合Gamma保持只读，未收录私有码不猜功能");
        string Display(string id) { var f = package.Controls.Single(f => f.Id == id); return new Reading { Capability = new() { Definition = f }, Result = report.Features.Single(x => x.Definition.Id == id).Samples[0] }.Display(false); }
        check(Display("vcp.ae").Contains("60 Hz") && Display("vcp.b6") == "LCD（有源矩阵）" && Display("vcp.c0").Contains("40402 小时") && Display("vcp.c6") == "0x45CC", "读取器复用标准状态解释：频率、技术类型、使用时长、识别键");
        ControlDefinition Recognized(string id) { report.RestoreGeneratedStandardDefinitions(); return report.Features.Single(f => f.Definition.Id == id).Definition; }
        report.Features[0].Samples[0].Current = 512;
        check(!Recognized("vcp.87").Writable, "MCCS2.0的锐度不误套2.1连续数值写入");
        report.Features[0].Samples[0].Current = 513; report.Discovery.Features.Clear();
        check(!Recognized("vcp.6c").Writable, "未声明的扩展响应只识别，不因稳定回读就开放新写入");
        report.Discovery.Features.Add(new() { Code = "0x6C", Declared = "显示器声明" });
        foreach (var sample in report.Features.Single(f => f.Definition.Id == "vcp.6c").Samples) sample.Maximum = 100;
        check(!Recognized("vcp.6c").Writable, "异常当前值大于范围保留原始证据与只读识别");
        report = Report("DEL"); report.Features.AddRange([Feature(0xe0, 0, 1), Feature(0xe1, 0, 1)]);
        var input = new SurveyFeature { Definition = Library.Copy(DeviceControl.Standard.Single(f => f.Id == "input-source")), ReadStatus = "读取已验证", CanExport = true,
            Samples = [new() { Status = "ok", Current = 0x0f0f, Maximum = 3 }] }; report.Features.Add(input);
        var color = new SurveyFeature { Definition = Library.Copy(DeviceControl.Standard.Single(f => f.Id == "color-preset")), ReadStatus = "读取已验证", CanExport = true,
            Samples = [new() { Status = "ok", Current = 5, Maximum = 12 }] }; report.Features.Add(color);
        report.Rounds.Add(new() { Device = report.Device, Readings = [new() { Capability = new() { Definition = input.Definition } }, new() { Capability = new() { Definition = color.Definition } }] });
        report.Discovery!.Features.Add(new() { Code = "0x60", Declared = "显示器声明", Values = "0x0F, 0x11, 0x1B" });
        report.RestoreGeneratedStandardDefinitions(); package = new AdapterPackage { Controls = report.Features.Select(f => f.Definition).ToList() };
        check(package.Controls.Single(f => f.Id == "vcp.e1") is { Writable: false, ValueType: "status" } dellPower
            && dellPower.Name.Contains("厂商候选") && dellPower.HelpText.Contains("DEL4245.xml")
            && new Reading { Capability = new() { Definition = dellPower }, Result = new() { Status = "ok", Current = 0 } }.Display(false).Contains("开启"), "品牌级Dell私有码有来源且只读候选，不当成标准电源编码");
        check(new Reading { Capability = new() { Definition = package.Controls.Single(f => f.Id == "input-source") }, Result = input.Samples[0] }.Display(false).Contains("DisplayPort 1（厂商候选）")
            && !package.Controls.Single(f => f.Id == "input-source").Writable, "Dell复合输入源解释实际回读，不把复合数值当作SET编码");
        input.Definition.Rules[0].Values.Remove("code-0f0f"); input.Definition.Rules[0].Labels.Remove("code-0f0f");
        input.Definition.Rules[0].Values["existing-composite"] = 0x0f0f;
        report.RestoreGeneratedStandardDefinitions(); var existingComposite = new AdapterPackage { Controls = report.Features.Select(f => f.Definition).ToList() };
        check(existingComposite.Controls.Single(f => f.Id == "input-source").Rules[0].Values.Values.Count(v => v == 0x0f0f) == 1,
            "品牌标签复用已有复合编码，不生成重复取值导致适配包无法导入");
        report = Report("MSI"); report.Features.Add(Feature(0xe1, 0, 1));
        check(!Recognized("vcp.e1").Name.Contains("Dell"), "不同厂商不套用Dell候选");
        report = Report("AOC"); report.Features.Add(color); report.Rounds.Add(new() { Device = report.Device, Readings = [new() { Capability = new() { Definition = color.Definition } }] });
        report.RestoreGeneratedStandardDefinitions(); package = new AdapterPackage { Controls = report.Features.Select(f => f.Definition).ToList() };
        check(package.Controls.Single(f => f.Id == "color-preset").Rules[0].Labels["6500k"] == "暖色（厂商候选）", "AOC共用色温标签不猜准确K值，候选来源保留在读取器");
        color.Definition.Rules[0].Labels["6500k"] = "厂家实测6500K";
        report.Rounds[0].Readings[0].Capability.Provider = "test.model-specific";
        check(Recognized("color-preset").Rules[0].Labels["6500k"] == "厂家实测6500K", "型号包或第三方定义优先，品牌标签不覆盖实测映射");
        report.Rounds[0].Readings[0].Capability.Provider = "user.detected-from-model";
        check(Recognized("color-preset").Rules[0].Labels["6500k"] == "厂家实测6500K",
            "型号映射经过生成包再检测仍保留，不被品牌候选重新命名");
        var policy = new HdrSafetyPolicy { BlockedFeatures = ["brightness"] };
        check(HdrSafety.ProtectedId(policy, new() { Id = "vcp.6c", VcpCode = 0x6c }) == "brightness", "新增标准图像项不能绕过已有显示器HDR保护");
    }
}

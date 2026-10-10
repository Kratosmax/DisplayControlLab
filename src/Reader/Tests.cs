using System.Runtime.InteropServices;

namespace MonitorInspector;
internal static class Tests
{
    internal static string Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed.Add("PASS: " + name); }
        Check(Marshal.SizeOf<Native.Path>() == 72 && Marshal.SizeOf<Native.Mode>() == 64 && Marshal.SizeOf<Native.Physical>() == (IntPtr.Size == 8 ? 264 : 260), "Win32 结构体尺寸");
        var caps = Parsers.Capabilities("(prot(monitor)type(LCD)vcp(10 12 14(01 05 08 0B) 16 18 1A DC(00 01) DF)mccs_ver(2.2))");
        Check(caps.Count == 8 && caps[0x14].SequenceEqual(new byte[] {1,5,8,11}) && caps[0x10].Count == 0, "相邻章节与离散 VCP 取值解析");
        Check(Parsers.Capabilities("(model(TEST))").Count == 0, "缺少 VCP 时不推测支持");
        bool threw = false; try { Parsers.Capabilities("vcp(14(01 05"); } catch (FormatException) { threw = true; }
        Check(threw, "拒绝截断能力字符串");
        var edid = new byte[256]; new byte[] {0,255,255,255,255,255,255,0}.CopyTo(edid,0);
        edid[8] = 0x04; edid[9] = 0x43; edid[10] = 0x34; edid[11] = 0x12;
        edid[17] = 35; edid[21] = 60; edid[22] = 34; edid[57] = 0xFC;
        System.Text.Encoding.ASCII.GetBytes("TEST PANEL\n  ").CopyTo(edid,59);
        edid[126] = 1; edid[128] = 2; edid[130] = 8; edid[132] = 0xE3; edid[133] = 6; edid[134] = 4; edid[135] = 1;
        for (int block = 0; block < 2; block++) edid[block*128+127] = (byte)((256-edid.Skip(block*128).Take(127).Sum(x=>(int)x)%256)%256);
        var parsed = Parsers.Edid(edid);
        Check(parsed.Manufacturer == "ABC" && parsed.Product == "0x1234" && parsed.Name == "TEST PANEL" && parsed.HdrMetadata.Contains("HDR10"), "EDID 标识、名称和 CTA HDR 数据块");
        edid[20]++; Check(Parsers.Edid(edid).Validation.Contains("错误"), "校验损坏 EDID 不输出可信硬件结论");
        Check(Parsers.Edid([1,2,3]).Validation.Contains("无效"), "短 EDID 安全回退");
        DdcResult Reading(uint? value) => new() { Features = [new() { Code="0xE1", Physical="1", Current=value, Status=value.HasValue ? "成功" : "失败" }] };
        var session = new ReadSession(); var monitor = new MonitorData { DevicePath="test" };
        Check(session.Observe(monitor, Reading(1), "").Contains("记住") && monitor.Changes.Count == 0, "首次读取建立内存基线");
        session.Observe(monitor, Reading(2), "HDR Game");
        Check(monitor.Changes.Count == 1 && monitor.Changes[0].Before == 1 && monitor.Changes[0].After == 2 && monitor.Changes[0].Operation == "HDR Game", "自动记录变化值与操作说明");
        session.Observe(monitor, Reading(null), "失败"); session.Observe(monitor, Reading(3), "恢复");
        Check(monitor.Changes.Count == 2 && monitor.Changes[1].Before == 2, "失败读取保留有效基线");
        session.Observe(monitor, Reading(3), "重复读取");
        Check(monitor.Changes.Count == 2, "相同读数不重复记录");
        var other = new MonitorData { DevicePath="other" }; session.Observe(other, Reading(9), "其他屏幕");
        Check(other.Changes.Count == 0 && monitor.Changes.Count == 2, "不同显示器独立基线与记录");
        var exported = System.Text.Json.JsonSerializer.Serialize(monitor, Program.Json);
        Check(System.Text.Json.JsonSerializer.Deserialize<MonitorData>(exported)?.Changes.Count == 2, "导出记录包含历史变化");
        string logPath = Path.Combine(AppContext.BaseDirectory, "validation-tests", Guid.NewGuid().ToString("N"), "current.jsonl");
        string[] ReadLog()
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        using (var log = new AuditLog(logPath))
        {
            log.Write("read_completed", monitor);
            Check(ReadLog().Length == 2, "日志立即落盘且允许并行读取");
            bool locked = false;
            try { using var conflicting = new AuditLog(logPath); } catch (IOException) { locked = true; }
            Check(locked && ReadLog().Length == 2, "第二进程不能清空活跃日志");
        }
        using (var log = new AuditLog(logPath)) { Check(ReadLog().Length == 1, "新会话清空上一轮日志"); }
        Check(File.ReadAllLines(logPath).All(line => System.Text.Json.JsonDocument.Parse(line).RootElement.TryGetProperty("kind", out _)), "日志逐行可解析 JSON");
        string observation = ObservationStore.Save(Path.GetDirectoryName(logPath)!, monitor);
        using var observedJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(observation));
        Check(observedJson.RootElement.GetProperty("kind").GetString() == "observation" && observedJson.RootElement.GetProperty("controls")[0].GetProperty("verification").GetString() == "observed", "自动保存读取资料，不冒充已验证映射");
        return string.Join(Environment.NewLine, passed) + $"\n{passed.Count} 项通过\n";
    }
}

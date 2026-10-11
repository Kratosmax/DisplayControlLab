using System.Text;
using System.Text.RegularExpressions;

namespace MonitorInspector;

public sealed class MonitorData
{
    [System.Text.Json.Serialization.JsonIgnore] public string Nickname { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayName => Nickname.Length == 0 ? Name : Nickname;
    public string Name { get; set; } = "未识别型号";
    public string Device { get; set; } = "";
    public string DevicePath { get; set; } = "";
    public string Adapter { get; set; } = "未知";
    public string Connection { get; set; } = "未知";
    public string Resolution { get; set; } = "未知";
    public string RefreshRate { get; set; } = "未知";
    public bool? WindowsHdrEnabled { get; set; }
    public bool? HdrSupported { get; set; }
    public bool CanSetWindowsHdr { get; set; }
    public bool? HdrUserEnabled { get; set; }
    public string Hdr { get; set; } = "未读取";
    public string Color { get; set; } = "未知";
    public EdidData Edid { get; set; } = new();
    public List<string> Notes { get; set; } = [];
    public DdcResult? Ddc { get; set; }
    public List<ChangeRecord> Changes { get; set; } = [];
    public override string ToString() => $"{Name}  ·  {Device}";
}
public sealed class EdidData
{
    public string Manufacturer { get; set; } = "未知";
    public string Product { get; set; } = "未知";
    public string Name { get; set; } = "";
    public string Serial { get; set; } = "未报告";
    public string ManufactureDate { get; set; } = "未报告";
    public string Size { get; set; } = "未报告";
    public string HdrMetadata { get; set; } = "未读取";
    public string Validation { get; set; } = "未读取";
    public string RawHex { get; set; } = "";
}
public sealed class FeatureRow
{
    public uint? Type { get; set; }
    public DateTimeOffset ReadAt { get; set; }
    public double QueryDurationMs { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Declared { get; set; } = "";
    public string Status { get; set; } = "";
    public uint? Current { get; set; }
    public uint? Maximum { get; set; }
    public string Values { get; set; } = "";
    public string Interpretation { get; set; } = "";
    public int? Error { get; set; }
    public string Physical { get; set; } = "";
    public bool Changed { get; set; }
}
public sealed class ChangeRecord
{
    public DateTimeOffset Time { get; set; }
    public string Operation { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Physical { get; set; } = "";
    public uint? Before { get; set; }
    public uint? After { get; set; }
    public string Note { get; set; } = "";
}
public sealed class DdcResult
{
    public string QueryMode { get; set; } = "declared-and-common";
    public string WindowsHdr { get; set; } = "未读取";
    public string Status { get; set; } = "";
    public List<string> Capabilities { get; set; } = [];
    public List<FeatureRow> Features { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public DateTimeOffset ReadAt { get; set; } = DateTimeOffset.Now;
}
public static class Parsers
{
    // Parse the vcp(...) section with balanced parentheses, not a flat regex.
    public static Dictionary<byte, List<byte>> Capabilities(string text)
    {
        var result = new Dictionary<byte, List<byte>>();
        var start = Regex.Match(text, @"\bvcp\s*\(", RegexOptions.IgnoreCase);
        if (!start.Success) return result;
        var pos = start.Index + start.Length;
        while (pos < text.Length)
        {
            SkipSpace(text, ref pos);
            if (pos >= text.Length || text[pos] == ')') break;
            var code = HexToken(text, ref pos);
            if (code is null) throw new FormatException("VCP 能力字符串格式异常，已停止解析。");
            SkipSpace(text, ref pos);
            var values = new List<byte>();
            if (pos < text.Length && text[pos] == '(')
            {
                pos++;
                while (true)
                {
                    SkipSpace(text, ref pos);
                    if (pos >= text.Length) throw new FormatException("VCP 能力字符串被截断。");
                    if (text[pos] == ')') { pos++; break; }
                    var value = HexToken(text, ref pos);
                    if (value is null) throw new FormatException("VCP 取值格式异常。");
                    values.Add(value.Value);
                }
            }
            result[code.Value] = values;
        }
        if (pos >= text.Length) throw new FormatException("VCP 能力字符串缺少结束括号。");
        return result;
    }
    private static void SkipSpace(string text, ref int pos) { while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++; }
    private static byte? HexToken(string text, ref int pos)
    {
        int start = pos;
        while (pos < text.Length && Uri.IsHexDigit(text[pos])) pos++;
        if (pos == start || pos - start > 2) return null;
        return Convert.ToByte(text[start..pos], 16);
    }
    public static EdidData Edid(byte[] bytes)
    {
        var e = new EdidData { RawHex = Convert.ToHexString(bytes) };
        byte[] header = [0,255,255,255,255,255,255,0];
        if (bytes.Length < 128 || !bytes.Take(8).SequenceEqual(header))
        { e.Validation = "无效或不足 128 字节，未解析"; return e; }
        int blocks = Math.Min(bytes.Length / 128, 1 + bytes[126]);
        var bad = Enumerable.Range(0, blocks).Where(b => bytes.Skip(b * 128).Take(128).Sum(x => (int)x) % 256 != 0).ToList();
        if (bad.Count > 0) { e.Validation = "校验和错误，未信任 EDID 内容"; return e; }
        bool truncated = bytes.Length < (1 + bytes[126]) * 128;
        e.Validation = truncated ? "已收到块校验通过；扩展块不完整" : "校验通过";
        int m = bytes[8] << 8 | bytes[9];
        e.Manufacturer = new string([(char)(64 + ((m >> 10) & 31)), (char)(64 + ((m >> 5) & 31)), (char)(64 + (m & 31))]);
        e.Product = $"0x{BitConverter.ToUInt16(bytes, 10):X4}";
        uint serial = BitConverter.ToUInt32(bytes, 12);
        if (serial != 0) e.Serial = serial.ToString();
        e.ManufactureDate = bytes[16] == 255 ? $"型号年份 {1990 + bytes[17]}" : $"{1990 + bytes[17]} 年 / 第 {bytes[16]} 周";
        if (bytes[21] != 0 && bytes[22] != 0) e.Size = $"{bytes[21]} × {bytes[22]} cm / 约 {Math.Sqrt(bytes[21] * bytes[21] + bytes[22] * bytes[22]) / 2.54:F1} 英寸";
        for (int p = 54; p <= 108; p += 18)
        {
            if (bytes[p] != 0 || bytes[p + 1] != 0 || bytes[p + 2] != 0) continue;
            string s = Encoding.ASCII.GetString(bytes, p + 5, 13).Trim('\0', ' ', '\n', '\r');
            if (bytes[p + 3] == 0xFC) e.Name = s;
            if (bytes[p + 3] == 0xFF && s.Length > 0) e.Serial = s;
        }
        var hdr = new List<string>();
        bool malformed = false;
        for (int block = 1; block < blocks; block++)
        {
            int b = block * 128;
            if (bytes[b] != 2) continue; // CTA-861 extension
            int end = bytes[b + 2];
            if (end == 0) continue;
            if (end < 4 || end > 127) { malformed = true; continue; }
            for (int p = b + 4; p < b + end;)
            {
                int len = bytes[p] & 31, tag = bytes[p] >> 5;
                if (p + 1 + len > b + end) { malformed = true; break; }
                if (tag == 7 && len >= 3 && bytes[p + 1] == 6)
                {
                    int flags = bytes[p + 2];
                    if ((flags & 4) != 0) hdr.Add("PQ / HDR10 EOTF");
                    if ((flags & 8) != 0) hdr.Add("HLG EOTF");
                    if ((flags & 2) != 0) hdr.Add("传统 HDR EOTF");
                }
                p += len + 1;
            }
        }
        e.HdrMetadata = hdr.Count > 0 ? string.Join("、", hdr.Distinct()) + "（EDID 声明）" : truncated || malformed ? "扩展数据不完整，无法判断" : "未发现 CTA HDR EOTF 声明（不等于实机不支持）";
        return e;
    }
}
public static class VcpNames
{
    public static readonly Dictionary<byte, string> Names = new()
    {
        [0x02]="新控制值",[0x04]="恢复出厂设置",[0x05]="恢复亮度 / 对比度",[0x08]="恢复颜色设置",
        [0x0B]="色温步进",[0x0C]="请求色温",[0x10]="亮度",[0x12]="对比度",[0x14]="颜色 / 色温预设",
        [0x16]="红色增益",[0x18]="绿色增益",[0x1A]="蓝色增益",[0x52]="活动控制",[0x60]="输入源",
        [0x62]="音量",[0x6C]="红色黑电平",[0x6E]="绿色黑电平",[0x70]="蓝色黑电平",[0x72]="Gamma",
        [0x87]="锐度",[0x8D]="音频静音",[0xC0]="使用时间",[0xC8]="显示控制器类型",[0xC9]="固件级别",
        [0xCA]="OSD",[0xCC]="OSD 语言",[0xD6]="电源状态",[0xDC]="显示应用 / 画面预设",[0xDF]="MCCS 版本"
    };
    public static string Name(byte code) => Names.GetValueOrDefault(code, code >= 0xE0 ? "厂商专用（含义未知）" : "未收录 / 含义未知");
    public static string Interpret(byte code, uint value) => code switch
    {
        0x14 => value switch { 1=>"sRGB",4=>"5000K",5=>"6500K",6=>"7500K",8=>"9300K",11=>"用户自定义",_=>"取值含义待查" } + "（标准解释，厂商可能不同）",
        0x60 => value switch {15=>"DisplayPort 1",16=>"DisplayPort 2",17=>"HDMI 1",18=>"HDMI 2",_=>"厂商 / 其他输入取值"},
        0xDF => $"{value >> 8}.{value & 255}",
        _ => ""
    };
}

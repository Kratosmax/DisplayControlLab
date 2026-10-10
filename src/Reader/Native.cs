using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MonitorInspector;

// Win32 declarations checked against Microsoft win32metadata wingdi.h and Learn.
// Only GET/query/enumeration functions are imported. No setting writes exist.
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] internal struct Source { public Luid Adapter; public uint Id, Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] internal struct Target
    {
        public Luid Adapter; public uint Id, Mode; public int Technology; public uint Rotation, Scaling;
        public Rational Refresh; public uint ScanLine; public int Available; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Path { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct Mode { [FieldOffset(0)] public uint Type; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
    {
        public uint Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct Physical
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    internal delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] internal static extern int QueryDisplayConfig(uint flags, ref uint paths, [In, Out] Path[] pathArray, ref uint modes, [In, Out] Mode[] modeArray, IntPtr topology);
    [DllImport("user32.dll")] internal static extern int DisplayConfigGetDeviceInfo([In, Out] byte[] request);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplaySettingsW(string device, int mode, [In, Out] byte[] devmode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] Physical[] physical);
    [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool DestroyPhysicalMonitors(uint count, [In] Physical[] physical);
    [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);
    [DllImport("dxva2.dll", CharSet = CharSet.Ansi, SetLastError = true)] internal static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder buffer, uint length);
    [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);

    private static byte[] Request(uint type, int size, Luid adapter, uint id)
    {
        var b = new byte[size];
        BitConverter.GetBytes(type).CopyTo(b, 0); BitConverter.GetBytes(size).CopyTo(b, 4);
        BitConverter.GetBytes(adapter.Low).CopyTo(b, 8); BitConverter.GetBytes(adapter.High).CopyTo(b, 12);
        BitConverter.GetBytes(id).CopyTo(b, 16); return b;
    }
    private static string WString(byte[] b, int offset, int length) => Encoding.Unicode.GetString(b, offset, length).Split('\0')[0];

    internal static List<MonitorData> Enumerate()
    {
        var result = new List<MonitorData>();
        for (int retry = 0; retry < 3; retry++)
        {
            int status = GetDisplayConfigBufferSizes(2, out var pc, out var mc);
            if (status != 0) throw new Win32Exception(status, "读取显示器连接失败");
            if (pc > 128 || mc > 1024) throw new InvalidOperationException("显示配置数量异常");
            var paths = new Path[pc]; var modes = new Mode[mc];
            status = QueryDisplayConfig(2, ref pc, paths, ref mc, modes, IntPtr.Zero);
            if (status == 122) continue;
            if (status != 0) throw new Win32Exception(status, "读取活动显示配置失败");
            for (int i = 0; i < pc; i++)
            {
                var p = paths[i]; var m = new MonitorData();
                var source = Request(1, 84, p.Source.Adapter, p.Source.Id);
                status = DisplayConfigGetDeviceInfo(source);
                if (status == 0) m.Device = WString(source, 20, 64);
                else m.Notes.Add($"源名称读取失败：{status}");
                var target = Request(2, 420, p.Target.Adapter, p.Target.Id);
                status = DisplayConfigGetDeviceInfo(target);
                if (status == 0) { m.Name = WString(target, 36, 128); m.DevicePath = WString(target, 164, 256); }
                else m.Notes.Add($"型号读取失败：{status}");
                m.Connection = p.Target.Technology switch { 0=>"VGA / HD15",4=>"DVI",5=>"HDMI",6=>"LVDS",10=>"DisplayPort（外接）",11=>"DisplayPort（内置）",15=>"Miracast",16=>"间接有线显示",-2147483648=>"内置",_=>$"其他接口（{p.Target.Technology}）" };
                m.RefreshRate = p.Target.Refresh.Denominator > 0 ? $"{(double)p.Target.Refresh.Numerator / p.Target.Refresh.Denominator:0.###} Hz" : "未报告";
                var dm = new byte[220]; BitConverter.GetBytes((ushort)220).CopyTo(dm, 68);
                if (m.Device.Length > 0 && EnumDisplaySettingsW(m.Device, -1, dm))
                    m.Resolution = $"{BitConverter.ToUInt32(dm, 172)} × {BitConverter.ToUInt32(dm, 176)}";
                var d = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
                for (uint n = 0; EnumDisplayDevicesW(null, n, ref d, 0); n++)
                {
                    if (d.Name == m.Device) { m.Adapter = d.Description; break; }
                    d.Size = (uint)Marshal.SizeOf<DisplayDevice>();
                }
                var color = Request(15, 36, p.Target.Adapter, p.Target.Id);
                status = DisplayConfigGetDeviceInfo(color);
                if (status == 0)
                {
                    uint flags = BitConverter.ToUInt32(color, 20), mode = BitConverter.ToUInt32(color, 32);
                    m.Hdr = $"{((flags & 16) != 0 ? "系统报告支持 HDR" : "系统报告未支持 HDR")}；当前 {(mode == 2 ? "HDR" : mode == 1 ? "广色域 SDR / WCG" : "SDR")}";
                    m.Color = ColorText(color);
                }
                else
                {
                    color = Request(9, 32, p.Target.Adapter, p.Target.Id);
                    int legacy = DisplayConfigGetDeviceInfo(color);
                    if (legacy == 0)
                    {
                        uint flags = BitConverter.ToUInt32(color, 20);
                        m.Hdr = $"旧接口：高级颜色{((flags & 2) != 0 ? "开启" : "关闭")}（不能单凭此区分 HDR 与 ACM）";
                        m.Color = ColorText(color);
                    }
                    else m.Hdr = $"无法读取（新接口 {status} / 旧接口 {legacy}）";
                }
                m.Edid = ReadEdid(m.DevicePath, m.Notes);
                if (string.IsNullOrWhiteSpace(m.Name)) m.Name = m.Edid.Name.Length > 0 ? m.Edid.Name : "未识别型号";
                result.Add(m);
            }
            return result;
        }
        throw new InvalidOperationException("显示配置持续变化，请稍后刷新。");
    }
    private static string ColorText(byte[] b)
    {
        uint encoding = BitConverter.ToUInt32(b, 24), bits = BitConverter.ToUInt32(b, 28);
        return $"{encoding switch { 0=>"RGB",1=>"YCbCr 4:4:4",2=>"YCbCr 4:2:2",3=>"YCbCr 4:2:0",_=>"其他编码" }} / {bits} bit 每通道（Windows 报告）";
    }
    private static EdidData ReadEdid(string path, List<string> notes)
    {
        try
        {
            var parts = path.Split('#');
            if (parts.Length < 3 || !parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase)) return new();
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters", false);
            if (key?.GetValue("EDID") is byte[] bytes) return Parsers.Edid(bytes);
            notes.Add("该连接的注册表 EDID 未读取到。");
        }
        catch (Exception ex) { notes.Add("EDID 读取失败：" + ex.Message); }
        return new();
    }
    internal static DdcResult ReadDdc(string device, string expectedPath)
    {
        var r = new DdcResult();
        if (string.IsNullOrEmpty(expectedPath)) { r.Status = "设备身份未读取到，无法安全对应显示器；请刷新"; return r; }
        var matches = Enumerate().Where(x => x.Device == device).ToList();
        if (matches.Count != 1 || matches[0].DevicePath != expectedPath)
        { r.Status = "显示器连接已变化或处于复制模式，无法可靠对应物理显示器；请刷新"; return r; }
        IntPtr handle = IntPtr.Zero;
        MonitorCallback cb = (IntPtr h, IntPtr dc, ref Rect rect, IntPtr data) =>
        {
            var mi = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfoW(h, ref mi) && mi.Device == device) { handle = h; return false; }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        if (handle == IntPtr.Zero) { r.Status = "未找到当前显示器句柄，请刷新"; return r; }
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out var count))
        { r.Status = $"无法枚举物理显示器（{Marshal.GetLastWin32Error()}）"; return r; }
        if (count == 0 || count > 16) { r.Status = "没有可用的物理显示器句柄"; return r; }
        var physical = new Physical[count];
        if (!GetPhysicalMonitorsFromHMONITOR(handle, count, physical))
        { r.Status = $"物理显示器读取失败（{Marshal.GetLastWin32Error()}）"; return r; }
        try
        {
            for (int i = 0; i < physical.Length; i++)
            {
                var ph = physical[i];
                string label = $"{i + 1}: {ph.Description}";
                var declared = new Dictionary<byte, List<byte>>();
                if (GetCapabilitiesStringLength(ph.Handle, out var length))
                {
                    if (length is > 0 and <= 65536)
                    {
                        var sb = new StringBuilder((int)length);
                        if (CapabilitiesRequestAndCapabilitiesReply(ph.Handle, sb, length))
                        {
                            r.Capabilities.Add(sb.ToString());
                            try { declared = Parsers.Capabilities(sb.ToString()); }
                            catch (FormatException ex) { r.Notes.Add(ex.Message); }
                        }
                        else r.Notes.Add($"{label} 能力字符串读取失败：{Marshal.GetLastWin32Error()}");
                    }
                    else r.Notes.Add($"{label} 能力字符串长度异常：{length}");
                }
                else r.Notes.Add($"{label} 能力字符串长度读取失败：{Marshal.GetLastWin32Error()}");
                // Only query declared controls and a small fixed set of ordinary read-only controls.
                // Never brute-force manufacturer ranges or execute reset/save/set commands.
                byte[] common = [0x10,0x12,0x14,0x16,0x18,0x1A,0x60,0xDF];
                byte[] skip = [0x02,0x04,0x05,0x06,0x08,0xB0,0xB2];
                foreach (byte code in declared.Keys.Union(common).Order())
                {
                    var row = new FeatureRow { Code = $"0x{code:X2}", Name = VcpNames.Name(code), Declared = declared.ContainsKey(code) ? "显示器声明" : "未声明 / 常用项读取", Values = declared.TryGetValue(code, out var values) ? string.Join(", ", values.Select(x => $"0x{x:X2}")) : "", Physical = label };
                    if (skip.Contains(code)) row.Status = "仅记录声明，跳过命令类项目";
                    else if (GetVCPFeatureAndVCPFeatureReply(ph.Handle, code, out var type, out var current, out var maximum))
                    {
                        row.Status = "读取成功（未验证可写）"; row.Current = current; row.Maximum = maximum;
                        row.Interpretation = VcpNames.Interpret(code, current);
                    }
                    else { row.Error = Marshal.GetLastWin32Error(); row.Status = "未读到（不等于不支持）"; }
                    r.Features.Add(row);
                    Thread.Sleep(40);
                }
            }
            int success = r.Features.Count(x => x.Current.HasValue);
            r.Status = $"已读取 {success} 项；总计 {r.Features.Count} 项。读取成功不代表可以修改。";
            r.Notes.Add("HDR 预设没有通用映射；厂商专用码保持未知，不根据型号猜测。");
            if (success == 0) r.Notes.Add("请检查显示器 DDC/CI 开关、连接、休眠与 HDR 锁定；本工具无法仅凭失败确定原因。");
        }
        finally
        {
            if (!DestroyPhysicalMonitors(count, physical)) r.Notes.Add($"释放物理显示器句柄失败：{Marshal.GetLastWin32Error()}");
        }
        return r;
    }
}

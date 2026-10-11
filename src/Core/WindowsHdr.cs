using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MonitorInspector;
public static partial class Native
{
    // Microsoft win32metadata wingdi.h: DISPLAYCONFIG_SET_HDR_STATE,
    // DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE=16, 20-byte header + UINT32 flags.
    [DllImport("user32.dll")] private static extern int DisplayConfigSetDeviceInfo([In] byte[] request);
    internal static byte[] HdrPacket(uint adapterLow, int adapterHigh, uint targetId, bool enabled)
    {
        var packet = Request(16, 24, new Luid { Low = adapterLow, High = adapterHigh }, targetId);
        BitConverter.GetBytes(enabled ? 1u : 0u).CopyTo(packet, 20); return packet;
    }
    public static void SetWindowsHdr(MonitorData monitor, bool enabled)
    {
        var now = Enumerate().SingleOrDefault(d => HardwareReports.SameDevice(d, monitor)) ?? throw new IOException("显示连接已改变");
        if (!now.CanSetWindowsHdr) throw new NotSupportedException("HDR 专用 API 不可用；不会用广色域开关冒充 HDR");
        int status = GetDisplayConfigBufferSizes(2, out uint pc, out uint mc);
        if (status != 0 || pc > 128 || mc > 1024) throw new Win32Exception(status, "显示配置读取失败");
        var paths = new Path[pc]; var modes = new Mode[mc]; status = QueryDisplayConfig(2, ref pc, paths, ref mc, modes, IntPtr.Zero);
        if (status != 0) throw new Win32Exception(status);
        var matches = new List<Target>();
        foreach (var path in paths.Take((int)pc)) {
            var name = Request(2, 420, path.Target.Adapter, path.Target.Id);
            if (DisplayConfigGetDeviceInfo(name) == 0 && WString(name, 164, 256) == monitor.DevicePath) matches.Add(path.Target);
        }
        if (matches.Count != 1) throw new IOException("HDR 目标无法唯一匹配");
        var target = matches[0]; status = DisplayConfigSetDeviceInfo(HdrPacket(target.Adapter.Low, target.Adapter.High, target.Id, enabled));
        if (status != 0) throw new Win32Exception(status, "Windows HDR 设置失败");
    }
}

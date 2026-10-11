namespace MonitorInspector;
public static class HardwareReports
{
    public static bool SameDevice(MonitorData a, MonitorData b) => a.DevicePath == b.DevicePath && a.Device == b.Device && a.Edid.Manufacturer == b.Edid.Manufacturer && a.Edid.Product == b.Edid.Product && a.Edid.Serial == b.Edid.Serial;
    public static List<MonitorData> Devices() => Native.Enumerate();
    public static DdcResult Scan(string device, string expectedPath, bool expanded) => Native.ReadDdc(device, expectedPath, expanded);
}

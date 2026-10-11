namespace MonitorInspector;
public static partial class DeviceControl
{
    public static Snapshot ReadBatch(BatchReadRequest request)
    {
        if (request.Scope?.Count > 128) throw new InvalidDataException("读取范围过大");
        var device = Native.Enumerate().SingleOrDefault(d => HardwareReports.SameDevice(d, request.Device)) ?? throw new IOException("显示器连接变化");
        var catalogue = CapabilityCatalog.Resolve(device); var snapshot = new Snapshot { Device = device };
        foreach (var c in catalogue.Where(c => request.Scope == null || request.Scope.Contains(c.Definition.Id))) {
            ControlResult result;
            if (c.Problem.Length > 0) result = new() { Status = c.Availability, Message = c.Problem };
            else if (!c.Definition.Readable || c.Definition.ValueType == "action") result = new() { Status = "unsupported", Message = "仅写入或执行" };
            else result = Execute(DisplayWorkspace.Request(device, c, "read"), device, catalogue, retryStandardRead: c.Definition.Verification is "read-observed" or "write-confirmed");
            snapshot.Readings.Add(new() { Capability = c, Result = result });
        }
        var after = Native.Enumerate().SingleOrDefault(d => HardwareReports.SameDevice(d, device)) ?? throw new IOException("读取期间连接变化");
        if (after.WindowsHdrEnabled != device.WindowsHdrEnabled) throw new IOException("读取期间系统 HDR 已改变，稍后重读");
        return snapshot;
    }
}

using System.Runtime.InteropServices;

namespace MonitorInspector;

// ADL structures and transport: AMD GPUOpen display-library include/adl_structures.h
// and Sample/DDCBlockAccess. Protocol payloads are supplied by installed packages.
internal static class AdlTransport
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct Adapter
    {
        public int Size, Index;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Udid;
        public int Bus, Device, Function, Vendor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string WindowsDisplay;
        public int Present, Exists;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Driver;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Pnp;
        public int OsIndex;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayId { public int Logical, Physical, LogicalAdapter, PhysicalAdapter; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct Display
    {
        public DisplayId Id;
        public int Controller;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Manufacturer;
        public int Type, Output, Connector, Mask, Value;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr Allocate(int size);
    private static readonly Allocate Allocator = Marshal.AllocHGlobal;
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Main_Control_Create(Allocate allocate, int connectedOnly, out IntPtr context);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Main_Control_Destroy(IntPtr context);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int count);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, [In, Out] Adapter[] adapters, int size);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Display_DisplayInfo_Get(IntPtr context, int adapter, out int count, out IntPtr displays, int force);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll")] private static extern int ADL2_Display_DDCBlockAccess_Get(IntPtr context, int adapter, int display, int option, int command, int sendSize, byte[] send, ref int receiveSize, [Out] byte[]? receive);

    internal static byte[] Exchange(MonitorData monitor, byte[] packet, int replyLength)
    {
        IntPtr context = IntPtr.Zero;
        void Check(int status, string stage)
        {
            if (status == 0) return;
            throw new IOException($"{stage}失败（AMD ADL {status}）");
        }
        try
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new NotSupportedException("此通信通道需要 Windows x64 AMD 驱动");
            Check(ADL2_Main_Control_Create(Allocator, 1, out context), "初始化");
            Check(ADL2_Adapter_NumberOfAdapters_Get(context, out int count), "枚举显卡");
            if (count is <= 0 or > 128) throw new InvalidDataException("显卡数量异常");
            var adapters = new Adapter[count];
            for (int i = 0; i < count; i++) adapters[i].Size = Marshal.SizeOf<Adapter>();
            Check(ADL2_Adapter_AdapterInfo_Get(context, adapters, Marshal.SizeOf<Adapter>() * count), "读取显卡资料");
            var targets = new Dictionary<(int Adapter, int Display), DisplayId>();
            foreach (var adapter in adapters.Where(x => x.Present != 0))
            {
                IntPtr displays = IntPtr.Zero;
                try
                {
                    int status = ADL2_Display_DisplayInfo_Get(context, adapter.Index, out int displayCount, out displays, 0);
                    if (status != 0) continue;
                    if (displayCount is < 0 or > 128 || (displayCount > 0 && displays == IntPtr.Zero)) throw new InvalidDataException("显示器列表异常");
                    for (int i = 0; i < displayCount; i++)
                    {
                        var display = Marshal.PtrToStructure<Display>(IntPtr.Add(displays, i * Marshal.SizeOf<Display>()));
                        if ((display.Value & 3) == 3 && display.Name == monitor.Name)
                            targets[(display.Id.PhysicalAdapter, display.Id.Physical)] = display.Id;
                    }
                }
                finally { if (displays != IntPtr.Zero) Marshal.FreeHGlobal(displays); }
            }
            if (targets.Count != 1) throw new InvalidOperationException("AMD 设备身份不能唯一匹配，停止通信");
            var target = targets.Values.Single();
            if (!adapters.Any(x => x.Index == target.LogicalAdapter && x.WindowsDisplay == monitor.Device))
                throw new InvalidOperationException("AMD 连接与所选 Windows 显示器不一致");
            int size = 0;
            Check(ADL2_Display_DDCBlockAccess_Get(context, target.LogicalAdapter, target.Logical, 0, 0, packet.Length, packet, ref size, null), "发送 DDC 请求");
            if (replyLength == 0) return [];
            Thread.Sleep(100);
            byte[] reply = new byte[Math.Max(32, replyLength)]; size = replyLength;
            Check(ADL2_Display_DDCBlockAccess_Get(context, target.LogicalAdapter, target.Logical, 0, 0, 1, [0x6F], ref size, reply), "接收 DDC 响应");
            if (size != replyLength) throw new InvalidDataException("DDC 响应长度不符");
            return reply[..size];
        }
        finally
        {
            if (context != IntPtr.Zero) ADL2_Main_Control_Destroy(context);
            GC.KeepAlive(Allocator);
        }
    }
}

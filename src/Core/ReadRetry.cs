using System.ComponentModel;

namespace MonitorInspector;

public static partial class DeviceControl
{
    // Retry transport GET failures only. Never repeat SET or hide invalid data.
    internal static T ReadWithRetry<T>(Func<T> read, Action<int>? wait = null)
    {
        wait ??= Thread.Sleep;
        for (int attempt = 0; ; attempt++) {
            try { return read(); }
            catch (Win32Exception) when (attempt < 2) { wait(attempt == 0 ? 120 : 250); }
        }
    }
}

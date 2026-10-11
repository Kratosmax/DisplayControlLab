using System.ComponentModel;

namespace MonitorInspector;
public static partial class DeviceControl
{
    // Only the built-in absolute brightness command may be repeated, and only
    // after a failed SET plus a verified unchanged value/range and fresh safety checks.
    internal static (uint Current, uint Maximum) WriteBrightness(
        uint desired, (uint Current, uint Maximum) before, Action<uint> write,
        Func<(uint Current, uint Maximum)> read, Action validate,
        Action<Win32Exception> failed, Action<int>? wait = null)
    {
        wait ??= Thread.Sleep;
        if (desired == before.Current) { validate(); return before; }
        for (int attempt = 0; ; attempt++) {
            wait(attempt == 0 ? 80 : 180);
            validate();
            bool rejected = false;
            try { write(desired); }
            catch (Win32Exception ex) { failed(ex); rejected = true; }
            wait(180); validate();
            var after = read();
            foreach (int delay in new[] { 120, 200 }) {
                if (after.Current == desired || after.Maximum != before.Maximum || after.Current != before.Current) break;
                wait(delay); validate(); after = read();
            }
            if (!rejected || attempt >= 1 || after.Current == desired || after != before) return after;
        }
    }
}

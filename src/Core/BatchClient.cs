using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MonitorInspector;
public interface IBatchDisplayBackend
{
    Task<Snapshot> ReadBatch(MonitorData device, List<string>? scope, CancellationToken token);
}
public sealed class BatchReadRequest
{
    public MonitorData Device { get; set; } = new();
    public List<string>? Scope { get; set; }
}
public static class BatchClient
{
    public static async Task<Snapshot> Read(string executable, MonitorData device, List<string>? scope, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("--batch-worker"); using var p = Process.Start(start)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(60000);
        async Task<string> Bounded(StreamReader stream) { var text = new StringBuilder(); char[] buffer = new char[4096]; int n; while ((n = await stream.ReadAsync(buffer.AsMemory(), deadline.Token)) != 0) { if (text.Length + n > 1024 * 1024) throw new InvalidDataException("读取结果超过 1 MB"); text.Append(buffer, 0, n); } return text.ToString(); }
        try {
            var output = Bounded(p.StandardOutput); var error = Bounded(p.StandardError);
            await p.StandardInput.WriteAsync(JsonSerializer.Serialize(new BatchReadRequest { Device = device, Scope = scope }, Packages.Json).AsMemory(), deadline.Token); p.StandardInput.Close();
            await Task.WhenAll(output, error, p.WaitForExitAsync(deadline.Token));
            if (p.ExitCode != 0) throw new IOException((await error)[..Math.Min(500, (await error).Length)]);
            var snapshot = JsonSerializer.Deserialize<Snapshot>(await output, Packages.Json) ?? throw new IOException("读取结果为空");
            if (!HardwareReports.SameDevice(snapshot.Device, device)) throw new IOException("读取设备身份变化");
            return snapshot;
        } finally { if (!p.HasExited) { p.Kill(true); await p.WaitForExitAsync(); } }
    }
}

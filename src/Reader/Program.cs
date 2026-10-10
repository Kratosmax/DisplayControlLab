using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MonitorInspector;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal static AuditLog? Log;
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--ddc")
            {
                var result = Native.ReadDdc(args[1], args[2]);
                using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                output.Write(JsonSerializer.Serialize(result, Json)); return 0;
            }
            if (args.Length == 2 && args[0] == "--report")
            { File.WriteAllText(args[1], JsonSerializer.Serialize(Native.Enumerate(), Json)); return 0; }
            if (args.Length == 3 && args[0] == "--scan-report")
            {
                var monitors = Native.Enumerate();
                int index = int.Parse(args[1]) - 1;
                if (index < 0 || index >= monitors.Count) throw new ArgumentException("显示器序号超出范围");
                var monitor = monitors[index];
                monitor.Ddc = Scan(monitor, CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllText(args[2], JsonSerializer.Serialize(monitor, Json));
                ObservationStore.Save(Path.Combine(AppContext.BaseDirectory, "observations"), monitor);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--self-test")
            { File.WriteAllText(args[1], Tests.Run()); return 0; }
            if (args.Length > 0 && !args.Contains("--capture")) throw new ArgumentException("未知启动参数");
            using var log = args.Contains("--capture") ? null : new AuditLog(Path.Combine(AppContext.BaseDirectory, "logs", "current.jsonl"));
            Log = log;
            var app = new Application();
            app.DispatcherUnhandledException += (_, e) => { Log?.Write("ui_error", new { error = e.Exception.ToString() }); MessageBox.Show(e.Exception.Message, "显示器查看器"); e.Handled = true; };
            app.Run(new MainWindow(args)); return 0;
        }
        catch (Exception ex)
        {
            if (args.Length == 2 && args[0] is "--report" or "--self-test") File.WriteAllText(args[1] + ".error.txt", ex.ToString());
            if (args.Length > 0) { using var error = new StreamWriter(Console.OpenStandardError()); error.Write(ex); }
            else MessageBox.Show(ex.Message, "显示器查看器启动失败");
            return 1;
        }
    }
    internal static async Task<DdcResult> Scan(MonitorData monitor, CancellationToken token)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("--ddc"); start.ArgumentList.Add(monitor.Device); start.ArgumentList.Add(monitor.DevicePath);
        using var process = Process.Start(start) ?? throw new IOException("无法启动读取进程");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string output = await stdout, error = await stderr;
            if (process.ExitCode != 0) return new() { Status = "读取进程失败", Notes = [error.Length > 2000 ? error[..2000] : error] };
            return JsonSerializer.Deserialize<DdcResult>(output) ?? new() { Status = "没有返回读取结果" };
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return new() { Status = token.IsCancellationRequested ? "读取已取消" : "读取超过 60 秒，已停止；不能据此认定显示器不支持" };
        }
    }
}

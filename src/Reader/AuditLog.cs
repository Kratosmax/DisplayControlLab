using System.Text;
using System.Text.Json;

namespace MonitorInspector;

internal sealed class AuditLog : IDisposable
{
    private readonly StreamWriter writer;
    internal string FilePath { get; }
    internal string SessionId { get; } = Guid.NewGuid().ToString("N");
    internal AuditLog(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // A second process cannot truncate a live session's log. Workers never open it.
        writer = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        Write("session_started", new { version = typeof(AuditLog).Assembly.GetName().Version?.ToString(), process = Environment.ProcessId, os = Environment.OSVersion.VersionString });
    }
    internal void Write(string kind, object data)
    {
        writer.WriteLine(JsonSerializer.Serialize(new { time = DateTimeOffset.Now, session = SessionId, kind, data }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
    public void Dispose() { Write("session_ended", new { }); writer.Dispose(); }
}

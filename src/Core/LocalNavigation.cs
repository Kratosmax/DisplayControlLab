using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace MonitorInspector;

// Ownership is a held file handle, so crashes release it without deleting user data.
// Only navigation ids are accepted; no paths or commands are transported.
public sealed class LocalNavigation : IDisposable
{
    readonly FileStream? ownership;
    readonly CancellationTokenSource stop = new();
    readonly string pipe;
    public bool IsOwner => ownership != null;
    public LocalNavigation(string identity, Action<string> receive)
    {
        pipe = "DisplayControl-" + identity;
        string filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        try { ownership = new FileStream(Path.Combine(Library.Folder("instances"), filename + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return; }
        _ = Task.Run(async () => {
            while (!stop.IsCancellationRequested) {
                try {
                    using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(2000);
                    using var reader = new StreamReader(server); var buffer = new char[65]; var count = 0;
                    while (count < buffer.Length) { int read = await reader.ReadAsync(buffer.AsMemory(count, 1), timeout.Token); if (read == 0 || buffer[count] == '\n') break; count += read; }
                    string id = new string(buffer, 0, count).Trim(); if (id == "show" || Guid.TryParseExact(id, "N", out _)) receive(id);
                } catch (OperationCanceledException) { if (stop.IsCancellationRequested) break; }
                catch (IOException) { }
            }
        });
    }
    public void Forward(string context)
    {
        if (context != "show" && !Guid.TryParseExact(context, "N", out _)) throw new InvalidDataException("导航 id 无效");
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly); client.Connect(3000); using var writer = new StreamWriter(client); writer.WriteLine(context);
    }
    public void Dispose() { stop.Cancel(); ownership?.Dispose(); }
}

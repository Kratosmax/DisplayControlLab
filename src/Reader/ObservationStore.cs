using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MonitorInspector;

internal static class ObservationStore
{
    internal static string Save(string directory, MonitorData monitor)
    {
        Directory.CreateDirectory(directory);
        // Keep connections separate until cross-connector identity is verified.
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(monitor.DevicePath)))[..16];
        string path = Path.Combine(directory, $"{key}.json");
        var document = new
        {
            schemaVersion = 1, kind = "observation", capturedAt = DateTimeOffset.Now,
            identity = new { manufacturer = monitor.Edid.Manufacturer, productCode = monitor.Edid.Product, serial = monitor.Edid.Serial, name = monitor.Name, devicePath = monitor.DevicePath },
            connection = new { monitor.Connection, monitor.Adapter },
            controls = (monitor.Ddc?.Features ?? []).Where(row => row.Current.HasValue).Select(row => new
            {
                code = row.Code, name = row.Name, meaningSource = row.Name.Contains("未知") ? "unknown" : "standard-reference",
                verification = "observed", physical = row.Physical, current = row.Current, maximum = row.Maximum,
                declaredValues = row.Values, readStatus = row.Status, error = row.Error
            }),
            changes = monitor.Changes, raw = monitor
        };
        string staging = path + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(document, Program.Json), new UTF8Encoding(false));
        File.Move(staging, path, overwrite: true);
        return path;
    }
}

using System.Text;
using System.Text.Json;

// Replace these fixture branches with documented vendor SDK calls.
// This executable never accesses real hardware.
Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(false);
string text = Console.In.ReadToEnd(); if (text.Length > 1024 * 1024) return 2;
using var json = JsonDocument.Parse(text); var request = json.RootElement;
string id = request.GetProperty("requestId").GetString()!;
string action = request.GetProperty("action").GetString()!;
string feature = request.GetProperty("controlId").GetString()!;
if (request.GetProperty("apiVersion").GetInt32() != 1) return 2;
if (action is "probe" or "describe") { Console.Write(JsonSerializer.Serialize(new { apiVersion = 1, requestId = id, status = "ok", message = "模拟后端可运行；没有连接真实驱动", text = "simulation only" })); return 0; }
if (action is not ("read" or "write")) return 2;
bool write = action == "write";
if (feature == "simulated-status") {
    Console.Write(JsonSerializer.Serialize(new { apiVersion = 1, requestId = id, status = "ok", message = "只读文本（模拟）", text = "USB endpoint ready" })); return 0;
}
if (feature is "simulated-action" or "simulated-write-only") {
    Console.Write(JsonSerializer.Serialize(new { apiVersion = 1, requestId = id, status = write ? "ok" : "unsupported", message = write ? "已发送（模拟），无回读，不能确认效果" : "该功能不提供读取", sent = write, confirmed = false })); return 0;
}
uint current = 42;
if (write && (!uint.TryParse(request.GetProperty("value").GetString(), out current) || current > 100)) {
    Console.Write(JsonSerializer.Serialize(new { apiVersion = 1, requestId = id, status = "error", message = "示例值必须为 0–100" })); return 0;
}
Console.Write(JsonSerializer.Serialize(new { apiVersion = 1, requestId = id, status = "ok", message = "模拟插件；未操作真实设备", current, maximum = 100, before = 42, sent = write, confirmed = write, raw = "simulation" }));
return 0;

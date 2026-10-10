using System.Text.Json;

// Stage one: inspect the shared contract only. No monitor write APIs are linked.
if (args.Length != 2 || args[0] != "--inspect")
{
    Console.WriteLine("DisplayControl 控制器骨架 0.2\n当前阶段仅检查配置，不发送显示器控制命令。\n用法：DisplayControl.Controller.exe --inspect observations/文件.json");
    return args.Length == 0 ? 0 : 2;
}
try
{
    if (new FileInfo(args[1]).Length > 4 * 1024 * 1024) throw new InvalidDataException("配置超过 4 MB");
    using var json = JsonDocument.Parse(File.ReadAllText(args[1]));
    var root = json.RootElement;
    if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("配置版本不支持");
    string? kind = root.GetProperty("kind").GetString();
    if (kind is not ("observation" or "profile")) throw new InvalidDataException("配置类型无效");
    var identity = root.GetProperty("identity");
    foreach (string field in new[] { "manufacturer", "productCode", "serial", "name", "devicePath" })
        if (identity.GetProperty(field).ValueKind != JsonValueKind.String) throw new InvalidDataException("设备身份字段无效：" + field);
    Console.WriteLine($"显示器：{identity.GetProperty("name").GetString()}\n资料类型：{kind}");
    int observed = 0, verified = 0;
    foreach (var control in root.GetProperty("controls").EnumerateArray())
    {
        string code = control.GetProperty("code").GetString() ?? "";
        if (code.Length != 4 || !code.StartsWith("0x") || !byte.TryParse(code.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out _)) throw new InvalidDataException("控制码格式错误：" + code);
        string? state = control.GetProperty("verification").GetString();
        if (state is not ("observed" or "candidate" or "verified")) throw new InvalidDataException("验证状态无效");
        if (kind == "observation" && state == "verified") throw new InvalidDataException("读取记录不能自行声明写入已验证");
        if (state == "verified") verified++; else observed++;
        Console.WriteLine($"{code}  {control.GetProperty("name").GetString()}  [{state}]");
    }
    Console.WriteLine($"已读取 / 候选 {observed} 项，标记已验证 {verified} 项。骨架阶段不执行任何写入；标记已验证也不代表已经核验其证据。");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("配置检查失败：" + ex.Message); return 1; }

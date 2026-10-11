using System.Security.Cryptography;
using System.Text.Json;
using MonitorInspector.Updates;

if (args.Length != 2) throw new ArgumentException("使用：ReleaseTools 产物目录 当前版本说明文件");
string folder = Path.GetFullPath(args[0]), notes = File.ReadAllText(args[1]);
string key = Environment.GetEnvironmentVariable("DISPLAYCONTROL_SIGNING_KEY") ?? throw new InvalidOperationException("缺少受控签名私钥");
foreach (string channel in new[] { "Lite", "Full" }) {
    string version = UpdateIdentity.CurrentVersion.ToString(3); string name = $"DisplayControlLab-{version}-{channel}.zip";
    var path = Path.Combine(folder, name); var file = new FileInfo(path);
    var unsigned = new UpdateManifest { Version = version, Channel = channel, DownloadUrl = UpdateTrust.Repository + "/releases/download/v" + version + "/" + name,
        Size = file.Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), ReleaseNotes = notes };
    string signature = UpdateManifestCodec.Sign(unsigned, key);
    var signed = new UpdateManifest { Version = unsigned.Version, Channel = channel, DownloadUrl = unsigned.DownloadUrl, Size = unsigned.Size, Sha256 = unsigned.Sha256, ReleaseNotes = notes, Signature = signature };
    string json = UpdateManifestCodec.Serialize(signed);
    var info = UpdateManifestCodec.ParseAndVerify(json, UpdateTrust.PublicKeyPem, channel);
    await UpdatePackageValidator.ValidateAsync(path, info);
    File.WriteAllText(Path.Combine(folder, UpdateTrust.ManifestName(channel)), json);
    if (channel == "Lite") File.WriteAllText(Path.Combine(folder, "update.json"), json);
}
string releasePrefix = "DisplayControlLab-" + UpdateIdentity.CurrentVersion.ToString(3) + "-";
var sums = Directory.GetFiles(folder).Where(p => Path.GetFileName(p).StartsWith(releasePrefix, StringComparison.Ordinal) && Path.GetExtension(p) is ".exe" or ".zip" || Path.GetFileName(p) is "update.json" or "update-lite.json" or "update-full.json").OrderBy(p => p).Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant() + "  " + Path.GetFileName(p));
File.WriteAllLines(Path.Combine(folder, "SHA256SUMS.txt"), sums);
Console.WriteLine("两通道清单签名及实际ZIP预检完成；已生成兼容清单与SHA256。");

using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonitorInspector.Updates;

public sealed class UpdatePackageMetadata
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("productId")]
    public string ProductId { get; init; } = UpdateTrust.ProductId;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public List<string> Files { get; init; } = [];

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "Lite";
}

public sealed record ValidatedPackage(UpdatePackageMetadata Metadata, IReadOnlyList<string> Files);

public static class UpdatePackageValidator
{
    private const int MaximumEntries = 4096;
    private const long MaximumExpandedSize = 500L * 1024 * 1024;
    private const int MaximumMetadataSize = 64 * 1024;
    private static readonly string[] CommonRequiredEntries =
    [
        "DisplayControl.Controller.exe",
        "DisplayControl.Controller.dll",
        "DisplayControl.Reader.exe",
        "DisplayControl.Reader.dll",
        "DisplayControl.Core.dll",
        "DisplayControl.UI.dll",
        "DisplayControl.Updater.exe",
        "update-install.json",
        "update-package.json"
    ];

    private static readonly string[] LiteUpdaterEntries =
    [
        "DisplayControl.Updater.dll",
        "DisplayControl.Updater.deps.json",
        "DisplayControl.Updater.runtimeconfig.json"
    ];

    public static async Task<ValidatedPackage> ValidateAsync(
        string packagePath,
        UpdateInfo update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(update);

        var file = new FileInfo(packagePath);
        if (!file.Exists || file.Length != update.Size)
        {
            throw new InvalidDataException("更新包大小与清单不一致。");
        }

        await using (var stream = file.OpenRead())
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException("更新包 SHA-256 校验失败。");
            }
        }

        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("更新包文件数量超出允许范围。");
        }

        long expandedSize = 0;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0) throw new InvalidDataException("更新包包含链接");
            var relativePath = NormalizeRelativePath(entry.FullName);
            expandedSize = checked(expandedSize + entry.Length);
            if (expandedSize > MaximumExpandedSize)
            {
                throw new InvalidDataException("更新包解压后大小超出允许范围。");
            }
            if (!files.Add(relativePath))
            {
                throw new InvalidDataException($"更新包包含重复路径：{relativePath}");
            }
        }

        foreach (var required in CommonRequiredEntries)
        {
            if (!files.Contains(required))
            {
                throw new InvalidDataException($"更新包缺少必需文件：{required}");
            }
        }

        if (update.Channel == "Lite")
        {
            foreach (var required in LiteUpdaterEntries)
            {
                if (!files.Contains(required))
                {
                    throw new InvalidDataException($"轻量更新包缺少必需文件：{required}");
                }
            }
        }
        else if (update.Channel != "Full")
        {
            throw new InvalidDataException("更新包使用了不支持的通道。");
        }

        var metadata = await ReadMetadataAsync(archive, cancellationToken).ConfigureAwait(false);
        if (metadata.SchemaVersion != 1 ||
            metadata.ProductId != UpdateTrust.ProductId ||
            metadata.Channel != update.Channel ||
            !Version.TryParse(metadata.Version, out var packageVersion) ||
            packageVersion != update.Version)
        {
            throw new InvalidDataException("更新包元数据与清单不匹配。");
        }

        if (metadata.Files.Count > 0 && !metadata.Files.Select(NormalizeRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(archive.Entries.Where(e => e.Name.Length > 0).Select(e => NormalizeRelativePath(e.FullName)))) throw new InvalidDataException("软件文件清单与ZIP内容不符");
        var installEntry = archive.GetEntry("update-install.json")!;
        if (installEntry.Length > MaximumMetadataSize) throw new InvalidDataException("安装身份过大");
        using (var identity = installEntry.Open()) { var marker = JsonSerializer.Deserialize<UpdatePackageMetadata>(identity); if (marker?.ProductId != metadata.ProductId || marker.Channel != metadata.Channel || marker.Version != metadata.Version) throw new InvalidDataException("包内安装身份不一致"); }
        foreach (var assemblyName in new[] { "DisplayControl.Controller.dll", "DisplayControl.Reader.dll", "DisplayControl.Updater.dll" })
        {
        var assemblyEntry = archive.GetEntry(assemblyName)
            ?? throw new InvalidDataException("更新包缺少 DisplayControl.Controller.dll。");
        const int maximumAssemblySize = 100 * 1024 * 1024;
        if (assemblyEntry.Length <= 0 || assemblyEntry.Length > maximumAssemblySize)
        {
            throw new InvalidDataException("更新包入口程序集大小无效。");
        }
        await using var assemblyStream = assemblyEntry.Open();
        using var seekableAssembly = new MemoryStream((int)assemblyEntry.Length);
        await BoundedStream.CopyToAsync(assemblyStream, seekableAssembly, maximumAssemblySize, cancellationToken)
            .ConfigureAwait(false);
        seekableAssembly.Position = 0;
        using var peReader = new PEReader(seekableAssembly, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
        {
            throw new InvalidDataException("更新包入口程序集没有有效元数据。");
        }
        var assemblyVersion = peReader.GetMetadataReader().GetAssemblyDefinition().Version;
        if (NormalizeVersion(assemblyVersion) != NormalizeVersion(update.Version))
        {
            throw new InvalidDataException("更新包入口程序集版本与清单不匹配。");
        }

        }
        return new ValidatedPackage(metadata, files.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static string NormalizeRelativePath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("更新包包含空路径。");
        }

        var normalized = entryName.Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }
        if (normalized.StartsWith('/') ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') || segment.IndexOfAny(['<','>','"','|','?','*']) >= 0 || System.Text.RegularExpressions.Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ||
            normalized.Split('/')[0].Equals(".displaycontrol-update", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"更新包包含不安全路径：{entryName}");
        }
        return normalized;
    }

    private static async Task<UpdatePackageMetadata> ReadMetadataAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("update-package.json")
            ?? throw new InvalidDataException("更新包缺少元数据。");
        if (entry.Length <= 0 || entry.Length > MaximumMetadataSize)
        {
            throw new InvalidDataException("更新包元数据大小无效。");
        }
        await using var stream = entry.Open();
        using var memory = new MemoryStream((int)entry.Length);
        await BoundedStream.CopyToAsync(stream, memory, MaximumMetadataSize, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;
        return await JsonSerializer.DeserializeAsync<UpdatePackageMetadata>(memory, cancellationToken: cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("更新包元数据为空。");
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));
}

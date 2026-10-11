using System.Net;
using System.Net.Http;
using System.Text;
using System.Security.Cryptography;

namespace MonitorInspector.Updates;

public sealed record UpdateRoute(string BaseUrl, int Priority, bool IsDirect = false);
public sealed class UpdateSettings
{
    public string Id { get; set; } = "33333333333333333333333333333333";
    public bool CheckOnStartup { get; set; } = true;
    public string SkippedVersion { get; set; } = "";
    public string? HttpProxy { get; set; }
    public List<UpdateRoute> Routes { get; set; } = [new("", 10, true)];
    public UpdateSettings Normalize()
    {
        var copy = Library.Copy(this); copy.Routes = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); bool direct = false;
        foreach (var route in Routes) {
            if (route.Priority is < 0 or > 10) throw new ArgumentException("线路优先级必须为 0–10");
            if (route.IsDirect) { if (!direct) copy.Routes.Add(new("", route.Priority, true)); direct = true; continue; }
            if (!Uri.TryCreate(route.BaseUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new ArgumentException("GitHub 前缀地址必须是 HTTP(S) 地址，不含账号、查询或片段");
            string prefix = uri.AbsoluteUri.TrimEnd('/');
            if (seen.Add(prefix)) copy.Routes.Add(new(prefix, route.Priority));
        }
        if (!direct) copy.Routes.Insert(0, new("", copy.Routes.Count == 0 ? 10 : 1, true));
        if (copy.Routes.All(r => r.Priority == 0)) throw new ArgumentException("至少启用一条更新线路");
        copy.HttpProxy = null;
        if (!string.IsNullOrWhiteSpace(HttpProxy)) {
            if (!Uri.TryCreate(HttpProxy.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new ArgumentException("HTTP 网络代理仅接受 http://主机:端口，不支持账号、路径或 SOCKS");
            copy.HttpProxy = uri.GetLeftPart(UriPartial.Authority);
        }
        return copy;
    }
    public static UpdateSettings Load() => (Library.All<UpdateSettings>("update-settings").FirstOrDefault() ?? new()).Normalize();
    public void Save() => Library.Save("update-settings", Id, Normalize());
    public IEnumerable<(Uri Uri, string Name, bool Direct)> Candidates(Uri original)
    {
        var settings = Normalize();
        if (original.Host != "github.com") return [(original, original.Host, true)];
        return settings.Routes.Where(r => r.Priority > 0).OrderByDescending(r => r.Priority)
            .Select(r => (r.IsDirect ? original : new Uri(r.BaseUrl + "/" + original.AbsoluteUri), r.IsDirect ? "GitHub 直连" : new Uri(r.BaseUrl).Host, r.IsDirect));
    }
}

public sealed class UpdateNetwork : IDisposable
{
    readonly UpdateSettings settings;
    readonly HttpClient client;
    static readonly HashSet<string> GitHubHosts = new(StringComparer.OrdinalIgnoreCase) { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };
    public UpdateNetwork(UpdateSettings settings, HttpMessageHandler? testHandler = null)
    {
        this.settings = settings.Normalize();
        client = new HttpClient(testHandler ?? new SocketsHttpHandler {
            AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10), UseProxy = settings.HttpProxy != null,
            Proxy = settings.HttpProxy == null ? null : new WebProxy(settings.HttpProxy), PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DisplayControlLab/" + UpdateIdentity.CurrentVersion);
    }
    async Task<HttpResponseMessage> Open(Uri uri, bool direct, CancellationToken token)
    {
        var first = uri;
        for (int redirects = 0; redirects <= 5; redirects++) {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null) {
                var next = new Uri(uri, response.Headers.Location); response.Dispose();
                bool allowed = direct ? next.Scheme == "https" && next.IsDefaultPort && GitHubHosts.Contains(next.Host)
                    : next.Scheme == first.Scheme && next.Authority == first.Authority;
                if (!allowed || next.UserInfo.Length > 0) throw new InvalidDataException("更新线路重定向到未允许的地址");
                uri = next; continue;
            }
            if (!response.IsSuccessStatusCode) { var code = response.StatusCode; response.Dispose(); throw new HttpRequestException("HTTP " + (int)code, null, code); }
            return response;
        }
        throw new InvalidDataException("更新重定向次数过多");
    }
    static async Task<long> Copy(Stream source, Stream destination, long limit, Action<long>? received, CancellationToken token)
    {
        byte[] buffer = new byte[65536]; long count = 0;
        while (true) {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(token); stall.CancelAfter(TimeSpan.FromSeconds(20));
            int read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false); if (read == 0) return count;
            count += read; if (count > limit) throw new InvalidDataException("下载超过允许大小");
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); received?.Invoke(count);
        }
    }
    public async Task<T> Fetch<T>(Uri original, long limit, Func<string, T> verify, CancellationToken token)
    {
        var errors = new List<string>();
        foreach (var route in settings.Candidates(original)) {
            try {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var response = await Open(route.Uri, route.Direct, timeout.Token).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("更新清单过大");
                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false); using var memory = new MemoryStream();
                await Copy(source, memory, limit, null, timeout.Token).ConfigureAwait(false);
                return verify(Encoding.UTF8.GetString(memory.ToArray()));
            } catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or CryptographicException or System.Text.Json.JsonException or FormatException) { errors.Add(route.Name + "：" + SafeError(ex)); }
        }
        throw new IOException("所有更新线路均失败。" + string.Join("；", errors));
    }
    public async Task<string> Download(UpdateInfo info, string folder, IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(folder); string temporary = Path.Combine(folder, "package.download"), final = Path.Combine(folder, "package.zip");
        var errors = new List<string>();
        foreach (var route in settings.Candidates(info.DownloadUri)) {
            try {
                File.Delete(temporary); progress?.Report(new("连接线路", route.Name, null));
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var response = await Open(route.Uri, route.Direct, timeout.Token).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is long length && length != info.Size) throw new InvalidDataException("服务器包大小与签名清单不符");
                await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) {
                    long total = await Copy(source, target, info.Size, n => progress?.Report(new("下载", route.Name, Math.Min(99, (int)(n * 100 / info.Size)))), token).ConfigureAwait(false);
                    if (total != info.Size) throw new InvalidDataException("下载未完成，尚未修改安装目录");
                }
                progress?.Report(new("验证签名、哈希与包结构", route.Name, 99));
                await UpdatePackageValidator.ValidateAsync(temporary, info, token).ConfigureAwait(false);
                File.Move(temporary, final, true); progress?.Report(new("准备安装", route.Name, 100)); return final;
            } catch (OperationCanceledException) when (token.IsCancellationRequested) { File.Delete(temporary); throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or CryptographicException) { File.Delete(temporary); errors.Add(route.Name + "：" + SafeError(ex)); }
        }
        throw new IOException("所有下载线路均失败，尚未修改安装目录。" + string.Join("；", errors));
    }
    static string SafeError(Exception ex) => ex is HttpRequestException h ? h.StatusCode is { } code ? "HTTP " + (int)code : "连接失败" : ex is OperationCanceledException ? "网络超时" : ex is CryptographicException ? "签名或哈希验证失败" : ex is InvalidDataException ? ex.Message : "读取或校验失败";
    public void Dispose() => client.Dispose();
}
public sealed record UpdateProgress(string Stage, string Route, int? Percent);

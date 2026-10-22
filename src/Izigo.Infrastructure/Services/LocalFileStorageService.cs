using Izigo.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Services;

// Stores files on the local disk and serves them at /uploads (see Program.cs);
// swap for Azure Blob / S3 when documents need private, signed access.
public class LocalFileStorageService(
    IConfiguration config,
    IHttpContextAccessor httpContext,
    ILogger<LocalFileStorageService> logger) : IFileStorageService
{
    private const string UploadsPath = "/uploads";

    private readonly string _root = ResolveRoot(config);

    private readonly string _baseUrl = (config["Storage:BaseUrl"] ?? "").Trim().TrimEnd('/');

    /// <summary>
    /// The folder uploads are written to and served from. Program.cs uses this too, so the
    /// two cannot disagree. On Azure App Service /tmp and wwwroot are wiped on restart and
    /// deploy, so files go under $HOME (/home), which persists.
    /// </summary>
    public static string ResolveRoot(IConfiguration config)
    {
        var configured = config["Storage:LocalPath"]?.Trim();
        if (!string.IsNullOrEmpty(configured))
            return configured;

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"))
            && !string.IsNullOrEmpty(home))
            return Path.Combine(home, "data", "izigo-uploads");

        return Path.Combine(Path.GetTempPath(), "izigo-uploads");
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType,
        CancellationToken ct = default)
    {
        var normalizedKey = NormalizeKey(key);
        var filePath = Path.Combine(_root, normalizedKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var file = File.Create(filePath);
        await stream.CopyToAsync(file, ct);

        logger.LogInformation("Stored {Key} ({ContentType}, {Bytes} bytes)", normalizedKey, contentType, file.Length);
        return PublicUrl(normalizedKey);
    }

    /// <summary>
    /// Absolute URL for a stored file, because the admin dashboard runs on another host.
    /// Accepts a bare key, the "/uploads/…" path UploadAsync returns, or a full URL.
    /// </summary>
    public string GetSignedUrl(string fileKey, int expiryMinutes = 15)   // no signing for local disk
    {
        var url = PublicUrl(KeyFromReference(fileKey));
        if (!url.StartsWith('/'))
            return url;

        var request = httpContext.HttpContext?.Request;
        if (request is null || !request.Host.HasValue)
            return url;

        // App Service terminates TLS at its front end and forwards plain HTTP.
        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        return $"{scheme}://{request.Host}{url}";
    }

    /// <summary>
    /// Same-host relative path unless Storage:BaseUrl names a real public host, so deployed
    /// APIs never persist localhost URLs.
    /// </summary>
    private string PublicUrl(string normalizedKey) =>
        UsesBaseUrl ? $"{_baseUrl}/{normalizedKey}" : $"{UploadsPath}/{normalizedKey}";

    private bool UsesBaseUrl =>
        _baseUrl.Length > 0
        && !_baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
        && !_baseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKey(string key) => key.Replace('\\', '/').TrimStart('/');

    private string KeyFromReference(string reference)
    {
        var value = reference.Trim();
        if (UsesBaseUrl && value.StartsWith(_baseUrl + "/", StringComparison.OrdinalIgnoreCase))
            return NormalizeKey(value[(_baseUrl.Length + 1)..]);

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            value = uri.AbsolutePath;

        value = NormalizeKey(value);
        var prefix = UploadsPath.TrimStart('/') + "/";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }
}

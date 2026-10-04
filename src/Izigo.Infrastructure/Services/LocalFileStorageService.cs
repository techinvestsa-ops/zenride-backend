using Izigo.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Services;

// Stores files on the local disk; swap for Azure Blob / S3 in production.
public class LocalFileStorageService(IConfiguration config, ILogger<LocalFileStorageService> logger)
    : IFileStorageService
{
    private readonly string _root = config["Storage:LocalPath"]
        ?? Path.Combine(Path.GetTempPath(), "izigo-uploads");

    private readonly string _baseUrl = config["Storage:BaseUrl"]
        ?? "http://localhost:5000/uploads";

    public async Task<string> UploadAsync(Stream stream, string key, string contentType,
        CancellationToken ct = default)
    {
        var filePath = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var file = File.Create(filePath);
        await stream.CopyToAsync(file, ct);

        logger.LogInformation("Stored {Key} ({ContentType}, {Bytes} bytes)", key, contentType, file.Length);
        return PublicUrl(key);
    }

    public string GetSignedUrl(string fileKey, int expiryMinutes = 15)
        => PublicUrl(fileKey);   // no signing for local dev

    /// <summary>
    /// Prefer same-host relative paths when BaseUrl is unset or still pointing at localhost,
    /// so deployed APIs do not persist dev URLs into BackgroundJob.ResultUrl.
    /// </summary>
    private string PublicUrl(string key)
    {
        var normalizedKey = key.Replace('\\', '/').TrimStart('/');
        var relative = $"/uploads/{normalizedKey}";
        var baseUrl = (_baseUrl ?? "").Trim().TrimEnd('/');

        if (string.IsNullOrEmpty(baseUrl)
            || baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || baseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return relative;
        }

        return $"{baseUrl}/{normalizedKey}";
    }
}

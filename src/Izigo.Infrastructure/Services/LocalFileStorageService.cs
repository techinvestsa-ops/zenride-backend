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
        return $"{_baseUrl.TrimEnd('/')}/{key}";
    }

    public string GetSignedUrl(string fileKey, int expiryMinutes = 15)
        => $"{_baseUrl.TrimEnd('/')}/{fileKey}";   // no signing for local dev
}

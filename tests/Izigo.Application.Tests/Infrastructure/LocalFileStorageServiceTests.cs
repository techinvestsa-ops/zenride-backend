using FluentAssertions;
using Izigo.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Izigo.Application.Tests.Infrastructure;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "izigo-storage-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LocalFileStorageService Build(string? baseUrl = null, HttpContext? context = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:LocalPath"] = _root,
                ["Storage:BaseUrl"]   = baseUrl ?? "http://localhost:5000/uploads",
            })
            .Build();
        return new LocalFileStorageService(config,
            new HttpContextAccessor { HttpContext = context },
            NullLogger<LocalFileStorageService>.Instance);
    }

    private static HttpContext Request(string host, string scheme = "http", string? forwardedProto = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString(host);
        if (forwardedProto is not null) ctx.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        return ctx;
    }

    [Fact]
    public void ResolveRoot_TreatsAnEmptyLocalPathAsUnset()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:LocalPath"] = "" })
            .Build();

        LocalFileStorageService.ResolveRoot(config).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Upload_WritesUnderTheRootThatIsServed_AndReturnsAnUploadsPath()
    {
        var url = await Build().UploadAsync(new MemoryStream([1, 2, 3]), "kyc/u1/a.jpg", "image/jpeg");

        url.Should().Be("/uploads/kyc/u1/a.jpg");
        File.Exists(Path.Combine(_root, "kyc", "u1", "a.jpg")).Should().BeTrue();
    }

    [Theory]
    [InlineData("kyc/u1/a.jpg")]
    [InlineData("/uploads/kyc/u1/a.jpg")]
    [InlineData("http://localhost:5000/uploads/kyc/u1/a.jpg")]
    [InlineData("https://old-host.example/uploads/kyc/u1/a.jpg")]
    public void GetSignedUrl_BuildsOneAbsoluteUrlOnTheApiHost(string stored)
    {
        var storage = Build(context: Request("api.example", forwardedProto: "https"));

        storage.GetSignedUrl(stored).Should().Be("https://api.example/uploads/kyc/u1/a.jpg");
    }

    [Fact]
    public void GetSignedUrl_UsesAConfiguredPublicBaseUrl()
    {
        var storage = Build(baseUrl: "https://cdn.example/files", context: Request("api.example"));

        storage.GetSignedUrl("https://cdn.example/files/kyc/u1/a.jpg").Should().Be("https://cdn.example/files/kyc/u1/a.jpg");
        storage.GetSignedUrl("/uploads/kyc/u1/a.jpg").Should().Be("https://cdn.example/files/kyc/u1/a.jpg");
    }

    [Fact]
    public void GetSignedUrl_FallsBackToARelativePathOutsideARequest()
    {
        Build().GetSignedUrl("/uploads/kyc/u1/a.jpg").Should().Be("/uploads/kyc/u1/a.jpg");
    }
}

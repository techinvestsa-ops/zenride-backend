using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Domain.Entities;
using Microsoft.Extensions.Options;
using Moq;

namespace Izigo.Application.Tests.Helpers;

public static class FakeServices
{
    // ── Typed options ─────────────────────────────────────────────────────────

    public static IOptions<T> Options<T>(Action<T>? configure = null) where T : class, new()
    {
        var value = new T();
        configure?.Invoke(value);
        return Microsoft.Extensions.Options.Options.Create(value);
    }

    public static IOptions<OtpSettings>       OtpSettings(Action<OtpSettings>? cfg = null)       => Options(cfg);
    public static IOptions<AppSettings>        AppSettings(Action<AppSettings>? cfg = null)        => Options(cfg);
    public static IOptions<QuoteSettings>      QuoteSettings(Action<QuoteSettings>? cfg = null)    => Options(cfg);
    public static IOptions<AdminAuthSettings>  AdminAuthSettings(Action<AdminAuthSettings>? cfg = null) => Options(cfg);
    public static IOptions<OpsSettings>        OpsSettings(Action<OpsSettings>? cfg = null)        => Options(cfg);
    public static IOptions<DispatchSettings>   DispatchSettings(Action<DispatchSettings>? cfg = null) => Options(cfg);

    // ── Services ──────────────────────────────────────────────────────────────

    public static IAuditService Audit()
    {
        var mock = new Mock<IAuditService>();
        mock.Setup(a => a.RecordAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Izigo.Domain.Enums.AuditAction>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<object?>(), It.IsAny<object?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    public static IPasswordHasher Hasher()
    {
        var mock = new Mock<IPasswordHasher>();
        mock.Setup(h => h.Hash(It.IsAny<string>())).Returns((string p) => $"hashed:{p}");
        mock.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string p, string h) => h == $"hashed:{p}");
        return mock.Object;
    }

    public static IEmailService Email()
    {
        var mock = new Mock<IEmailService>();
        mock.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(e => e.BuildLink(It.IsAny<string>()))
            .Returns((string path) => $"https://admin.test{path}");
        return mock.Object;
    }

    public static IRealtimeService Realtime()
    {
        var mock = new Mock<IRealtimeService>();
        mock.Setup(r => r.PublishToAdminAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(r => r.PublishToUserAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(r => r.PublishToDriverAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(r => r.PublishToTripAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    public static IPushService Push()
    {
        var mock = new Mock<IPushService>();
        mock.Setup(p => p.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(p => p.SendBatchAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    public static IOtpService Otp(string fixedCode = "123456")
    {
        var mock = new Mock<IOtpService>();
        mock.Setup(o => o.GenerateAndSendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixedCode);
        return mock.Object;
    }

    public static ISmsService Sms()
    {
        var mock = new Mock<ISmsService>();
        mock.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    public static ITokenService Token()
    {
        var mock = new Mock<ITokenService>();
        mock.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("test_access_token");
        mock.Setup(t => t.GenerateRefreshToken()).Returns("test_refresh_token");
        mock.Setup(t => t.ValidateRefreshToken(It.IsAny<string>())).Returns((string t) => t);
        mock.Setup(t => t.GenerateAdminAccessToken(It.IsAny<Staff>())).Returns("test_admin_access_token");
        mock.Setup(t => t.GenerateAdminRefreshToken()).Returns("test_admin_refresh_token");
        mock.Setup(t => t.ValidateAdminRefreshToken(It.IsAny<string>())).Returns((string t) => t);
        return mock.Object;
    }

    public static IJobDispatcher JobDispatcher()
    {
        var mock = new Mock<IJobDispatcher>();
        return mock.Object;
    }

    public static IGeoService Geo()
    {
        var mock = new Mock<IGeoService>();
        mock.Setup(g => g.AutocompleteAsync(It.IsAny<string>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        mock.Setup(g => g.GetPlaceDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeoPlaceDetail?)null);
        mock.Setup(g => g.ReverseGeocodeAsync(It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeoReverseResult("123 Test St", 5.3, -4.0));
        mock.Setup(g => g.GetRouteAsync(It.IsAny<GeoPoint>(), It.IsAny<GeoPoint>(),
                It.IsAny<GeoPoint[]?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeoRouteResult("encoded_polyline", 5000, 900, null));
        mock.Setup(g => g.GetEtaAsync(It.IsAny<GeoPoint>(), It.IsAny<GeoPoint[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return mock.Object;
    }

    public static IFileStorageService FileStorage()
    {
        var mock = new Mock<IFileStorageService>();
        mock.Setup(f => f.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.test/file.jpg");
        mock.Setup(f => f.GetSignedUrl(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string key, int _) => $"https://storage.test/{key}?sig=test");
        return mock.Object;
    }
}

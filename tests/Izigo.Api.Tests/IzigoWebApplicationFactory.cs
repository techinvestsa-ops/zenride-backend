using Izigo.Application.Common.Interfaces;
using Izigo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Respawn;

namespace Izigo.Api.Tests;

/// <summary>
/// Boots the real ASP.NET Core host against a dedicated test database.
/// Third-party services (Pusher, FCM, SMTP) are replaced with no-op fakes
/// so tests do not require real credentials.
/// </summary>
public class IzigoWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=IzigoTestDB;Trusted_Connection=True;MultipleActiveResultSets=true";

    private Respawner? _respawner;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace real DB with test DB
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseSqlServer(TestConnectionString));
            services.RemoveAll<IApplicationDbContext>();
            services.AddScoped<IApplicationDbContext>(sp =>
                sp.GetRequiredService<ApplicationDbContext>());

            // Disable rate limiting in tests — all test requests share the same IP partition
            // ("unknown"), so the 120/min global limit is quickly exhausted by the full suite.
            services.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(opts =>
            {
                opts.GlobalLimiter =
                    System.Threading.RateLimiting.PartitionedRateLimiter
                        .Create<Microsoft.AspNetCore.Http.HttpContext, string>(
                            _ => System.Threading.RateLimiting.RateLimitPartition
                                .GetNoLimiter("test-no-limit"));
            });

            // Replace external services with no-op fakes
            services.RemoveAll<IRealtimeService>();
            services.AddSingleton(MockRealtime());

            services.RemoveAll<IPushService>();
            services.AddSingleton(MockPush());

            services.RemoveAll<IEmailService>();
            services.AddScoped(MockEmail());

            services.RemoveAll<IJobDispatcher>();
            services.AddScoped(_ =>
            {
                var m = new Mock<IJobDispatcher>();
                return m.Object;
            });
        });
    }

    public async Task InitializeAsync()
    {
        // Ensure test DB exists and all migrations are applied
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        // Also run the seeder so super admin exists for auth tests
        await Izigo.Infrastructure.Persistence.DbSeeder.SeedAsync(Services);

        _respawner = await Respawner.CreateAsync(TestConnectionString, new RespawnerOptions
        {
            TablesToIgnore = ["__EFMigrationsHistory", "HangFire.Schema",
                              "HangFire.Job", "HangFire.State", "HangFire.JobQueue",
                              "HangFire.Server", "HangFire.Hash", "HangFire.List",
                              "HangFire.Set", "HangFire.Counter", "HangFire.AggregatedCounter"]
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner is not null)
            await _respawner.ResetAsync(TestConnectionString);
        // Re-seed after wipe
        await Izigo.Infrastructure.Persistence.DbSeeder.SeedAsync(Services);
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    // ── Fake factories ────────────────────────────────────────────────────────

    private static IRealtimeService MockRealtime()
    {
        var m = new Mock<IRealtimeService>();
        m.Setup(r => r.PublishToAdminAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        m.Setup(r => r.GetConfig())
            .Returns(new Izigo.Application.Features.Realtime.Dtos.RealtimeConfigDto(
                "signalr", "/hubs/izigo", true));
        m.Setup(r => r.AuthorizeChannelAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Izigo.Application.Features.Realtime.Dtos.RealtimeAuthDto(string.Empty, null));
        return m.Object;
    }

    private static IPushService MockPush()
    {
        var m = new Mock<IPushService>();
        m.Setup(p => p.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        m.Setup(p => p.SendBatchAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return m.Object;
    }

    private static Func<IServiceProvider, IEmailService> MockEmail() => _ =>
    {
        var m = new Mock<IEmailService>();
        m.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return m.Object;
    };
}

using Izigo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Tests.Helpers;

/// <summary>
/// Creates a fresh in-memory ApplicationDbContext for each test.
/// Each call gets its own unique database so tests are isolated.
/// </summary>
public static class DbContextFactory
{
    public static ApplicationDbContext Create(string? name = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;

        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }
}

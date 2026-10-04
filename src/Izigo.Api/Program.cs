using Hangfire;
using Izigo.Api.Extensions;
using Izigo.Api.Hubs;
using Izigo.Api.Middleware;
using Izigo.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console());

    // ── Infrastructure ──────────────────────────────────────────────────────
    builder.Services.AddDatabase(builder.Configuration);
    builder.Services.AddJwtAuth(builder.Configuration);
    builder.Services.AddApiRateLimiting();
    builder.Services.AddHangfire(builder.Configuration);
    builder.Services.AddApplicationSettings(builder.Configuration);
    builder.Services.AddInfrastructureServices();
    builder.Services.AddMediatRServices();

    // ── API ─────────────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
            opts.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

    builder.Services.AddSwaggerServices();
    builder.Services.AddSignalR();

    var corsOrigins = (builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .Append("http://localhost:3000")
        .Append("https://admin-dashboard-tisa.azurewebsites.net")
        .Append("https://admin.zenride.app")
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p => p
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

    // ── App ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // CORS has to wrap error responses. Otherwise a failed login is reported
    // by the browser as a CORS error and the real status never surfaces.
    app.UseCors();
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<AccountLockMiddleware>();

    var uploadRoot = builder.Configuration["Storage:LocalPath"]?.Trim();
    if (string.IsNullOrEmpty(uploadRoot))
        uploadRoot = Path.Combine(Path.GetTempPath(), "izigo-uploads");
    Directory.CreateDirectory(uploadRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadRoot),
        RequestPath = "/uploads",
    });

    app.MapControllers();
    app.MapHub<IzigoHub>("/hubs/izigo");
    app.MapHub<AdminHub>("/hubs/admin");

    //if (app.Environment.IsDevelopment())
    //{
    //    app.UseSwagger();
    //    app.UseSwaggerUI(opts =>
    //    {
    //        opts.SwaggerEndpoint("/swagger/v1/swagger.json", "Izigo API v1");
    //        opts.RoutePrefix = "swagger";
    //    });
    //    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
    //}
    app.UseSwagger();
    app.UseSwaggerUI(opts =>
    {
        opts.SwaggerEndpoint("v1/swagger.json", "Izigo API v1");
        opts.RoutePrefix = "swagger";
    });
    if (app.Environment.IsDevelopment())
    {
        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
    }
    app.UseHangfireDashboard(
        builder.Configuration["Hangfire:DashboardPath"] ?? "/admin/jobs");

    // Seed PlatformConfig + initial super-admin on every startup (idempotent)
    await DbSeeder.SeedAsync(app.Services);

    Log.Information("Izigo API starting on {Env}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start");
}
finally
{
    Log.CloseAndFlush();
}

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }

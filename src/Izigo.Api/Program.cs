using Hangfire;
using Izigo.Api.Extensions;
using Izigo.Api.Hubs;
using Izigo.Api.Middleware;
using Izigo.Infrastructure.Persistence;
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

    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p => p
            .WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                ?? ["http://localhost:3000"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

    // ── App ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    app.UseMiddleware<ExceptionMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
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

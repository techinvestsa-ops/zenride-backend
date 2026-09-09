using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.SqlServer;
using Izigo.Api.Hubs;
using Izigo.Api.Services;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Infrastructure.Jobs;
using Izigo.Infrastructure.Jobs.Processors;
using Izigo.Infrastructure.Persistence;
using Izigo.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// Infrastructure using aliases handled by project references

namespace Izigo.Api.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseSqlServer(config.GetConnectionString("DefaultConnection"),
                sql => sql.EnableRetryOnFailure(3)));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        return services;
    }

    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config)
    {
        var secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret required");
        var key = Encoding.UTF8.GetBytes(secret);
        var issuer = config["Jwt:Issuer"] ?? "izigo-api";
        var appAudience = config["Jwt:Audience"] ?? "izigo-apps";
        var adminAudience = config["Jwt:AdminAudience"] ?? "izigo-admin";
        var clockSkew = TimeSpan.FromSeconds(
            int.TryParse(config["Jwt:ClockSkewSeconds"], out var s) ? s : 30);

        services.AddAuthentication(opts =>
        {
            opts.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            opts.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer("AppBearer", opts =>
        {
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true,
                ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = issuer, ValidAudience = appAudience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = clockSkew
            };
            // SignalR WebSocket connections pass the JWT as ?access_token= since
            // browsers cannot set Authorization headers on WebSocket upgrade requests.
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var token = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) &&
                        ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        ctx.Token = token;
                    return Task.CompletedTask;
                }
            };
        })
        .AddJwtBearer("AdminBearer", opts =>
        {
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true,
                ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = issuer, ValidAudience = adminAudience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = clockSkew
            };
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var token = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) &&
                        ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        ctx.Token = token;
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy("AppPolicy", p =>
                p.AddAuthenticationSchemes("AppBearer").RequireAuthenticatedUser())
            .AddPolicy("AdminPolicy", p =>
                p.AddAuthenticationSchemes("AdminBearer").RequireAuthenticatedUser()
                 .RequireClaim("is_staff", "true"))
            .AddPolicy("RiderPolicy", p =>
                p.AddAuthenticationSchemes("AppBearer").RequireAuthenticatedUser()
                 .RequireClaim("role", "rider"))
            .AddPolicy("DriverPolicy", p =>
                p.AddAuthenticationSchemes("AppBearer").RequireAuthenticatedUser()
                 .RequireClaim("role", "driver"));

        return services;
    }

    /// <summary>
    /// Rate limiting policies:
    /// - "otp"              5 requests / 10 min per IP (anti-brute-force for OTP endpoints)
    /// - "check_avail"      10 requests / 1 min per IP (anti-enumeration for availability check)
    /// - "global"           120 requests / 1 min per IP (general guard)
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = 429;

            opts.AddFixedWindowLimiter("otp", o =>
            {
                o.Window = TimeSpan.FromMinutes(10);
                o.PermitLimit = 5;
                o.QueueLimit = 0;
                o.AutoReplenishment = true;
            });

            opts.AddFixedWindowLimiter("check_avail", o =>
            {
                o.Window = TimeSpan.FromMinutes(1);
                o.PermitLimit = 10;
                o.QueueLimit = 0;
                o.AutoReplenishment = true;
            });

            opts.AddFixedWindowLimiter("global", o =>
            {
                o.Window = TimeSpan.FromMinutes(1);
                o.PermitLimit = 120;
                o.QueueLimit = 0;
                o.AutoReplenishment = true;
            });

            opts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = 120,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    public static IServiceCollection AddHangfire(this IServiceCollection services, IConfiguration config)
    {
        services.AddHangfire(h => h
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(config.GetConnectionString("DefaultConnection"),
                new SqlServerStorageOptions
                {
                    CommandBatchMaxTimeout        = TimeSpan.FromMinutes(5),
                    SlidingInvisibilityTimeout    = TimeSpan.FromMinutes(5),
                    QueuePollInterval             = TimeSpan.Zero,
                    UseRecommendedIsolationLevel  = true,
                    DisableGlobalLocks            = true
                }));

        // Always start the Hangfire server — required for jobs to actually run.
        services.AddHangfireServer(opts =>
        {
            opts.WorkerCount = 2;           // keep it light in dev
            opts.Queues      = ["default"];
        });

        return services;
    }

    public static IServiceCollection AddApplicationSettings(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<OtpSettings>       (config.GetSection("Otp"));
        services.Configure<AppSettings>        (config.GetSection("App"));
        services.Configure<QuoteSettings>      (config.GetSection("Quote"));
        services.Configure<OpsSettings>        (config.GetSection("Ops"));
        services.Configure<AdminAuthSettings>  (config.GetSection("AdminAuth"));
        services.Configure<DispatchSettings>   (config.GetSection("DispatchDefaults"));
        return services;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICurrentStaffService, CurrentStaffService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        services.AddScoped<IGeoService, GoogleMapsService>();
        services.AddScoped<IPaymentGateway, PaymentGatewayService>();
        services.AddSingleton<IRealtimeService, SignalRRealtimeService>();
        services.AddSingleton<IPushService, FcmPushService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<ISmsService, TermiiSmsService>();
        services.AddScoped<IJobDispatcher, HangfireJobDispatcher>();
        // Hangfire job processors — must be registered so DI can inject their dependencies
        services.AddScoped<ReportJobProcessor>();
        services.AddScoped<BroadcastJobProcessor>();
        services.AddScoped<GenericJobProcessor>();
        services.AddScoped<DispatchJobProcessor>();
        services.AddHttpClient("fcm");
        services.AddHttpClient("payment");
        services.AddHttpClient("maps");
        services.AddHttpClient("sms");
        return services;
    }

    public static IServiceCollection AddMediatRServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(
                typeof(Application.Common.Models.ApiResponse).Assembly));
        return services;
    }

    public static IServiceCollection AddSwaggerServices(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(opts =>
        {
            opts.SwaggerDoc("v1", new OpenApiInfo
            {
                Title   = "Izigo API",
                Version = "v1",
                Description = "Rider + Driver API and Admin Console — Ride, Co-Ride, Package verticals"
            });

        // Use the full namespace-qualified name as schema ID to prevent collisions
        // when the same simple type name appears in both app and admin namespaces
        // (e.g. ForgotPasswordRequest in Features.Auth.Dtos vs Features.Admin.Auth.Dtos).
        opts.CustomSchemaIds(type =>
            (type.FullName ?? type.Name)
                .Replace("+", ".")           // flatten nested types
                .Replace("Izigo.Application.Features.", "")  // trim long common prefix
                .Replace("Izigo.Api.Controllers.", ""));

        // If a route still has ambiguous actions, take the first rather than throw.
        opts.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

            // Bearer token auth in Swagger UI
            var scheme = new OpenApiSecurityScheme
            {
                Name         = "Authorization",
                Type         = SecuritySchemeType.Http,
                Scheme       = "bearer",
                BearerFormat = "JWT",
                In           = ParameterLocation.Header,
                Description  = "Paste your access_token here (without 'Bearer ' prefix)"
            };
            opts.AddSecurityDefinition("Bearer", scheme);
            opts.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id   = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}

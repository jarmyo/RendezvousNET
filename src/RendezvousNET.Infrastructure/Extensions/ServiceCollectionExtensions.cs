using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Application.Services;
using RendezvousNET.Infrastructure.Identity;
using RendezvousNET.Infrastructure.Notifications;
using RendezvousNET.Infrastructure.Persistence;

namespace RendezvousNET.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Infrastructure services: EF Core, Identity, JWT, and Notifications.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Database ---
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not set.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // --- Identity ---
        services.AddIdentity<AppUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // --- JWT Authentication ---
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = configuration["Jwt:Issuer"] ?? "RendezvousNET",
                ValidAudience = configuration["Jwt:Audience"] ?? "RendezvousNET",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };

            // Support SignalR hub connections via query string token
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                }
            };
        });

        // --- Application Services ---
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IMeetingService, MeetingService>();
        services.AddScoped<IParticipantService, ParticipantService>();

        // --- Notifications (Composite strategy with fallback) ---
        services.AddSingleton<IEmailNotificationProvider>(sp =>
        {
            var apiKey = configuration["SendGrid:ApiKey"] ?? string.Empty;
            var fromEmail = configuration["SendGrid:FromEmail"] ?? "noreply@rendezvous.app";
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SendGridEmailProvider>>();
            return new SendGridEmailProvider(logger, apiKey, fromEmail);
        });

        services.AddSingleton<INotificationService, CompositeNotificationService>(sp =>
            new CompositeNotificationService(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CompositeNotificationService>>(),
                pushProvider: null,      // Register FCM/APNs provider here when available
                webPushProvider: null,   // Register Web Push (VAPID) provider here when available
                emailProvider: sp.GetRequiredService<IEmailNotificationProvider>()
            ));

        return services;
    }
}

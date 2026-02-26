using RendezvousNET.Api.BackgroundServices;
using RendezvousNET.Api.Hubs;
using RendezvousNET.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// --- Infrastructure (EF Core, Identity, JWT, Notifications) ---
builder.Services.AddInfrastructure(builder.Configuration);

// --- Controllers ---
builder.Services.AddControllers();

// --- SignalR ---
builder.Services.AddSignalR(options =>
{
    // Keep-alive to maintain connections through proxies/load balancers
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// --- OpenAPI ---
builder.Services.AddOpenApi();

// --- CORS (configure allowed origins from configuration in production) ---
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5001", "https://localhost:7001"];

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for SignalR
    });
});

// --- Background Services ---
builder.Services.AddHostedService<CheckInBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<MeetingHub>("/hubs/meeting");

app.Run();

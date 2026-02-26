using Microsoft.EntityFrameworkCore;
using RendezvousNET.Api.Hubs;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Application.Services;
using RendezvousNET.Domain.Interfaces;
using RendezvousNET.Infrastructure.Data;
using RendezvousNET.Infrastructure.Notifications;
using RendezvousNET.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core - SQL Server (switch to PostgreSQL by replacing UseSqlServer with UseNpgsql)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// SignalR for real-time status updates
builder.Services.AddSignalR();

// Clean Architecture DI registrations
builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
builder.Services.AddScoped<IParticipantRepository, ParticipantRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IMeetingStatusHub, MeetingStatusHubClient>();
builder.Services.AddScoped<MeetingService>();

// CORS for Blazor and MAUI clients
builder.Services.AddCors(options =>
    options.AddPolicy("AllowRendezvousClients", policy =>
        policy.WithOrigins("https://localhost:7001", "http://localhost:5001")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowRendezvousClients");
app.UseAuthorization();
app.MapControllers();
app.MapHub<MeetingStatusHub>("/hubs/meeting-status");

app.Run();

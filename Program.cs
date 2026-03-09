using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Components;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Services;
using AgentCompanyWeb.Hubs;
using AgentCompanyEngineer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add SignalR for real-time communication
builder.Services.AddSignalR();

// Database - use DbContextFactory for background services and scoped usage
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=agentcompany.db";
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(connectionString), ServiceLifetime.Scoped);
// Also register regular DbContext for scoped usage (derived from factory)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString), ServiceLifetime.Scoped);

// Services
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddSingleton<ToastService>();
builder.Services.AddScoped<SwarmScaleService>();
builder.Services.AddScoped<GroupService>();

// Cron Jobs and Triggers Services
builder.Services.AddScoped<CronJobService>();
builder.Services.AddScoped<TriggerService>();

// GitHub Webhook Service
builder.Services.AddScoped<GitHubWebhookService>();

// Background services for scheduled tasks and event-driven triggers
builder.Services.AddHostedService<CronSchedulerService>();
builder.Services.AddHostedService<TriggerExecutorService>();

// Agent Engine Services (from AgentCompanyEngineer)
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<AgentNetworkService>();
builder.Services.AddSingleton<AgentService>();
builder.Services.AddSingleton<DockerService>();
builder.Services.AddHttpClient();

// In-process event service for Blazor components (no SignalR client needed)
builder.Services.AddSingleton<AgentNetworkEvents>();

// Agent Network Server (in-process HTTP API for inter-agent communication)
// Now uses IServiceScopeFactory and IHubContext for database persistence and real-time updates
builder.Services.AddSingleton<AgentNetworkServer>();

var app = builder.Build();

// Ensure database is created and apply any pending model changes
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hub for real-time Agent Network communication
app.MapHub<AgentNetworkHub>("/hubs/agentnetwork");

// Map Agent Network HTTP API endpoints (for agents that connect via HTTP)
var agentNetworkServer = app.Services.GetRequiredService<AgentNetworkServer>();
agentNetworkServer.MapEndpoints(app);

// Map GitHub Webhook endpoints
app.MapGitHubWebhookEndpoints();

app.Run();

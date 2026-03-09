using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;
using AgentCompanyWeb.Hubs;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Background service that runs scheduled cron jobs.
/// Checks for due jobs every 30 seconds and executes them.
/// </summary>
public class CronSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CronSchedulerService> _logger;
    private readonly IConfiguration _config;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public CronSchedulerService(
        IServiceScopeFactory scopeFactory,
        ILogger<CronSchedulerService> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cron Scheduler Service started");

        // Initial delay to let the application start up
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueJobsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing cron jobs");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Cron Scheduler Service stopped");
    }

    private async Task ProcessDueJobsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var cronJobService = scope.ServiceProvider.GetRequiredService<CronJobService>();

        var dueJobs = await cronJobService.GetDueJobsAsync();

        if (dueJobs.Count == 0)
            return;

        _logger.LogInformation("Found {Count} due cron jobs to execute", dueJobs.Count);

        foreach (var job in dueJobs)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                await ExecuteJobAsync(job, cronJobService, scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing cron job '{Name}' (ID: {Id})", job.Name, job.Id);
                await cronJobService.UpdateJobExecutionAsync(job.Id, JobExecutionStatus.Failed, ex.Message);
            }
        }
    }

    private async Task ExecuteJobAsync(
        CronJob job,
        CronJobService cronJobService,
        IServiceProvider serviceProvider,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Executing cron job '{Name}' (ID: {Id}), action: {Action}",
            job.Name, job.Id, job.ActionType);

        // Create log entry
        var log = await cronJobService.AddLogAsync(new CronJobLog
        {
            CronJobId = job.Id,
            ScheduledAt = job.NextRunAt ?? DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            Status = JobExecutionStatus.Running
        });

        try
        {
            // Render the message template
            var message = cronJobService.RenderMessage(job);
            log.RenderedMessage = message;

            string? result = null;
            string? target = null;

            switch (job.ActionType)
            {
                case JobActionType.PromptAgent:
                case JobActionType.SendDirectMessage:
                    if (!job.TargetAgentId.HasValue)
                        throw new InvalidOperationException("Target agent not specified");

                    // Check if agent is online (if skip offline is enabled)
                    if (job.SkipIfAgentOffline)
                    {
                        var db = serviceProvider.GetRequiredService<AppDbContext>();
                        var agent = await db.Agents.FindAsync(job.TargetAgentId.Value);
                        if (agent == null || agent.Status != AgentStatus.Online)
                        {
                            _logger.LogInformation("Skipping job '{Name}' - target agent is offline", job.Name);
                            await cronJobService.UpdateLogAsync(log.Id, JobExecutionStatus.Skipped,
                                result: "Agent offline");
                            await cronJobService.UpdateJobExecutionAsync(job.Id, JobExecutionStatus.Skipped);
                            return;
                        }
                    }

                    target = job.TargetAgent?.Name ?? job.TargetAgentId.Value.ToString();
                    result = await SendDirectMessageAsync(
                        job.SenderAgent?.Name ?? "CronScheduler",
                        job.TargetAgent?.Name ?? job.TargetAgentId.Value.ToString(),
                        message,
                        serviceProvider);
                    break;

                case JobActionType.SendTeamMessage:
                    if (!job.TargetTeamId.HasValue)
                        throw new InvalidOperationException("Target team not specified");

                    target = job.TargetTeam?.Name ?? job.TargetTeamId.Value.ToString();
                    result = await SendTeamMessageAsync(
                        job.SenderAgent?.Name ?? "CronScheduler",
                        job.TargetTeam?.Name ?? job.TargetTeamId.Value.ToString(),
                        message,
                        serviceProvider);
                    break;

                case JobActionType.StartAgent:
                    if (!job.TargetAgentId.HasValue)
                        throw new InvalidOperationException("Target agent not specified");

                    target = job.TargetAgent?.Name ?? job.TargetAgentId.Value.ToString();
                    result = await StartAgentAsync(job.TargetAgentId.Value, serviceProvider);
                    break;

                case JobActionType.StopAgent:
                    if (!job.TargetAgentId.HasValue)
                        throw new InvalidOperationException("Target agent not specified");

                    target = job.TargetAgent?.Name ?? job.TargetAgentId.Value.ToString();
                    result = await StopAgentAsync(job.TargetAgentId.Value, serviceProvider);
                    break;

                case JobActionType.RestartAgent:
                    if (!job.TargetAgentId.HasValue)
                        throw new InvalidOperationException("Target agent not specified");

                    target = job.TargetAgent?.Name ?? job.TargetAgentId.Value.ToString();
                    await StopAgentAsync(job.TargetAgentId.Value, serviceProvider);
                    await Task.Delay(2000, stoppingToken); // Wait for container to stop
                    result = await StartAgentAsync(job.TargetAgentId.Value, serviceProvider);
                    break;

                case JobActionType.Webhook:
                    result = await ExecuteWebhookAsync(message, serviceProvider);
                    target = "webhook";
                    break;

                default:
                    throw new InvalidOperationException($"Unknown action type: {job.ActionType}");
            }

            log.Target = target;
            await cronJobService.UpdateLogAsync(log.Id, JobExecutionStatus.Success, result);
            await cronJobService.UpdateJobExecutionAsync(job.Id, JobExecutionStatus.Success);

            _logger.LogInformation("Cron job '{Name}' executed successfully", job.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cron job '{Name}' failed", job.Name);
            await cronJobService.UpdateLogAsync(log.Id, JobExecutionStatus.Failed, error: ex.Message);

            // Handle retry if enabled
            if (job.RetryOnFailure && log.RetryAttempt < job.MaxRetries)
            {
                _logger.LogInformation("Scheduling retry {Attempt}/{Max} for job '{Name}'",
                    log.RetryAttempt + 1, job.MaxRetries, job.Name);
                // Retry will happen on next scheduler cycle
            }
            else
            {
                await cronJobService.UpdateJobExecutionAsync(job.Id, JobExecutionStatus.Failed, ex.Message);
            }
        }
    }

    private async Task<string> SendDirectMessageAsync(
        string from,
        string to,
        string message,
        IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<AppDbContext>();
        var hubContext = serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Hubs.AgentNetworkHub>>();
        var events = serviceProvider.GetRequiredService<AgentNetworkEvents>();

        // Persist to database
        var networkMessage = new NetworkMessage
        {
            From = from,
            To = to,
            Content = message,
            Timestamp = DateTime.UtcNow,
            IsDirectMessage = true
        };

        db.NetworkMessages.Add(networkMessage);
        await db.SaveChangesAsync();

        // Broadcast via SignalR
        var messageDto = new
        {
            id = networkMessage.Id,
            from = networkMessage.From,
            to = networkMessage.To,
            content = networkMessage.Content,
            timestamp = networkMessage.Timestamp,
            isDirectMessage = true
        };

        await hubContext.Clients.Group($"agent:{to}").SendAsync("ReceiveDirectMessage", messageDto);
        await hubContext.Clients.All.SendAsync("DirectMessageReceived", messageDto);

        // Notify in-process subscribers
        events.NotifyMessageReceived(networkMessage.Id, from, to, null, message, networkMessage.Timestamp, true);

        return $"Message sent to {to}";
    }

    private async Task<string> SendTeamMessageAsync(
        string from,
        string teamName,
        string message,
        IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<AppDbContext>();
        var hubContext = serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Hubs.AgentNetworkHub>>();
        var events = serviceProvider.GetRequiredService<AgentNetworkEvents>();

        // Ensure network group exists
        var group = await db.NetworkGroups.FirstOrDefaultAsync(g => g.Name == teamName);
        if (group == null)
        {
            group = new NetworkGroup { Name = teamName, CreatedAt = DateTime.UtcNow };
            db.NetworkGroups.Add(group);
        }

        // Persist message
        var networkMessage = new NetworkMessage
        {
            From = from,
            GroupName = teamName,
            Content = message,
            Timestamp = DateTime.UtcNow,
            IsDirectMessage = false
        };

        db.NetworkMessages.Add(networkMessage);
        await db.SaveChangesAsync();

        // Broadcast via SignalR
        var messageDto = new
        {
            id = networkMessage.Id,
            from = networkMessage.From,
            group = networkMessage.GroupName,
            content = networkMessage.Content,
            timestamp = networkMessage.Timestamp
        };

        await hubContext.Clients.Group($"group:{teamName}").SendAsync("ReceiveGroupMessage", messageDto);
        await hubContext.Clients.All.SendAsync("GroupMessageReceived", messageDto);

        // Notify in-process subscribers
        events.NotifyMessageReceived(networkMessage.Id, from, null, teamName, message, networkMessage.Timestamp, false);

        return $"Message sent to team {teamName}";
    }

    private async Task<string> StartAgentAsync(int agentId, IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.FindAsync(agentId);

        if (agent == null)
            throw new InvalidOperationException($"Agent {agentId} not found");

        if (agent.Status == AgentStatus.Online || agent.Status == AgentStatus.Working)
            return $"Agent {agent.Name} is already running";

        // TODO: Integrate with Docker service to start container
        // For now, just update status
        agent.Status = AgentStatus.Starting;
        await db.SaveChangesAsync();

        return $"Agent {agent.Name} start requested";
    }

    private async Task<string> StopAgentAsync(int agentId, IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.FindAsync(agentId);

        if (agent == null)
            throw new InvalidOperationException($"Agent {agentId} not found");

        if (agent.Status == AgentStatus.Offline)
            return $"Agent {agent.Name} is already stopped";

        // TODO: Integrate with Docker service to stop container
        // For now, just update status
        agent.Status = AgentStatus.Stopping;
        await db.SaveChangesAsync();

        return $"Agent {agent.Name} stop requested";
    }

    private async Task<string> ExecuteWebhookAsync(string webhookUrl, IServiceProvider serviceProvider)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var response = await httpClient.GetAsync(webhookUrl);
        return $"Webhook executed: {response.StatusCode}";
    }
}

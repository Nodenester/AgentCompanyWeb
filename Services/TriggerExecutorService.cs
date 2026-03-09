using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;
using AgentCompanyWeb.Hubs;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Service that listens for events and executes matching triggers.
/// Subscribes to AgentNetworkEvents and processes triggers asynchronously.
/// </summary>
public class TriggerExecutorService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentNetworkEvents _events;
    private readonly ILogger<TriggerExecutorService> _logger;
    private readonly ConcurrentQueue<TriggerEventArgs> _eventQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;

    public TriggerExecutorService(
        IServiceScopeFactory scopeFactory,
        AgentNetworkEvents events,
        ILogger<TriggerExecutorService> logger)
    {
        _scopeFactory = scopeFactory;
        _events = events;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Trigger Executor Service starting");

        // Subscribe to events
        _events.OnAgentStatusChanged += HandleAgentStatusChanged;
        _events.OnAgentTaskChanged += HandleAgentTaskChanged;
        _events.OnMessageReceived += HandleMessageReceived;
        _events.OnFileChanged += HandleFileChanged;
        _events.OnAgentError += HandleAgentError;
        _events.OnTriggerEvent += HandleTriggerEvent;

        // Start background processing
        _processingTask = Task.Run(() => ProcessEventsAsync(_cts.Token), cancellationToken);

        _logger.LogInformation("Trigger Executor Service started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Trigger Executor Service stopping");

        // Unsubscribe from events
        _events.OnAgentStatusChanged -= HandleAgentStatusChanged;
        _events.OnAgentTaskChanged -= HandleAgentTaskChanged;
        _events.OnMessageReceived -= HandleMessageReceived;
        _events.OnFileChanged -= HandleFileChanged;
        _events.OnAgentError -= HandleAgentError;
        _events.OnTriggerEvent -= HandleTriggerEvent;

        // Stop processing
        _cts.Cancel();

        if (_processingTask != null)
        {
            await Task.WhenAny(_processingTask, Task.Delay(5000, cancellationToken));
        }

        _logger.LogInformation("Trigger Executor Service stopped");
    }

    public void Dispose()
    {
        _cts.Dispose();
    }

    private void HandleAgentStatusChanged(AgentStatusEventArgs e)
    {
        if (string.IsNullOrEmpty(e.GroupId)) return;

        var eventData = new Dictionary<string, object?>
        {
            ["agent_name"] = e.AgentName,
            ["status"] = e.Status,
            ["current_task"] = e.CurrentTask
        };

        // Determine specific event type
        var eventType = e.Status switch
        {
            "online" or "Online" => TriggerEventType.AgentOnline,
            "offline" or "Offline" => TriggerEventType.AgentOffline,
            "working" or "Working" => TriggerEventType.AgentStartedWorking,
            _ => TriggerEventType.AgentStatusChanged
        };

        _eventQueue.Enqueue(new TriggerEventArgs
        {
            EventType = eventType,
            GroupId = e.GroupId,
            EventData = eventData,
            Timestamp = e.Timestamp
        });

        // Also queue generic status changed
        if (eventType != TriggerEventType.AgentStatusChanged)
        {
            _eventQueue.Enqueue(new TriggerEventArgs
            {
                EventType = TriggerEventType.AgentStatusChanged,
                GroupId = e.GroupId,
                EventData = eventData,
                Timestamp = e.Timestamp
            });
        }
    }

    private void HandleAgentTaskChanged(AgentTaskEventArgs e)
    {
        if (string.IsNullOrEmpty(e.GroupId)) return;

        var eventData = new Dictionary<string, object?>
        {
            ["agent_name"] = e.AgentName,
            ["previous_task"] = e.PreviousTask,
            ["new_task"] = e.NewTask,
            ["task"] = e.NewTask
        };

        _eventQueue.Enqueue(new TriggerEventArgs
        {
            EventType = TriggerEventType.AgentTaskChanged,
            GroupId = e.GroupId,
            EventData = eventData,
            Timestamp = e.Timestamp
        });

        // Check for work started/finished
        if (string.IsNullOrEmpty(e.PreviousTask) && !string.IsNullOrEmpty(e.NewTask))
        {
            _eventQueue.Enqueue(new TriggerEventArgs
            {
                EventType = TriggerEventType.AgentStartedWorking,
                GroupId = e.GroupId,
                EventData = eventData,
                Timestamp = e.Timestamp
            });
        }
        else if (!string.IsNullOrEmpty(e.PreviousTask) && string.IsNullOrEmpty(e.NewTask))
        {
            _eventQueue.Enqueue(new TriggerEventArgs
            {
                EventType = TriggerEventType.AgentFinishedWorking,
                GroupId = e.GroupId,
                EventData = eventData,
                Timestamp = e.Timestamp
            });
        }
    }

    private void HandleMessageReceived(MessageEventArgs e)
    {
        // We need to find the group ID from the message context
        // For now, we'll process this with a lookup
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                string? groupId = null;

                if (e.IsDirectMessage)
                {
                    // Find agent by name to get group ID
                    var agent = await db.Agents.FirstOrDefaultAsync(a => a.Name == e.To);
                    groupId = agent?.GroupId.ToString();
                }
                else if (!string.IsNullOrEmpty(e.GroupName))
                {
                    // Find team by name to get group ID
                    var team = await db.Teams.FirstOrDefaultAsync(t => t.Name == e.GroupName);
                    groupId = team?.GroupId.ToString();
                }

                if (string.IsNullOrEmpty(groupId)) return;

                var eventData = new Dictionary<string, object?>
                {
                    ["from"] = e.From,
                    ["to"] = e.To,
                    ["group_name"] = e.GroupName,
                    ["message"] = e.Content,
                    ["content"] = e.Content
                };

                var eventType = e.IsDirectMessage
                    ? TriggerEventType.DirectMessageReceived
                    : TriggerEventType.TeamMessageReceived;

                _eventQueue.Enqueue(new TriggerEventArgs
                {
                    EventType = eventType,
                    GroupId = groupId,
                    EventData = eventData,
                    Timestamp = e.Timestamp
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message event for triggers");
            }
        });
    }

    private void HandleFileChanged(FileEventArgs e)
    {
        if (string.IsNullOrEmpty(e.GroupId)) return;

        var eventData = new Dictionary<string, object?>
        {
            ["path"] = e.Path,
            ["change_type"] = e.ChangeType.ToString()
        };

        var eventType = e.ChangeType switch
        {
            FileChangeType.Created => TriggerEventType.FileCreated,
            FileChangeType.Modified => TriggerEventType.FileModified,
            _ => TriggerEventType.FileModified
        };

        _eventQueue.Enqueue(new TriggerEventArgs
        {
            EventType = eventType,
            GroupId = e.GroupId,
            EventData = eventData,
            Timestamp = e.Timestamp
        });
    }

    private void HandleAgentError(AgentErrorEventArgs e)
    {
        if (string.IsNullOrEmpty(e.GroupId)) return;

        var eventData = new Dictionary<string, object?>
        {
            ["agent_name"] = e.AgentName,
            ["error"] = e.Error
        };

        _eventQueue.Enqueue(new TriggerEventArgs
        {
            EventType = TriggerEventType.AgentError,
            GroupId = e.GroupId,
            EventData = eventData,
            Timestamp = e.Timestamp
        });
    }

    private void HandleTriggerEvent(TriggerEventArgs e)
    {
        _eventQueue.Enqueue(e);
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_eventQueue.TryDequeue(out var eventArgs))
                {
                    await ProcessEventAsync(eventArgs, cancellationToken);
                }
                else
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing trigger event");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task ProcessEventAsync(TriggerEventArgs eventArgs, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var triggerService = scope.ServiceProvider.GetRequiredService<TriggerService>();

        // Parse group ID
        if (!int.TryParse(eventArgs.GroupId, out var groupIdInt))
        {
            _logger.LogWarning("Invalid group ID: {GroupId}", eventArgs.GroupId);
            return;
        }

        // Get matching triggers
        int? sourceAgentId = eventArgs.EventData.TryGetValue("agent_id", out var agentId)
            && agentId != null && int.TryParse(agentId.ToString(), out var agentIdInt)
            ? agentIdInt
            : null;
        int? sourceTeamId = eventArgs.EventData.TryGetValue("team_id", out var teamId)
            && teamId != null && int.TryParse(teamId.ToString(), out var teamIdInt)
            ? teamIdInt
            : null;

        var triggers = await triggerService.GetMatchingTriggersAsync(
            eventArgs.EventType,
            groupIdInt,
            sourceAgentId,
            sourceTeamId);

        if (triggers.Count == 0)
            return;

        _logger.LogInformation("Found {Count} matching triggers for event {EventType} in group {GroupId}",
            triggers.Count, eventArgs.EventType, eventArgs.GroupId);

        foreach (var trigger in triggers)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Check condition
            if (!triggerService.MatchesCondition(trigger, eventArgs.EventData))
            {
                _logger.LogDebug("Trigger '{Name}' condition not matched", trigger.Name);
                continue;
            }

            // Handle delay if specified
            if (trigger.DelaySeconds > 0)
            {
                _logger.LogDebug("Delaying trigger '{Name}' for {Seconds} seconds", trigger.Name, trigger.DelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(trigger.DelaySeconds), cancellationToken);
            }

            try
            {
                await ExecuteTriggerAsync(trigger, eventArgs, triggerService, scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing trigger '{Name}'", trigger.Name);
            }
        }
    }

    private async Task ExecuteTriggerAsync(
        Trigger trigger,
        TriggerEventArgs eventArgs,
        TriggerService triggerService,
        IServiceProvider serviceProvider)
    {
        _logger.LogInformation("Executing trigger '{Name}' (ID: {Id})", trigger.Name, trigger.Id);

        // Render message
        var message = triggerService.RenderMessage(trigger, eventArgs.EventType, eventArgs.EventData);

        // Create log entry
        var log = await triggerService.AddLogAsync(new TriggerLog
        {
            TriggerId = trigger.Id,
            EventAt = eventArgs.Timestamp,
            ExecutedAt = DateTime.UtcNow,
            EventType = eventArgs.EventType,
            EventData = System.Text.Json.JsonSerializer.Serialize(eventArgs.EventData),
            Status = JobExecutionStatus.Running,
            RenderedMessage = message
        });

        try
        {
            string? target = null;
            string? result = null;

            switch (trigger.ActionType)
            {
                case JobActionType.PromptAgent:
                case JobActionType.SendDirectMessage:
                    if (!trigger.TargetAgentId.HasValue)
                        throw new InvalidOperationException("Target agent not specified");

                    target = trigger.TargetAgent?.Name ?? trigger.TargetAgentId.Value.ToString();
                    result = await SendDirectMessageAsync(
                        "TriggerSystem",
                        trigger.TargetAgent?.Name ?? trigger.TargetAgentId.Value.ToString(),
                        message,
                        serviceProvider);
                    break;

                case JobActionType.SendTeamMessage:
                    if (!trigger.TargetTeamId.HasValue)
                        throw new InvalidOperationException("Target team not specified");

                    target = trigger.TargetTeam?.Name ?? trigger.TargetTeamId.Value.ToString();
                    result = await SendTeamMessageAsync(
                        "TriggerSystem",
                        trigger.TargetTeam?.Name ?? trigger.TargetTeamId.Value.ToString(),
                        message,
                        serviceProvider);
                    break;

                default:
                    throw new InvalidOperationException($"Trigger action type {trigger.ActionType} not supported");
            }

            // Update log
            log.Target = target;
            log.Result = result;
            log.Status = JobExecutionStatus.Success;

            // Update trigger
            await triggerService.UpdateTriggerFiredAsync(trigger.Id);

            _logger.LogInformation("Trigger '{Name}' executed successfully", trigger.Name);
        }
        catch (Exception ex)
        {
            log.Error = ex.Message;
            log.Status = JobExecutionStatus.Failed;
            _logger.LogError(ex, "Trigger '{Name}' failed", trigger.Name);
        }

        // Save log updates (we need to re-fetch and update)
        using var db = serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        var existingLog = await db.TriggerLogs.FindAsync(log.Id);
        if (existingLog != null)
        {
            existingLog.Target = log.Target;
            existingLog.Result = log.Result;
            existingLog.Error = log.Error;
            existingLog.Status = log.Status;
            await db.SaveChangesAsync();
        }
    }

    private async Task<string> SendDirectMessageAsync(
        string from,
        string to,
        string message,
        IServiceProvider serviceProvider)
    {
        var dbFactory = serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hubContext = serviceProvider.GetRequiredService<IHubContext<AgentNetworkHub>>();

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

        return $"Message sent to {to}";
    }

    private async Task<string> SendTeamMessageAsync(
        string from,
        string teamName,
        string message,
        IServiceProvider serviceProvider)
    {
        var dbFactory = serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hubContext = serviceProvider.GetRequiredService<IHubContext<AgentNetworkHub>>();

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

        return $"Message sent to team {teamName}";
    }
}

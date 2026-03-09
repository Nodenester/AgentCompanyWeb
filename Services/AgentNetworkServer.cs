using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;
using AgentCompanyWeb.Hubs;

namespace AgentCompanyWeb.Services;

/// <summary>
/// In-process Agent Network server that provides HTTP endpoints for inter-agent communication.
/// Persists messages to database and broadcasts via SignalR for real-time updates.
/// </summary>
public class AgentNetworkServer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AgentNetworkHub> _hubContext;
    private readonly AgentNetworkEvents _events;
    private readonly string _sharedFilesPath;
    private readonly ILogger<AgentNetworkServer> _logger;

    public AgentNetworkServer(
        IServiceScopeFactory scopeFactory,
        IHubContext<AgentNetworkHub> hubContext,
        AgentNetworkEvents events,
        ILogger<AgentNetworkServer> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _events = events;
        _logger = logger;
        _sharedFilesPath = config["AgentNetwork:SharedFilesPath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AgentNetwork", "shared-files");

        // Ensure shared files directory exists
        Directory.CreateDirectory(_sharedFilesPath);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/network");

        // Connection management
        group.MapPost("/connect", Connect);
        group.MapPost("/disconnect", Disconnect);
        group.MapGet("/status", GetStatus);
        group.MapGet("/agents", GetAgents);

        // Direct messaging
        group.MapPost("/dm", SendDirectMessage);
        group.MapGet("/inbox", GetInbox);
        group.MapGet("/conversation", GetConversation);

        // Group messaging
        group.MapPost("/group/create", CreateGroup);
        group.MapPost("/group/send", SendGroupMessage);
        group.MapGet("/group/read", GetGroupMessages);
        group.MapGet("/groups", GetGroups);

        // Shared filesystem
        group.MapPost("/file/write", WriteFile);
        group.MapGet("/file/read", ReadFile);
        group.MapGet("/file/list", ListFiles);
        group.MapGet("/file/search", SearchFiles);
        group.MapDelete("/file/delete", DeleteFile);

        // Agent groups lookup
        group.MapGet("/agent/{agentName}/groups", GetAgentGroups);

        // Webhook trigger endpoint for external integrations
        group.MapPost("/trigger/webhook", TriggerWebhook);
    }

    // Webhook trigger for external systems to fire events
    private async Task<IResult> TriggerWebhook(WebhookTriggerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GroupId))
            return Results.BadRequest(new { error = "GroupId required" });

        try
        {
            var eventData = request.Data ?? new Dictionary<string, object?>();

            // Add webhook-specific data
            eventData["webhook_event"] = request.Event ?? "custom";
            eventData["webhook_source"] = request.Source ?? "external";
            eventData["webhook_timestamp"] = DateTime.UtcNow.ToString("O");

            // Fire the trigger event
            _events.FireTriggerEvent(
                Data.Models.TriggerEventType.WebhookReceived,
                request.GroupId,
                eventData);

            _logger.LogInformation("Webhook trigger received for group {GroupId}, event: {Event}",
                request.GroupId, request.Event ?? "custom");

            return Results.Ok(new { success = true, message = "Webhook trigger processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook trigger");
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    // Connection endpoints
    private async Task<IResult> Connect(ConnectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AgentName))
            return Results.BadRequest(new { error = "Agent name required" });

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = await db.NetworkConnections
            .FirstOrDefaultAsync(c => c.AgentName == request.AgentName);

        if (connection == null)
        {
            connection = new NetworkConnection
            {
                AgentName = request.AgentName,
                Status = "online",
                ConnectedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            };
            db.NetworkConnections.Add(connection);
        }
        else
        {
            connection.Status = "online";
            connection.LastSeen = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        // Broadcast status change via SignalR and in-process events
        await _hubContext.Clients.All.SendAsync("AgentStatusChanged", new
        {
            agentName = request.AgentName,
            status = "online",
            timestamp = DateTime.UtcNow
        });
        _events.NotifyAgentStatusChanged(request.AgentName, "online");

        _logger.LogInformation("Agent '{Name}' connected to network", request.AgentName);

        return Results.Ok(new { success = true, agentName = request.AgentName });
    }

    private async Task<IResult> Disconnect(DisconnectRequest request)
    {
        if (string.IsNullOrEmpty(request.AgentName))
            return Results.Ok(new { success = true });

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = await db.NetworkConnections
            .FirstOrDefaultAsync(c => c.AgentName == request.AgentName);

        if (connection != null)
        {
            connection.Status = "offline";
            await db.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("AgentStatusChanged", new
            {
                agentName = request.AgentName,
                status = "offline",
                timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("Agent '{Name}' disconnected", request.AgentName);
        }

        return Results.Ok(new { success = true });
    }

    private async Task<IResult> GetStatus()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connectedCount = await db.NetworkConnections.CountAsync(c => c.Status == "online");
        var groupCount = await db.NetworkGroups.CountAsync();

        return Results.Ok(new
        {
            online = true,
            connectedAgents = connectedCount,
            groups = groupCount
        });
    }

    private async Task<IResult> GetAgents()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agents = await db.NetworkConnections.Select(a => new
        {
            name = a.AgentName,
            status = a.Status,
            currentTask = a.CurrentTask,
            lastSeen = a.LastSeen
        }).ToListAsync();

        return Results.Ok(new { agents });
    }

    private async Task<IResult> GetAgentGroups(string agentName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Find agent by name
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Name == agentName);
        if (agent == null)
        {
            return Results.Ok(new { groups = Array.Empty<string>() });
        }

        // Get teams this agent is a member of
        var teamNames = await db.TeamMembers
            .Where(tm => tm.AgentId == agent.Id)
            .Join(db.Teams, tm => tm.TeamId, t => t.Id, (tm, t) => t.Name)
            .ToListAsync();

        return Results.Ok(new { groups = teamNames });
    }

    // Direct messaging endpoints
    private async Task<IResult> SendDirectMessage(DirectMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new { error = "Recipient and message required" });

        var sender = request.From ?? "system";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Persist to database
        var message = new NetworkMessage
        {
            From = sender,
            To = request.To,
            Content = request.Message,
            Timestamp = DateTime.UtcNow,
            IsDirectMessage = true
        };

        db.NetworkMessages.Add(message);
        await db.SaveChangesAsync();

        // Broadcast via SignalR
        var messageDto = new
        {
            id = message.Id,
            from = message.From,
            to = message.To,
            content = message.Content,
            timestamp = message.Timestamp,
            isDirectMessage = true
        };

        await _hubContext.Clients.Group($"agent:{request.To}").SendAsync("ReceiveDirectMessage", messageDto);
        await _hubContext.Clients.Group($"agent:{sender}").SendAsync("MessageSent", messageDto);
        await _hubContext.Clients.All.SendAsync("DirectMessageReceived", messageDto);

        // Notify in-process subscribers (Blazor components)
        _events.NotifyMessageReceived(message.Id, sender, request.To, null, request.Message, message.Timestamp, true);

        _logger.LogInformation("DM from '{From}' to '{To}'", sender, request.To);

        return Results.Ok(new { success = true, messageId = message.Id });
    }

    private async Task<IResult> GetInbox(string? agentName, int limit = 50)
    {
        if (string.IsNullOrEmpty(agentName))
            return Results.BadRequest(new { error = "Agent name required" });

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var messages = await db.NetworkMessages
            .Where(m => m.IsDirectMessage && m.To == agentName)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                from = m.From,
                to = m.To,
                content = m.Content,
                timestamp = m.Timestamp
            })
            .ToListAsync();

        return Results.Ok(new { messages });
    }

    // Get conversation history between two agents (both sent and received) - FIXED bidirectional
    private async Task<IResult> GetConversation(string? agentName, string? withAgent, int limit = 50)
    {
        if (string.IsNullOrEmpty(agentName) || string.IsNullOrEmpty(withAgent))
            return Results.BadRequest(new { error = "Both agent names required" });

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Get messages in BOTH directions
        var messages = await db.NetworkMessages
            .Where(m => m.IsDirectMessage &&
                ((m.From == agentName && m.To == withAgent) ||
                 (m.From == withAgent && m.To == agentName)))
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                from = m.From,
                to = m.To,
                content = m.Content,
                timestamp = m.Timestamp
            })
            .ToListAsync();

        return Results.Ok(new { messages });
    }

    // Group messaging endpoints
    private async Task<IResult> CreateGroup(CreateGroupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Group name required" });

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.NetworkGroups.FirstOrDefaultAsync(g => g.Name == request.Name);
        if (existing == null)
        {
            db.NetworkGroups.Add(new NetworkGroup
            {
                Name = request.Name,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("GroupCreated", new
            {
                groupName = request.Name,
                timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("Group '{Name}' created", request.Name);
        }

        return Results.Ok(new { success = true, groupName = request.Name });
    }

    private async Task<IResult> SendGroupMessage(GroupMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Group) || string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new { error = "Group and message required" });

        var sender = request.From ?? "system";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check if agent is a member of the team (group)
        // Allow "WebUI" and "system" to send to any group
        if (sender != "WebUI" && sender != "system" && sender != "AgentCompanyWeb")
        {
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Name == sender);
            if (agent != null)
            {
                var team = await db.Teams.FirstOrDefaultAsync(t => t.Name == request.Group);
                if (team != null)
                {
                    var isMember = await db.TeamMembers.AnyAsync(tm => tm.TeamId == team.Id && tm.AgentId == agent.Id);
                    if (!isMember)
                    {
                        _logger.LogWarning("Agent '{Agent}' tried to send to group '{Group}' but is not a member", sender, request.Group);
                        return Results.BadRequest(new { error = $"Agent '{sender}' is not a member of group '{request.Group}'" });
                    }
                }
            }
        }

        // Ensure network group exists for message storage
        var group = await db.NetworkGroups.FirstOrDefaultAsync(g => g.Name == request.Group);
        if (group == null)
        {
            group = new NetworkGroup { Name = request.Group, CreatedAt = DateTime.UtcNow };
            db.NetworkGroups.Add(group);
        }

        // Persist message
        var message = new NetworkMessage
        {
            From = sender,
            GroupName = request.Group,
            Content = request.Message,
            Timestamp = DateTime.UtcNow,
            IsDirectMessage = false
        };

        db.NetworkMessages.Add(message);
        await db.SaveChangesAsync();

        // Broadcast via SignalR
        var messageDto = new
        {
            id = message.Id,
            from = message.From,
            group = message.GroupName,
            content = message.Content,
            timestamp = message.Timestamp
        };

        await _hubContext.Clients.Group($"group:{request.Group}").SendAsync("ReceiveGroupMessage", messageDto);
        await _hubContext.Clients.All.SendAsync("GroupMessageReceived", messageDto);

        // Notify in-process subscribers (Blazor components)
        _events.NotifyMessageReceived(message.Id, sender, null, request.Group, request.Message, message.Timestamp, false);

        _logger.LogInformation("Message to group '{Group}' from '{From}'", request.Group, sender);

        return Results.Ok(new { success = true, messageId = message.Id });
    }

    private async Task<IResult> GetGroupMessages(string? group, int limit = 50)
    {
        if (string.IsNullOrEmpty(group))
            return Results.BadRequest(new { error = "Group name required" });

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var messages = await db.NetworkMessages
            .Where(m => !m.IsDirectMessage && m.GroupName == group)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                from = m.From,
                content = m.Content,
                timestamp = m.Timestamp,
                group = m.GroupName
            })
            .ToListAsync();

        return Results.Ok(new { messages });
    }

    private async Task<IResult> GetGroups()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var groups = await db.NetworkGroups.Select(g => g.Name).ToListAsync();
        return Results.Ok(new { groups });
    }

    // Filesystem endpoints (unchanged - they don't need real-time)
    private async Task<IResult> WriteFile(FileWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return Results.BadRequest(new { error = "Path required" });

        try
        {
            var fullPath = GetSafePath(request.Path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(fullPath, request.Content ?? "");

            _logger.LogInformation("File written: {Path}", request.Path);

            return Results.Ok(new { success = true, path = request.Path });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private async Task<IResult> ReadFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Results.BadRequest(new { error = "Path required" });

        try
        {
            var fullPath = GetSafePath(path);
            if (!File.Exists(fullPath))
                return Results.NotFound(new { error = "File not found" });

            var content = await File.ReadAllTextAsync(fullPath);
            return Results.Ok(new { content, path });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private IResult ListFiles(string? folder)
    {
        try
        {
            var basePath = string.IsNullOrEmpty(folder)
                ? _sharedFilesPath
                : GetSafePath(folder);

            if (!Directory.Exists(basePath))
                return Results.Ok(new { files = Array.Empty<object>() });

            var files = new List<object>();

            // Add directories
            foreach (var dir in Directory.GetDirectories(basePath))
            {
                var info = new DirectoryInfo(dir);
                files.Add(new
                {
                    name = info.Name,
                    path = GetRelativePath(dir),
                    isDirectory = true,
                    size = 0L,
                    modifiedAt = info.LastWriteTimeUtc
                });
            }

            // Add files
            foreach (var file in Directory.GetFiles(basePath))
            {
                var info = new FileInfo(file);
                files.Add(new
                {
                    name = info.Name,
                    path = GetRelativePath(file),
                    isDirectory = false,
                    size = info.Length,
                    modifiedAt = info.LastWriteTimeUtc
                });
            }

            return Results.Ok(new { files });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private IResult SearchFiles(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return Results.BadRequest(new { error = "Query required" });

        try
        {
            var files = Directory.GetFiles(_sharedFilesPath, $"*{query}*", SearchOption.AllDirectories)
                .Take(50)
                .Select(f => new FileInfo(f))
                .Select(info => new
                {
                    name = info.Name,
                    path = GetRelativePath(info.FullName),
                    isDirectory = false,
                    size = info.Length,
                    modifiedAt = info.LastWriteTimeUtc
                })
                .ToList();

            return Results.Ok(new { files });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private IResult DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Results.BadRequest(new { error = "Path required" });

        try
        {
            var fullPath = GetSafePath(path);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("File deleted: {Path}", path);
                return Results.Ok(new { success = true, path });
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                _logger.LogInformation("Directory deleted: {Path}", path);
                return Results.Ok(new { success = true, path });
            }
            else
            {
                return Results.NotFound(new { error = "File or directory not found" });
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private string GetSafePath(string relativePath)
    {
        // Prevent directory traversal attacks
        var normalized = relativePath.Replace("\\", "/").TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_sharedFilesPath, normalized));

        if (!fullPath.StartsWith(_sharedFilesPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid path");

        return fullPath;
    }

    private string GetRelativePath(string fullPath)
    {
        return Path.GetRelativePath(_sharedFilesPath, fullPath).Replace("\\", "/");
    }

    // Request/Response models
    public record ConnectRequest(string AgentName);
    public record DisconnectRequest(string? AgentName);
    public record DirectMessageRequest(string? From, string To, string Message);
    public record CreateGroupRequest(string Name);
    public record GroupMessageRequest(string? From, string Group, string Message);
    public record FileWriteRequest(string Path, string? Content);
    public record WebhookTriggerRequest(
        string GroupId,
        string? Event,
        string? Source,
        Dictionary<string, object?>? Data);
}

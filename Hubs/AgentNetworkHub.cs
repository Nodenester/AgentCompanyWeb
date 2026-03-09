using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;

namespace AgentCompanyWeb.Hubs;

/// <summary>
/// SignalR Hub for real-time Agent Network communication.
/// Handles agent connections, messaging, and status updates.
/// </summary>
public class AgentNetworkHub : Hub
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentNetworkHub> _logger;

    public AgentNetworkHub(IServiceScopeFactory scopeFactory, ILogger<AgentNetworkHub> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Find and update the agent's connection status
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = await db.NetworkConnections
            .FirstOrDefaultAsync(c => c.ConnectionId == Context.ConnectionId);

        if (connection != null)
        {
            connection.Status = "offline";
            connection.ConnectionId = null;
            await db.SaveChangesAsync();

            // Notify all clients about the status change
            await Clients.All.SendAsync("AgentStatusChanged", new
            {
                agentName = connection.AgentName,
                status = "offline",
                timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("Agent '{Name}' disconnected", connection.AgentName);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Register an agent with the network.
    /// </summary>
    public async Task<bool> Connect(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = await db.NetworkConnections
            .FirstOrDefaultAsync(c => c.AgentName == agentName);

        if (connection == null)
        {
            connection = new NetworkConnection
            {
                AgentName = agentName,
                Status = "online",
                ConnectedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow,
                ConnectionId = Context.ConnectionId
            };
            db.NetworkConnections.Add(connection);
        }
        else
        {
            connection.Status = "online";
            connection.LastSeen = DateTime.UtcNow;
            connection.ConnectionId = Context.ConnectionId;
        }

        await db.SaveChangesAsync();

        // Add to SignalR group for this agent
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{agentName}");

        // Notify all clients about the new agent
        await Clients.All.SendAsync("AgentStatusChanged", new
        {
            agentName,
            status = "online",
            timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Agent '{Name}' connected via SignalR", agentName);
        return true;
    }

    /// <summary>
    /// Disconnect an agent from the network.
    /// </summary>
    public async Task Disconnect(string agentName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = await db.NetworkConnections
            .FirstOrDefaultAsync(c => c.AgentName == agentName);

        if (connection != null)
        {
            connection.Status = "offline";
            connection.ConnectionId = null;
            await db.SaveChangesAsync();

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"agent:{agentName}");

            await Clients.All.SendAsync("AgentStatusChanged", new
            {
                agentName,
                status = "offline",
                timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("Agent '{Name}' disconnected", agentName);
        }
    }

    /// <summary>
    /// Update agent status (online, working, etc.)
    /// </summary>
    public async Task UpdateStatus(string agentName, string status, string? currentTask = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = await db.NetworkConnections
            .FirstOrDefaultAsync(c => c.AgentName == agentName);

        if (connection != null)
        {
            connection.Status = status;
            connection.CurrentTask = currentTask;
            connection.LastSeen = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await Clients.All.SendAsync("AgentStatusChanged", new
            {
                agentName,
                status,
                currentTask,
                timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Send a direct message to another agent.
    /// </summary>
    public async Task<bool> SendDirectMessage(string from, string to, string content)
    {
        if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(content))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Persist the message
        var message = new NetworkMessage
        {
            From = from,
            To = to,
            Content = content,
            Timestamp = DateTime.UtcNow,
            IsDirectMessage = true
        };

        db.NetworkMessages.Add(message);
        await db.SaveChangesAsync();

        var messageDto = new
        {
            id = message.Id,
            from = message.From,
            to = message.To,
            content = message.Content,
            timestamp = message.Timestamp,
            isDirectMessage = true
        };

        // Send to recipient's SignalR group
        await Clients.Group($"agent:{to}").SendAsync("ReceiveDirectMessage", messageDto);

        // Also send back to sender for confirmation
        await Clients.Group($"agent:{from}").SendAsync("MessageSent", messageDto);

        _logger.LogInformation("DM from '{From}' to '{To}'", from, to);
        return true;
    }

    /// <summary>
    /// Send a message to a group/team.
    /// </summary>
    public async Task<bool> SendGroupMessage(string from, string groupName, string content)
    {
        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(content))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Ensure group exists
        var group = await db.NetworkGroups.FirstOrDefaultAsync(g => g.Name == groupName);
        if (group == null)
        {
            group = new NetworkGroup { Name = groupName, CreatedAt = DateTime.UtcNow };
            db.NetworkGroups.Add(group);
        }

        // Persist the message
        var message = new NetworkMessage
        {
            From = from,
            GroupName = groupName,
            Content = content,
            Timestamp = DateTime.UtcNow,
            IsDirectMessage = false
        };

        db.NetworkMessages.Add(message);
        await db.SaveChangesAsync();

        var messageDto = new
        {
            id = message.Id,
            from = message.From,
            groupName = message.GroupName,
            content = message.Content,
            timestamp = message.Timestamp,
            isDirectMessage = false
        };

        // Send to the SignalR group for this team
        await Clients.Group($"group:{groupName}").SendAsync("ReceiveGroupMessage", messageDto);

        // Also broadcast to all clients watching this group
        await Clients.All.SendAsync("GroupMessageReceived", messageDto);

        _logger.LogInformation("Group message from '{From}' to '{Group}'", from, groupName);
        return true;
    }

    /// <summary>
    /// Join a group to receive messages.
    /// </summary>
    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"group:{groupName}");
        _logger.LogDebug("Client {ConnectionId} joined group {Group}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Leave a group.
    /// </summary>
    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"group:{groupName}");
        _logger.LogDebug("Client {ConnectionId} left group {Group}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Create a new group.
    /// </summary>
    public async Task<bool> CreateGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.NetworkGroups.FirstOrDefaultAsync(g => g.Name == groupName);
        if (existing != null)
            return true; // Already exists

        db.NetworkGroups.Add(new NetworkGroup
        {
            Name = groupName,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        await Clients.All.SendAsync("GroupCreated", new { groupName, timestamp = DateTime.UtcNow });

        _logger.LogInformation("Group '{Name}' created", groupName);
        return true;
    }

    /// <summary>
    /// Get conversation history between two agents.
    /// </summary>
    public async Task<List<object>> GetConversation(string agentName, string withAgent, int limit = 50)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var messages = await db.NetworkMessages
            .Where(m => m.IsDirectMessage &&
                ((m.From == agentName && m.To == withAgent) ||
                 (m.From == withAgent && m.To == agentName)))
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                id = m.Id,
                from = m.From,
                to = m.To,
                content = m.Content,
                timestamp = m.Timestamp
            })
            .ToListAsync();

        return messages.Cast<object>().ToList();
    }

    /// <summary>
    /// Get group message history.
    /// </summary>
    public async Task<List<object>> GetGroupMessages(string groupName, int limit = 50)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var messages = await db.NetworkMessages
            .Where(m => !m.IsDirectMessage && m.GroupName == groupName)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .Select(m => new
            {
                id = m.Id,
                from = m.From,
                groupName = m.GroupName,
                content = m.Content,
                timestamp = m.Timestamp
            })
            .ToListAsync();

        return messages.Cast<object>().ToList();
    }

    /// <summary>
    /// Get all connected agents.
    /// </summary>
    public async Task<List<object>> GetAgents()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agents = await db.NetworkConnections
            .Select(c => new
            {
                name = c.AgentName,
                status = c.Status,
                currentTask = c.CurrentTask,
                lastSeen = c.LastSeen
            })
            .ToListAsync();

        return agents.Cast<object>().ToList();
    }

    /// <summary>
    /// Get all groups.
    /// </summary>
    public async Task<List<string>> GetGroups()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.NetworkGroups.Select(g => g.Name).ToListAsync();
    }
}

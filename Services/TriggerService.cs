using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Service for managing event-based triggers
/// </summary>
public class TriggerService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<TriggerService> _logger;

    public TriggerService(IDbContextFactory<AppDbContext> dbFactory, ILogger<TriggerService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get all triggers for a group
    /// </summary>
    public async Task<List<Trigger>> GetTriggersByGroupAsync(int groupId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Triggers
            .Include(t => t.SourceAgent)
            .Include(t => t.SourceTeam)
            .Include(t => t.TargetAgent)
            .Include(t => t.TargetTeam)
            .Where(t => t.GroupId == groupId)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Get a trigger by ID
    /// </summary>
    public async Task<Trigger?> GetTriggerAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Triggers
            .Include(t => t.Group)
            .Include(t => t.SourceAgent)
            .Include(t => t.SourceTeam)
            .Include(t => t.TargetAgent)
            .Include(t => t.TargetTeam)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>
    /// Get all enabled triggers that match an event
    /// </summary>
    public async Task<List<Trigger>> GetMatchingTriggersAsync(
        TriggerEventType eventType,
        int groupId,
        int? sourceAgentId = null,
        int? sourceTeamId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        var query = db.Triggers
            .Include(t => t.Group)
            .Include(t => t.TargetAgent)
            .Include(t => t.TargetTeam)
            .Where(t => t.Enabled
                && t.GroupId == groupId
                && t.EventType == eventType
                && (t.MaxTriggers == null || t.TriggerCount < t.MaxTriggers));

        // Filter by source agent if specified in trigger
        if (sourceAgentId.HasValue)
        {
            query = query.Where(t => t.SourceAgentId == null || t.SourceAgentId == sourceAgentId);
        }

        // Filter by source team if specified in trigger
        if (sourceTeamId.HasValue)
        {
            query = query.Where(t => t.SourceTeamId == null || t.SourceTeamId == sourceTeamId);
        }

        var triggers = await query.ToListAsync();

        // Filter by cooldown
        return triggers.Where(t =>
            t.CooldownSeconds == 0 ||
            t.LastTriggeredAt == null ||
            (now - t.LastTriggeredAt.Value).TotalSeconds >= t.CooldownSeconds
        ).ToList();
    }

    /// <summary>
    /// Create a new trigger
    /// </summary>
    public async Task<Trigger> CreateTriggerAsync(Trigger trigger)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        trigger.Id = Guid.NewGuid().ToString();
        trigger.CreatedAt = DateTime.UtcNow;
        trigger.UpdatedAt = DateTime.UtcNow;

        db.Triggers.Add(trigger);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created trigger '{Name}' (ID: {Id}) for event {EventType} in group {GroupId}",
            trigger.Name, trigger.Id, trigger.EventType, trigger.GroupId);

        return trigger;
    }

    /// <summary>
    /// Update an existing trigger
    /// </summary>
    public async Task<Trigger?> UpdateTriggerAsync(string id, Action<Trigger> updateAction)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var trigger = await db.Triggers.FindAsync(id);
        if (trigger == null) return null;

        updateAction(trigger);
        trigger.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation("Updated trigger '{Name}' (ID: {Id})", trigger.Name, trigger.Id);

        return trigger;
    }

    /// <summary>
    /// Delete a trigger
    /// </summary>
    public async Task<bool> DeleteTriggerAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var trigger = await db.Triggers.FindAsync(id);
        if (trigger == null) return false;

        db.Triggers.Remove(trigger);
        await db.SaveChangesAsync();

        _logger.LogInformation("Deleted trigger '{Name}' (ID: {Id})", trigger.Name, id);

        return true;
    }

    /// <summary>
    /// Toggle trigger enabled status
    /// </summary>
    public async Task<bool> ToggleTriggerAsync(string id, bool enabled)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var trigger = await db.Triggers.FindAsync(id);
        if (trigger == null) return false;

        trigger.Enabled = enabled;
        trigger.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation("Toggled trigger '{Name}' (ID: {Id}) to {Status}",
            trigger.Name, id, enabled ? "enabled" : "disabled");

        return true;
    }

    /// <summary>
    /// Update trigger after it fires
    /// </summary>
    public async Task UpdateTriggerFiredAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var trigger = await db.Triggers.FindAsync(id);
        if (trigger == null) return;

        trigger.LastTriggeredAt = DateTime.UtcNow;
        trigger.TriggerCount++;
        trigger.UpdatedAt = DateTime.UtcNow;

        // Disable if max triggers reached
        if (trigger.MaxTriggers.HasValue && trigger.TriggerCount >= trigger.MaxTriggers)
        {
            trigger.Enabled = false;
            _logger.LogInformation("Trigger '{Name}' reached max triggers ({Max}), disabled",
                trigger.Name, trigger.MaxTriggers);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Add trigger execution log
    /// </summary>
    public async Task<TriggerLog> AddLogAsync(TriggerLog log)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        log.Id = Guid.NewGuid().ToString();
        db.TriggerLogs.Add(log);
        await db.SaveChangesAsync();

        return log;
    }

    /// <summary>
    /// Get execution logs for a trigger
    /// </summary>
    public async Task<List<TriggerLog>> GetLogsAsync(string triggerId, int limit = 50)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.TriggerLogs
            .Where(l => l.TriggerId == triggerId)
            .OrderByDescending(l => l.ExecutedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Get recent trigger logs for a group
    /// </summary>
    public async Task<List<TriggerLog>> GetRecentLogsAsync(int groupId, int limit = 100)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.TriggerLogs
            .Include(l => l.Trigger)
            .Where(l => l.Trigger != null && l.Trigger.GroupId == groupId)
            .OrderByDescending(l => l.ExecutedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Check if event data matches trigger condition
    /// </summary>
    public bool MatchesCondition(Trigger trigger, Dictionary<string, object?> eventData)
    {
        if (string.IsNullOrWhiteSpace(trigger.ConditionJson))
            return true; // No condition = always match

        try
        {
            var condition = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(trigger.ConditionJson);
            if (condition == null) return true;

            foreach (var (key, value) in condition)
            {
                if (!eventData.TryGetValue(key, out var eventValue))
                    return false;

                // Handle different condition types
                if (key.EndsWith("_contains"))
                {
                    var actualKey = key[..^9]; // Remove "_contains"
                    if (!eventData.TryGetValue(actualKey, out var actualValue))
                        return false;

                    var searchString = value.GetString() ?? "";
                    var actualString = actualValue?.ToString() ?? "";
                    if (!actualString.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                else if (key.EndsWith("_regex"))
                {
                    var actualKey = key[..^6]; // Remove "_regex"
                    if (!eventData.TryGetValue(actualKey, out var actualValue))
                        return false;

                    var pattern = value.GetString() ?? "";
                    var actualString = actualValue?.ToString() ?? "";
                    if (!System.Text.RegularExpressions.Regex.IsMatch(actualString, pattern))
                        return false;
                }
                else
                {
                    // Exact match
                    var conditionValue = value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : value.ToString();

                    if (!string.Equals(eventValue?.ToString(), conditionValue, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse trigger condition for {TriggerId}", trigger.Id);
            return false;
        }
    }

    /// <summary>
    /// Render message template with variable substitution
    /// </summary>
    public string RenderMessage(Trigger trigger, TriggerEventType eventType, Dictionary<string, object?> eventData)
    {
        var message = trigger.MessageTemplate;
        var now = DateTime.UtcNow;

        // Standard variables
        message = message.Replace("{date}", now.ToString("yyyy-MM-dd"));
        message = message.Replace("{time}", now.ToString("HH:mm:ss"));
        message = message.Replace("{datetime}", now.ToString("yyyy-MM-dd HH:mm:ss"));
        message = message.Replace("{timestamp}", now.ToString("O"));

        // Event variables
        message = message.Replace("{event_type}", eventType.ToString());
        message = message.Replace("{trigger_name}", trigger.Name);
        message = message.Replace("{trigger_count}", (trigger.TriggerCount + 1).ToString());

        // Entity variables
        message = message.Replace("{source_agent}", trigger.SourceAgent?.Name ?? "Any");
        message = message.Replace("{source_team}", trigger.SourceTeam?.Name ?? "Any");
        message = message.Replace("{target_agent}", trigger.TargetAgent?.Name ?? "Unknown");
        message = message.Replace("{target_team}", trigger.TargetTeam?.Name ?? "Unknown");
        message = message.Replace("{group_name}", trigger.Group?.Name ?? "Unknown");

        // Event data variables
        foreach (var (key, value) in eventData)
        {
            message = message.Replace($"{{{key}}}", value?.ToString() ?? "");
        }

        // Special event data serialization
        try
        {
            message = message.Replace("{event_data}", JsonSerializer.Serialize(eventData));
        }
        catch
        {
            message = message.Replace("{event_data}", "{}");
        }

        return message;
    }

    /// <summary>
    /// Get human-readable event type description
    /// </summary>
    public static string GetEventTypeDescription(TriggerEventType eventType)
    {
        return eventType switch
        {
            TriggerEventType.AgentStatusChanged => "Agent status changes",
            TriggerEventType.AgentTaskChanged => "Agent task changes",
            TriggerEventType.AgentOnline => "Agent comes online",
            TriggerEventType.AgentOffline => "Agent goes offline",
            TriggerEventType.AgentStartedWorking => "Agent starts working",
            TriggerEventType.AgentFinishedWorking => "Agent finishes working",
            TriggerEventType.TeamMessageReceived => "Message received in team",
            TriggerEventType.DirectMessageReceived => "Direct message received",
            TriggerEventType.FileCreated => "File created in shared storage",
            TriggerEventType.FileModified => "File modified in shared storage",
            TriggerEventType.AgentError => "Agent error occurs",
            TriggerEventType.AgentHealthCheckFailed => "Agent health check fails",
            TriggerEventType.AgentJoinedGroup => "New agent joins group",
            TriggerEventType.AgentLeftGroup => "Agent leaves group",
            TriggerEventType.WebhookReceived => "Webhook received",
            TriggerEventType.CustomEvent => "Custom event fired",
            _ => eventType.ToString()
        };
    }

    /// <summary>
    /// Clean up old log entries
    /// </summary>
    public async Task<int> CleanupLogsAsync(int daysToKeep = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);
        var oldLogs = await db.TriggerLogs
            .Where(l => l.ExecutedAt < cutoff)
            .ToListAsync();

        if (oldLogs.Count > 0)
        {
            db.TriggerLogs.RemoveRange(oldLogs);
            await db.SaveChangesAsync();
            _logger.LogInformation("Cleaned up {Count} old trigger logs", oldLogs.Count);
        }

        return oldLogs.Count;
    }
}

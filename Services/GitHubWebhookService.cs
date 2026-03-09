using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;
using AgentCompanyWeb.Hubs;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Service for managing GitHub webhooks and processing incoming events
/// </summary>
public class GitHubWebhookService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHubContext<AgentNetworkHub> _hubContext;
    private readonly AgentNetworkEvents _events;
    private readonly ILogger<GitHubWebhookService> _logger;

    public GitHubWebhookService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHubContext<AgentNetworkHub> hubContext,
        AgentNetworkEvents events,
        ILogger<GitHubWebhookService> logger)
    {
        _dbFactory = dbFactory;
        _hubContext = hubContext;
        _events = events;
        _logger = logger;
    }

    #region CRUD Operations

    public async Task<List<GitHubWebhook>> GetWebhooksByGroupAsync(int groupId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Set<GitHubWebhook>()
            .Include(w => w.TargetAgent)
            .Include(w => w.TargetTeam)
            .Where(w => w.GroupId == groupId)
            .OrderBy(w => w.Name)
            .ToListAsync();
    }

    public async Task<GitHubWebhook?> GetWebhookAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Set<GitHubWebhook>()
            .Include(w => w.Group)
            .Include(w => w.TargetAgent)
            .Include(w => w.TargetTeam)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<GitHubWebhook> CreateWebhookAsync(GitHubWebhook webhook)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        webhook.Id = Guid.NewGuid().ToString();
        webhook.CreatedAt = DateTime.UtcNow;
        webhook.UpdatedAt = DateTime.UtcNow;

        // Generate a random secret if not provided
        if (string.IsNullOrEmpty(webhook.WebhookSecret))
        {
            webhook.WebhookSecret = GenerateWebhookSecret();
        }

        db.Set<GitHubWebhook>().Add(webhook);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created GitHub webhook '{Name}' (ID: {Id}) for repo {Repo}",
            webhook.Name, webhook.Id, webhook.Repository);

        return webhook;
    }

    public async Task<GitHubWebhook?> UpdateWebhookAsync(string id, Action<GitHubWebhook> updateAction)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var webhook = await db.Set<GitHubWebhook>().FindAsync(id);
        if (webhook == null) return null;

        updateAction(webhook);
        webhook.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation("Updated GitHub webhook '{Name}' (ID: {Id})", webhook.Name, webhook.Id);

        return webhook;
    }

    public async Task<bool> DeleteWebhookAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var webhook = await db.Set<GitHubWebhook>().FindAsync(id);
        if (webhook == null) return false;

        db.Set<GitHubWebhook>().Remove(webhook);
        await db.SaveChangesAsync();

        _logger.LogInformation("Deleted GitHub webhook '{Name}' (ID: {Id})", webhook.Name, id);

        return true;
    }

    public async Task<bool> ToggleWebhookAsync(string id, bool enabled)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var webhook = await db.Set<GitHubWebhook>().FindAsync(id);
        if (webhook == null) return false;

        webhook.Enabled = enabled;
        webhook.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation("Toggled GitHub webhook '{Name}' (ID: {Id}) to {Status}",
            webhook.Name, id, enabled ? "enabled" : "disabled");

        return true;
    }

    #endregion

    #region Webhook Processing

    /// <summary>
    /// Validate GitHub webhook signature
    /// </summary>
    public bool ValidateSignature(string payload, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret))
            return false;

        // GitHub sends signature as "sha256=<hash>"
        if (!signature.StartsWith("sha256="))
            return false;

        var expectedHash = signature[7..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedHash = Convert.ToHexString(hash).ToLowerInvariant();

        return string.Equals(expectedHash, computedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Process an incoming GitHub webhook event
    /// </summary>
    public async Task<(bool Success, string Message)> ProcessWebhookAsync(
        string webhookId,
        string eventType,
        string? signature,
        string payload)
    {
        var webhook = await GetWebhookAsync(webhookId);
        if (webhook == null)
        {
            return (false, "Webhook not found");
        }

        if (!webhook.Enabled)
        {
            return (false, "Webhook is disabled");
        }

        // Validate signature if secret is configured
        if (!string.IsNullOrEmpty(webhook.WebhookSecret))
        {
            if (!ValidateSignature(payload, signature, webhook.WebhookSecret))
            {
                _logger.LogWarning("Invalid signature for webhook {Id}", webhookId);
                return (false, "Invalid signature");
            }
        }

        // Check cooldown
        if (webhook.CooldownSeconds > 0 && webhook.LastTriggeredAt.HasValue)
        {
            var elapsed = (DateTime.UtcNow - webhook.LastTriggeredAt.Value).TotalSeconds;
            if (elapsed < webhook.CooldownSeconds)
            {
                return (false, $"Cooldown active, {webhook.CooldownSeconds - (int)elapsed}s remaining");
            }
        }

        // Handle ping event
        if (eventType == "ping")
        {
            _logger.LogInformation("Ping received for webhook {Id}", webhookId);
            return (true, "Pong! Webhook configured successfully.");
        }

        // Check if we handle this event type
        if (!webhook.HandlesEvent(eventType))
        {
            return (false, $"Event type '{eventType}' not configured for this webhook");
        }

        // Parse payload
        GitHubEventData eventData;
        try
        {
            eventData = ParsePayload(eventType, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse webhook payload");
            return (false, "Failed to parse payload");
        }

        // Check branch filter
        if (!webhook.MatchesBranch(eventData.Branch))
        {
            return (false, $"Branch '{eventData.Branch}' doesn't match filter '{webhook.BranchFilter}'");
        }

        // Create log entry
        await using var db = await _dbFactory.CreateDbContextAsync();
        var log = new GitHubWebhookLog
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = webhookId,
            EventType = eventType,
            EventAction = eventData.Action,
            DeliveryId = eventData.DeliveryId,
            Repository = eventData.Repository,
            Branch = eventData.Branch,
            Sender = eventData.Sender,
            ReceivedAt = DateTime.UtcNow,
            Status = JobExecutionStatus.Running,
            Payload = payload.Length > 10000 ? payload[..10000] + "..." : payload
        };

        db.Set<GitHubWebhookLog>().Add(log);
        await db.SaveChangesAsync();

        try
        {
            // Render message
            var message = RenderMessage(webhook.MessageTemplate, eventData);
            log.RenderedMessage = message;

            // Execute action
            string? target = null;
            switch (webhook.ActionType)
            {
                case JobActionType.PromptAgent:
                case JobActionType.SendDirectMessage:
                    if (!webhook.TargetAgentId.HasValue)
                        throw new InvalidOperationException("Target agent not configured");

                    target = webhook.TargetAgent?.Name ?? webhook.TargetAgentId.Value.ToString();
                    await SendDirectMessageAsync("GitHub", target, message, db);
                    break;

                case JobActionType.SendTeamMessage:
                    if (!webhook.TargetTeamId.HasValue)
                        throw new InvalidOperationException("Target team not configured");

                    target = webhook.TargetTeam?.Name ?? webhook.TargetTeamId.Value.ToString();
                    await SendTeamMessageAsync("GitHub", target, message, db);
                    break;

                default:
                    throw new InvalidOperationException($"Action type {webhook.ActionType} not supported for GitHub webhooks");
            }

            // Update log
            log.Target = target;
            log.ProcessedAt = DateTime.UtcNow;
            log.Status = JobExecutionStatus.Success;

            // Update webhook stats
            var webhookToUpdate = await db.Set<GitHubWebhook>().FindAsync(webhookId);
            if (webhookToUpdate != null)
            {
                webhookToUpdate.LastTriggeredAt = DateTime.UtcNow;
                webhookToUpdate.TriggerCount++;
            }

            await db.SaveChangesAsync();

            _logger.LogInformation("GitHub webhook {Id} processed: {Event} from {Repo}",
                webhookId, eventType, eventData.Repository);

            // Also fire a trigger event for any listening triggers
            _events.FireTriggerEvent(TriggerEventType.WebhookReceived, webhook.GroupId.ToString(),
                new Dictionary<string, object?>
                {
                    ["source"] = "github",
                    ["event"] = eventType,
                    ["action"] = eventData.Action,
                    ["repository"] = eventData.Repository,
                    ["branch"] = eventData.Branch,
                    ["sender"] = eventData.Sender,
                    ["title"] = eventData.Title,
                    ["url"] = eventData.Url
                });

            return (true, $"Webhook processed successfully. Message sent to {target}.");
        }
        catch (Exception ex)
        {
            log.ProcessedAt = DateTime.UtcNow;
            log.Status = JobExecutionStatus.Failed;
            log.Error = ex.Message;
            await db.SaveChangesAsync();

            _logger.LogError(ex, "Failed to process webhook {Id}", webhookId);
            return (false, $"Failed to process: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse GitHub webhook payload into a structured format
    /// </summary>
    private GitHubEventData ParsePayload(string eventType, string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var data = new GitHubEventData
        {
            Event = eventType
        };

        // Extract common fields
        if (root.TryGetProperty("action", out var action))
            data.Action = action.GetString();

        if (root.TryGetProperty("sender", out var sender) && sender.TryGetProperty("login", out var login))
            data.Sender = login.GetString();

        if (root.TryGetProperty("repository", out var repo))
        {
            if (repo.TryGetProperty("full_name", out var fullName))
                data.Repository = fullName.GetString();
            if (repo.TryGetProperty("html_url", out var repoUrl))
                data.RepositoryUrl = repoUrl.GetString();
        }

        // Event-specific parsing
        switch (eventType)
        {
            case "push":
                if (root.TryGetProperty("ref", out var pushRef))
                {
                    var refStr = pushRef.GetString() ?? "";
                    data.Branch = refStr.Replace("refs/heads/", "").Replace("refs/tags/", "");
                }
                if (root.TryGetProperty("head_commit", out var headCommit))
                {
                    if (headCommit.TryGetProperty("message", out var msg))
                        data.Title = msg.GetString()?.Split('\n').FirstOrDefault();
                    if (headCommit.TryGetProperty("url", out var commitUrl))
                        data.Url = commitUrl.GetString();
                }
                if (root.TryGetProperty("commits", out var commits))
                    data.CommitCount = commits.GetArrayLength();
                break;

            case "pull_request":
                if (root.TryGetProperty("pull_request", out var pr))
                {
                    if (pr.TryGetProperty("title", out var prTitle))
                        data.Title = prTitle.GetString();
                    if (pr.TryGetProperty("html_url", out var prUrl))
                        data.Url = prUrl.GetString();
                    if (pr.TryGetProperty("body", out var prBody))
                        data.Body = prBody.GetString();
                    if (pr.TryGetProperty("number", out var prNumber))
                        data.Number = prNumber.GetInt32();
                    if (pr.TryGetProperty("head", out var head) && head.TryGetProperty("ref", out var headRef))
                        data.Branch = headRef.GetString();
                    if (pr.TryGetProperty("base", out var baseBranch) && baseBranch.TryGetProperty("ref", out var baseRef))
                        data.BaseBranch = baseRef.GetString();
                }
                break;

            case "issues":
                if (root.TryGetProperty("issue", out var issue))
                {
                    if (issue.TryGetProperty("title", out var issueTitle))
                        data.Title = issueTitle.GetString();
                    if (issue.TryGetProperty("html_url", out var issueUrl))
                        data.Url = issueUrl.GetString();
                    if (issue.TryGetProperty("body", out var issueBody))
                        data.Body = issueBody.GetString();
                    if (issue.TryGetProperty("number", out var issueNumber))
                        data.Number = issueNumber.GetInt32();
                }
                break;

            case "issue_comment":
            case "commit_comment":
            case "pull_request_review_comment":
                if (root.TryGetProperty("comment", out var comment))
                {
                    if (comment.TryGetProperty("body", out var commentBody))
                        data.Body = commentBody.GetString();
                    if (comment.TryGetProperty("html_url", out var commentUrl))
                        data.Url = commentUrl.GetString();
                }
                break;

            case "release":
                if (root.TryGetProperty("release", out var release))
                {
                    if (release.TryGetProperty("tag_name", out var tagName))
                        data.Title = tagName.GetString();
                    if (release.TryGetProperty("html_url", out var releaseUrl))
                        data.Url = releaseUrl.GetString();
                    if (release.TryGetProperty("body", out var releaseBody))
                        data.Body = releaseBody.GetString();
                }
                break;

            case "create":
            case "delete":
                if (root.TryGetProperty("ref", out var createRef))
                    data.Branch = createRef.GetString();
                if (root.TryGetProperty("ref_type", out var refType))
                    data.RefType = refType.GetString();
                break;

            case "workflow_run":
                if (root.TryGetProperty("workflow_run", out var workflowRun))
                {
                    if (workflowRun.TryGetProperty("name", out var wfName))
                        data.Title = wfName.GetString();
                    if (workflowRun.TryGetProperty("html_url", out var wfUrl))
                        data.Url = wfUrl.GetString();
                    if (workflowRun.TryGetProperty("conclusion", out var conclusion))
                        data.Conclusion = conclusion.GetString();
                    if (workflowRun.TryGetProperty("head_branch", out var wfBranch))
                        data.Branch = wfBranch.GetString();
                }
                break;

            case "check_run":
            case "check_suite":
                var checkKey = eventType == "check_run" ? "check_run" : "check_suite";
                if (root.TryGetProperty(checkKey, out var check))
                {
                    if (check.TryGetProperty("conclusion", out var checkConclusion))
                        data.Conclusion = checkConclusion.GetString();
                    if (check.TryGetProperty("html_url", out var checkUrl))
                        data.Url = checkUrl.GetString();
                }
                break;
        }

        return data;
    }

    /// <summary>
    /// Render message template with event data
    /// </summary>
    private string RenderMessage(string template, GitHubEventData data)
    {
        var message = template;

        // Basic event info
        message = message.Replace("{event}", data.Event ?? "");
        message = message.Replace("{action}", data.Action ?? "");
        message = message.Replace("{repo}", data.Repository ?? "");
        message = message.Replace("{repository}", data.Repository ?? "");
        message = message.Replace("{branch}", data.Branch ?? "");
        message = message.Replace("{base_branch}", data.BaseBranch ?? "");
        message = message.Replace("{sender}", data.Sender ?? "");
        message = message.Replace("{url}", data.Url ?? "");
        message = message.Replace("{title}", data.Title ?? "");
        message = message.Replace("{body}", TruncateBody(data.Body, 500));
        message = message.Replace("{number}", data.Number?.ToString() ?? "");
        message = message.Replace("{commits}", data.CommitCount?.ToString() ?? "0");
        message = message.Replace("{conclusion}", data.Conclusion ?? "");
        message = message.Replace("{ref_type}", data.RefType ?? "");

        // Date/time
        message = message.Replace("{date}", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        message = message.Replace("{time}", DateTime.UtcNow.ToString("HH:mm:ss"));
        message = message.Replace("{datetime}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

        return message;
    }

    private string TruncateBody(string? body, int maxLength)
    {
        if (string.IsNullOrEmpty(body)) return "";
        if (body.Length <= maxLength) return body;
        return body[..maxLength] + "...";
    }

    #endregion

    #region Messaging

    private async Task SendDirectMessageAsync(string from, string to, string message, AppDbContext db)
    {
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

        var messageDto = new
        {
            id = networkMessage.Id,
            from = networkMessage.From,
            to = networkMessage.To,
            content = networkMessage.Content,
            timestamp = networkMessage.Timestamp,
            isDirectMessage = true
        };

        await _hubContext.Clients.Group($"agent:{to}").SendAsync("ReceiveDirectMessage", messageDto);
        await _hubContext.Clients.All.SendAsync("DirectMessageReceived", messageDto);

        _events.NotifyMessageReceived(networkMessage.Id, from, to, null, message, networkMessage.Timestamp, true);
    }

    private async Task SendTeamMessageAsync(string from, string teamName, string message, AppDbContext db)
    {
        var group = await db.NetworkGroups.FirstOrDefaultAsync(g => g.Name == teamName);
        if (group == null)
        {
            group = new NetworkGroup { Name = teamName, CreatedAt = DateTime.UtcNow };
            db.NetworkGroups.Add(group);
        }

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

        var messageDto = new
        {
            id = networkMessage.Id,
            from = networkMessage.From,
            group = networkMessage.GroupName,
            content = networkMessage.Content,
            timestamp = networkMessage.Timestamp
        };

        await _hubContext.Clients.Group($"group:{teamName}").SendAsync("ReceiveGroupMessage", messageDto);
        await _hubContext.Clients.All.SendAsync("GroupMessageReceived", messageDto);

        _events.NotifyMessageReceived(networkMessage.Id, from, null, teamName, message, networkMessage.Timestamp, false);
    }

    #endregion

    #region Utilities

    public static string GenerateWebhookSecret()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<List<GitHubWebhookLog>> GetLogsAsync(string webhookId, int limit = 50)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Set<GitHubWebhookLog>()
            .Where(l => l.WebhookId == webhookId)
            .OrderByDescending(l => l.ReceivedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<GitHubWebhookLog>> GetRecentLogsAsync(int groupId, int limit = 100)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Set<GitHubWebhookLog>()
            .Include(l => l.Webhook)
            .Where(l => l.Webhook != null && l.Webhook.GroupId == groupId)
            .OrderByDescending(l => l.ReceivedAt)
            .Take(limit)
            .ToListAsync();
    }

    #endregion
}

/// <summary>
/// Parsed GitHub event data
/// </summary>
public class GitHubEventData
{
    public string? Event { get; set; }
    public string? Action { get; set; }
    public string? DeliveryId { get; set; }
    public string? Repository { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? Branch { get; set; }
    public string? BaseBranch { get; set; }
    public string? Sender { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Url { get; set; }
    public int? Number { get; set; }
    public int? CommitCount { get; set; }
    public string? Conclusion { get; set; }
    public string? RefType { get; set; }
}

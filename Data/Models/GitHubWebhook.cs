using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// Configuration for a GitHub webhook that triggers agent actions
/// </summary>
public class GitHubWebhook
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// The group this webhook belongs to
    /// </summary>
    [Required]
    public int GroupId { get; set; }

    [ForeignKey(nameof(GroupId))]
    public Group? Group { get; set; }

    /// <summary>
    /// GitHub repository in format "owner/repo"
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Whether this webhook is active
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Secret for validating webhook signatures (HMAC-SHA256)
    /// </summary>
    [MaxLength(100)]
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Which GitHub events to listen for (comma-separated)
    /// Examples: push, pull_request, issues, issue_comment, create, delete, release
    /// Use * for all events
    /// </summary>
    [Required]
    public string Events { get; set; } = "push,pull_request";

    /// <summary>
    /// Filter by branch (regex pattern, optional)
    /// Examples: "main", "main|develop", "feature/.*"
    /// </summary>
    [MaxLength(200)]
    public string? BranchFilter { get; set; }

    /// <summary>
    /// Action to perform when triggered
    /// </summary>
    [Required]
    public JobActionType ActionType { get; set; } = JobActionType.PromptAgent;

    /// <summary>
    /// Target agent ID (for PromptAgent, SendDirectMessage)
    /// </summary>
    public int? TargetAgentId { get; set; }

    [ForeignKey(nameof(TargetAgentId))]
    public Agent? TargetAgent { get; set; }

    /// <summary>
    /// Target team ID (for SendTeamMessage)
    /// </summary>
    public int? TargetTeamId { get; set; }

    [ForeignKey(nameof(TargetTeamId))]
    public Team? TargetTeam { get; set; }

    /// <summary>
    /// Message template to send
    /// Supports variables: {event}, {action}, {repo}, {branch}, {sender}, {url}, {title}, {body}
    /// </summary>
    [Required]
    public string MessageTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Cooldown in seconds between triggers from the same webhook
    /// </summary>
    public int CooldownSeconds { get; set; } = 0;

    /// <summary>
    /// Last time this webhook was triggered
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// Number of times this webhook has been triggered
    /// </summary>
    public int TriggerCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Get the full webhook URL for this configuration
    /// </summary>
    public string GetWebhookUrl(string baseUrl)
    {
        return $"{baseUrl.TrimEnd('/')}/api/webhooks/github/{Id}";
    }

    /// <summary>
    /// Check if this webhook handles the given event type
    /// </summary>
    public bool HandlesEvent(string eventType)
    {
        if (Events == "*") return true;
        var events = Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return events.Contains(eventType, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if this webhook matches the given branch
    /// </summary>
    public bool MatchesBranch(string? branch)
    {
        if (string.IsNullOrEmpty(BranchFilter) || string.IsNullOrEmpty(branch))
            return true;

        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(branch, $"^{BranchFilter}$");
        }
        catch
        {
            // If regex is invalid, do exact match
            return branch.Equals(BranchFilter, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>
/// Log entry for GitHub webhook executions
/// </summary>
public class GitHubWebhookLog
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string WebhookId { get; set; } = string.Empty;

    [ForeignKey(nameof(WebhookId))]
    public GitHubWebhook? Webhook { get; set; }

    /// <summary>
    /// GitHub event type (push, pull_request, etc.)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// GitHub event action (opened, closed, created, etc.)
    /// </summary>
    [MaxLength(50)]
    public string? EventAction { get; set; }

    /// <summary>
    /// GitHub delivery ID
    /// </summary>
    [MaxLength(50)]
    public string? DeliveryId { get; set; }

    /// <summary>
    /// Repository that triggered this
    /// </summary>
    [MaxLength(200)]
    public string? Repository { get; set; }

    /// <summary>
    /// Branch or ref
    /// </summary>
    [MaxLength(200)]
    public string? Branch { get; set; }

    /// <summary>
    /// Sender username
    /// </summary>
    [MaxLength(100)]
    public string? Sender { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    public JobExecutionStatus Status { get; set; } = JobExecutionStatus.Pending;

    /// <summary>
    /// Rendered message that was sent
    /// </summary>
    public string? RenderedMessage { get; set; }

    /// <summary>
    /// Target of the action
    /// </summary>
    [MaxLength(200)]
    public string? Target { get; set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Full payload (JSON, for debugging)
    /// </summary>
    public string? Payload { get; set; }
}

/// <summary>
/// Common GitHub event types
/// </summary>
public static class GitHubEventTypes
{
    public const string Push = "push";
    public const string PullRequest = "pull_request";
    public const string PullRequestReview = "pull_request_review";
    public const string PullRequestReviewComment = "pull_request_review_comment";
    public const string Issues = "issues";
    public const string IssueComment = "issue_comment";
    public const string Create = "create";
    public const string Delete = "delete";
    public const string Release = "release";
    public const string Workflow = "workflow_run";
    public const string WorkflowJob = "workflow_job";
    public const string CheckRun = "check_run";
    public const string CheckSuite = "check_suite";
    public const string Deployment = "deployment";
    public const string DeploymentStatus = "deployment_status";
    public const string Star = "star";
    public const string Fork = "fork";
    public const string Watch = "watch";
    public const string CommitComment = "commit_comment";
    public const string Ping = "ping";

    public static readonly string[] All = new[]
    {
        Push, PullRequest, PullRequestReview, PullRequestReviewComment,
        Issues, IssueComment, Create, Delete, Release,
        Workflow, WorkflowJob, CheckRun, CheckSuite,
        Deployment, DeploymentStatus, Star, Fork, Watch, CommitComment
    };

    public static string GetDescription(string eventType) => eventType switch
    {
        Push => "Code pushed to repository",
        PullRequest => "Pull request opened, closed, or updated",
        PullRequestReview => "Pull request review submitted",
        PullRequestReviewComment => "Comment on pull request diff",
        Issues => "Issue opened, closed, or updated",
        IssueComment => "Comment on an issue",
        Create => "Branch or tag created",
        Delete => "Branch or tag deleted",
        Release => "Release published or updated",
        Workflow => "GitHub Actions workflow run",
        WorkflowJob => "GitHub Actions job",
        CheckRun => "Check run completed",
        CheckSuite => "Check suite completed",
        Deployment => "Deployment created",
        DeploymentStatus => "Deployment status changed",
        Star => "Repository starred",
        Fork => "Repository forked",
        Watch => "Repository watched",
        CommitComment => "Comment on a commit",
        _ => eventType
    };
}

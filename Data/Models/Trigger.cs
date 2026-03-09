using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// Represents an event-based trigger that fires actions based on conditions
/// </summary>
public class Trigger
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// The group/workspace this trigger belongs to
    /// </summary>
    [Required]
    public int GroupId { get; set; }

    [ForeignKey(nameof(GroupId))]
    public Group? Group { get; set; }

    /// <summary>
    /// Whether the trigger is currently active
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Type of event that activates this trigger
    /// </summary>
    [Required]
    public TriggerEventType EventType { get; set; }

    /// <summary>
    /// Source agent ID (for agent-specific events)
    /// null means any agent in the group
    /// </summary>
    public int? SourceAgentId { get; set; }

    [ForeignKey(nameof(SourceAgentId))]
    public Agent? SourceAgent { get; set; }

    /// <summary>
    /// Source team ID (for team-specific events)
    /// </summary>
    public int? SourceTeamId { get; set; }

    [ForeignKey(nameof(SourceTeamId))]
    public Team? SourceTeam { get; set; }

    /// <summary>
    /// Condition filter (JSON) - matches against event data
    /// Examples:
    /// - {"status": "Online"} - only trigger when status becomes Online
    /// - {"message_contains": "error"} - only trigger when message contains "error"
    /// - {"task_contains": "completed"} - only when task contains "completed"
    /// </summary>
    public string? ConditionJson { get; set; }

    /// <summary>
    /// Action to perform when trigger fires
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
    /// The message/prompt to send
    /// Supports variables: {event_type}, {source_agent}, {source_team}, {event_data},
    /// {date}, {time}, {datetime}, {agent_name}, {team_name}, {group_name}
    /// </summary>
    [Required]
    public string MessageTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Delay before executing action (in seconds)
    /// Useful for debouncing or waiting for conditions to stabilize
    /// </summary>
    public int DelaySeconds { get; set; } = 0;

    /// <summary>
    /// Cooldown period in seconds - prevents trigger from firing again within this window
    /// </summary>
    public int CooldownSeconds { get; set; } = 0;

    /// <summary>
    /// Last time this trigger fired
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// How many times this trigger has fired
    /// </summary>
    public int TriggerCount { get; set; } = 0;

    /// <summary>
    /// Maximum number of times this trigger can fire (null = unlimited)
    /// </summary>
    public int? MaxTriggers { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property for trigger logs
    /// </summary>
    public ICollection<TriggerLog> Logs { get; set; } = new List<TriggerLog>();
}

/// <summary>
/// Types of events that can trigger actions
/// </summary>
public enum TriggerEventType
{
    /// <summary>
    /// Agent status changed (Online, Offline, Working, etc.)
    /// </summary>
    AgentStatusChanged,

    /// <summary>
    /// Agent's current task changed
    /// </summary>
    AgentTaskChanged,

    /// <summary>
    /// Agent came online
    /// </summary>
    AgentOnline,

    /// <summary>
    /// Agent went offline
    /// </summary>
    AgentOffline,

    /// <summary>
    /// Agent started working on a task
    /// </summary>
    AgentStartedWorking,

    /// <summary>
    /// Agent finished working (became idle)
    /// </summary>
    AgentFinishedWorking,

    /// <summary>
    /// Message received in a team chat
    /// </summary>
    TeamMessageReceived,

    /// <summary>
    /// Direct message received by an agent
    /// </summary>
    DirectMessageReceived,

    /// <summary>
    /// File created in shared filesystem
    /// </summary>
    FileCreated,

    /// <summary>
    /// File modified in shared filesystem
    /// </summary>
    FileModified,

    /// <summary>
    /// Agent error occurred
    /// </summary>
    AgentError,

    /// <summary>
    /// Agent health check failed
    /// </summary>
    AgentHealthCheckFailed,

    /// <summary>
    /// New agent joined the group
    /// </summary>
    AgentJoinedGroup,

    /// <summary>
    /// Agent left the group
    /// </summary>
    AgentLeftGroup,

    /// <summary>
    /// Webhook received (external trigger)
    /// </summary>
    WebhookReceived,

    /// <summary>
    /// Custom event (triggered programmatically)
    /// </summary>
    CustomEvent
}

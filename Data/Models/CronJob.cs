using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// Represents a scheduled job that triggers agent prompts or messages
/// </summary>
public class CronJob
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this job does
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// The group/workspace this job belongs to
    /// </summary>
    [Required]
    public int GroupId { get; set; }

    [ForeignKey(nameof(GroupId))]
    public Group? Group { get; set; }

    /// <summary>
    /// Whether the job is currently active
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Type of schedule: Cron, Interval, Daily, Weekly, Monthly
    /// </summary>
    [Required]
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Cron;

    /// <summary>
    /// Cron expression (for ScheduleType.Cron)
    /// Standard format: minute hour day-of-month month day-of-week
    /// Examples: "0 9 * * 1-5" = 9am weekdays, "*/15 * * * *" = every 15 minutes
    /// </summary>
    [MaxLength(100)]
    public string? CronExpression { get; set; }

    /// <summary>
    /// Interval in minutes (for ScheduleType.Interval)
    /// </summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>
    /// Time of day to run (for Daily/Weekly/Monthly schedules)
    /// Stored as "HH:mm" format
    /// </summary>
    [MaxLength(5)]
    public string? TimeOfDay { get; set; }

    /// <summary>
    /// Days of week to run (for Weekly schedule)
    /// Comma-separated: "1,2,3,4,5" for weekdays, "0,6" for weekends
    /// 0 = Sunday, 6 = Saturday
    /// </summary>
    [MaxLength(20)]
    public string? DaysOfWeek { get; set; }

    /// <summary>
    /// Day of month to run (for Monthly schedule)
    /// 1-31, use 0 for last day of month
    /// </summary>
    public int? DayOfMonth { get; set; }

    /// <summary>
    /// What type of action to perform
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
    /// The message/prompt to send to the agent or team
    /// Supports variables: {date}, {time}, {datetime}, {agent_name}, {team_name}, {group_name}
    /// </summary>
    [Required]
    public string MessageTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Send message as if from this agent (impersonation)
    /// If null, sent from "System" or "CronScheduler"
    /// </summary>
    public int? SenderAgentId { get; set; }

    [ForeignKey(nameof(SenderAgentId))]
    public Agent? SenderAgent { get; set; }

    /// <summary>
    /// Maximum number of times to run this job (null = unlimited)
    /// </summary>
    public int? MaxExecutions { get; set; }

    /// <summary>
    /// How many times this job has executed
    /// </summary>
    public int ExecutionCount { get; set; } = 0;

    /// <summary>
    /// Last time this job was executed
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Result of last execution
    /// </summary>
    public JobExecutionStatus? LastRunStatus { get; set; }

    /// <summary>
    /// Error message from last failed execution
    /// </summary>
    [MaxLength(1000)]
    public string? LastRunError { get; set; }

    /// <summary>
    /// Next scheduled execution time
    /// </summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    /// Timezone for scheduling (IANA format, e.g., "America/New_York")
    /// Defaults to local timezone if not specified
    /// </summary>
    [MaxLength(50)]
    public string Timezone { get; set; } = TimeZoneInfo.Local.Id;

    /// <summary>
    /// Whether to skip if the target agent is offline
    /// </summary>
    public bool SkipIfAgentOffline { get; set; } = false;

    /// <summary>
    /// Whether to retry failed executions
    /// </summary>
    public bool RetryOnFailure { get; set; } = false;

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Optional: Expire this job after this date
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property for execution logs
    /// </summary>
    public ICollection<CronJobLog> Logs { get; set; } = new List<CronJobLog>();
}

/// <summary>
/// Type of schedule
/// </summary>
public enum ScheduleType
{
    /// <summary>
    /// Standard cron expression
    /// </summary>
    Cron,

    /// <summary>
    /// Run every X minutes
    /// </summary>
    Interval,

    /// <summary>
    /// Run once daily at a specific time
    /// </summary>
    Daily,

    /// <summary>
    /// Run on specific days of the week at a specific time
    /// </summary>
    Weekly,

    /// <summary>
    /// Run on a specific day of each month
    /// </summary>
    Monthly,

    /// <summary>
    /// Run once at a specific time (one-shot)
    /// </summary>
    OneTime
}

/// <summary>
/// Type of action to perform when job triggers
/// </summary>
public enum JobActionType
{
    /// <summary>
    /// Send a prompt/task to an agent (will appear in their inbox)
    /// </summary>
    PromptAgent,

    /// <summary>
    /// Send a direct message to an agent
    /// </summary>
    SendDirectMessage,

    /// <summary>
    /// Send a message to a team/group chat
    /// </summary>
    SendTeamMessage,

    /// <summary>
    /// Start an agent if it's offline
    /// </summary>
    StartAgent,

    /// <summary>
    /// Stop an agent if it's online
    /// </summary>
    StopAgent,

    /// <summary>
    /// Restart an agent
    /// </summary>
    RestartAgent,

    /// <summary>
    /// Run a webhook/HTTP request
    /// </summary>
    Webhook
}

/// <summary>
/// Status of a job execution
/// </summary>
public enum JobExecutionStatus
{
    Pending,
    Running,
    Success,
    Failed,
    Skipped,
    Cancelled
}

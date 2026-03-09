using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// Execution log entry for a cron job
/// </summary>
public class CronJobLog
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The cron job this log belongs to
    /// </summary>
    [Required]
    public string CronJobId { get; set; } = string.Empty;

    [ForeignKey(nameof(CronJobId))]
    public CronJob? CronJob { get; set; }

    /// <summary>
    /// When this execution was scheduled to run
    /// </summary>
    public DateTime ScheduledAt { get; set; }

    /// <summary>
    /// When execution started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When execution completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration of execution in milliseconds
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Status of this execution
    /// </summary>
    public JobExecutionStatus Status { get; set; } = JobExecutionStatus.Pending;

    /// <summary>
    /// The rendered message that was sent (after variable substitution)
    /// </summary>
    public string? RenderedMessage { get; set; }

    /// <summary>
    /// Target of the action (agent name, team name, etc.)
    /// </summary>
    [MaxLength(200)]
    public string? Target { get; set; }

    /// <summary>
    /// Response or result from the action
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Retry attempt number (0 for first attempt)
    /// </summary>
    public int RetryAttempt { get; set; } = 0;

    /// <summary>
    /// Additional metadata about the execution (JSON)
    /// </summary>
    public string? Metadata { get; set; }
}

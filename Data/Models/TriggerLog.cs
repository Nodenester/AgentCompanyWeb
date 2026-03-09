using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// Execution log entry for a trigger
/// </summary>
public class TriggerLog
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The trigger this log belongs to
    /// </summary>
    [Required]
    public string TriggerId { get; set; } = string.Empty;

    [ForeignKey(nameof(TriggerId))]
    public Trigger? Trigger { get; set; }

    /// <summary>
    /// When the triggering event occurred
    /// </summary>
    public DateTime EventAt { get; set; }

    /// <summary>
    /// When the action was executed
    /// </summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The event type that fired this trigger
    /// </summary>
    public TriggerEventType EventType { get; set; }

    /// <summary>
    /// Event data (JSON) - the data that matched the trigger condition
    /// </summary>
    public string? EventData { get; set; }

    /// <summary>
    /// Status of this execution
    /// </summary>
    public JobExecutionStatus Status { get; set; } = JobExecutionStatus.Pending;

    /// <summary>
    /// The rendered message that was sent
    /// </summary>
    public string? RenderedMessage { get; set; }

    /// <summary>
    /// Target of the action
    /// </summary>
    [MaxLength(200)]
    public string? Target { get; set; }

    /// <summary>
    /// Result or response from the action
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? Error { get; set; }
}

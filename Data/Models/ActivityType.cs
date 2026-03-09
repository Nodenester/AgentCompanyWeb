namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// Types of activities that can appear in the activity timeline.
/// </summary>
public enum ActivityType
{
    TaskStarted,
    TaskCompleted,
    TaskFailed,
    Message,
    StatusChange,
    Error,
    Trigger,
    Info
}

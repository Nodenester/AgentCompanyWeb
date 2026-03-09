namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// An Agent instance managed by the system.
/// </summary>
public class Agent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Group this agent belongs to.
    /// </summary>
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    /// <summary>
    /// Role/title visible to other agents (Manager, Lead, Developer, etc.)
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Custom system prompt additions (injected into CLAUDE.md).
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// AI model to use for this agent.
    /// </summary>
    public string Model { get; set; } = "sonnet";

    /// <summary>
    /// Current task description (visible to other agents).
    /// </summary>
    public string? CurrentTask { get; set; }

    /// <summary>
    /// Docker container ID when running.
    /// </summary>
    public string? ContainerId { get; set; }

    /// <summary>
    /// Current status of the agent.
    /// </summary>
    public AgentStatus Status { get; set; } = AgentStatus.Offline;

    /// <summary>
    /// URL to the VNC viewer for this agent.
    /// </summary>
    public string? VncUrl { get; set; }

    /// <summary>
    /// Health check URL for this agent.
    /// </summary>
    public string? HealthUrl { get; set; }

    /// <summary>
    /// X position on the visual canvas.
    /// </summary>
    public double CanvasX { get; set; } = 100;

    /// <summary>
    /// Y position on the visual canvas.
    /// </summary>
    public double CanvasY { get; set; } = 100;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Teams this agent belongs to.
    /// </summary>
    public ICollection<TeamMember> TeamMemberships { get; set; } = new List<TeamMember>();
}

/// <summary>
/// Possible agent statuses.
/// </summary>
public enum AgentStatus
{
    Offline,
    Starting,
    Online,
    Working,
    Error,
    Stopping
}

/// <summary>
/// Common agent roles for quick selection.
/// </summary>
public static class AgentRoles
{
    public static readonly string[] Suggested = [
        "Manager",
        "Team Lead",
        "Senior Developer",
        "Developer",
        "QA Engineer",
        "DevOps",
        "Architect",
        "Specialist"
    ];
}

/// <summary>
/// Available AI models for agents.
/// </summary>
public static class AgentModels
{
    public static readonly (string Id, string Name, string Description)[] All = [
        ("opus", "Claude Opus", "Most capable, best for complex tasks"),
        ("sonnet", "Claude Sonnet", "Balanced performance and speed"),
        ("haiku", "Claude Haiku", "Fast and efficient for simple tasks")
    ];
}

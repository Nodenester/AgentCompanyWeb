namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// A Group (Company) organizes agents together with shared credentials.
/// </summary>
public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Encrypted GitHub token for this group's agents.
    /// </summary>
    public string? GitHubTokenEncrypted { get; set; }

    /// <summary>
    /// Encrypted Anthropic API key for this group's agents.
    /// </summary>
    public string? AnthropicKeyEncrypted { get; set; }

    /// <summary>
    /// Accent color for UI (blue, purple, pink, orange, green, teal).
    /// </summary>
    public string Color { get; set; } = "blue";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Agents belonging to this group.
    /// </summary>
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();

    /// <summary>
    /// Teams (group chats) in this group.
    /// </summary>
    public ICollection<Team> Teams { get; set; } = new List<Team>();
}

/// <summary>
/// Available group colors for UI.
/// </summary>
public static class GroupColors
{
    public static readonly string[] All = ["blue", "purple", "pink", "orange", "green", "teal"];

    public static string GetCssClass(string color) => $"group-{color}";
}

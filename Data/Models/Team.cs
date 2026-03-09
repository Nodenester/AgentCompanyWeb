namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// A Team represents a group chat / working group within a company.
/// Agents can be connected to multiple teams.
/// </summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Group (company) this team belongs to.
    /// </summary>
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    /// <summary>
    /// Color for visual distinction.
    /// </summary>
    public string Color { get; set; } = "blue";

    /// <summary>
    /// X position on the visual canvas.
    /// </summary>
    public double CanvasX { get; set; } = 400;

    /// <summary>
    /// Y position on the visual canvas.
    /// </summary>
    public double CanvasY { get; set; } = 100;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Members of this team.
    /// </summary>
    public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
}

/// <summary>
/// Junction table for Agent-Team many-to-many relationship.
/// </summary>
public class TeamMember
{
    public int Id { get; set; }

    public int AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Service to detect and adapt UI for different swarm scales.
/// </summary>
public class SwarmScaleService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SwarmScaleService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Gets the current scale tier based on agent count.
    /// </summary>
    public async Task<SwarmScale> GetCurrentScaleAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var count = await db.Agents.CountAsync();
        return GetScaleFromCount(count);
    }

    /// <summary>
    /// Gets the scale tier from an agent count.
    /// </summary>
    public static SwarmScale GetScaleFromCount(int agentCount) => agentCount switch
    {
        <= 3 => SwarmScale.Solo,
        <= 15 => SwarmScale.Team,
        <= 30 => SwarmScale.Department,
        _ => SwarmScale.Enterprise
    };

    /// <summary>
    /// Gets UI configuration for a given scale.
    /// </summary>
    public static SwarmScaleConfig GetConfig(SwarmScale scale) => scale switch
    {
        SwarmScale.Solo => new SwarmScaleConfig
        {
            CardSize = "large",
            ShowNetworkGraph = false,
            DefaultView = "grid",
            ShowDetailedInfo = true,
            UseVirtualization = false,
            MaxVisibleItems = 10,
            NodeSize = 180,
            Description = "Personal workspace - detailed agent cards"
        },
        SwarmScale.Team => new SwarmScaleConfig
        {
            CardSize = "medium",
            ShowNetworkGraph = true,
            DefaultView = "network",
            ShowDetailedInfo = true,
            UseVirtualization = false,
            MaxVisibleItems = 20,
            NodeSize = 160,
            Description = "Team collaboration - full network view"
        },
        SwarmScale.Department => new SwarmScaleConfig
        {
            CardSize = "small",
            ShowNetworkGraph = true,
            DefaultView = "grid",
            ShowDetailedInfo = false,
            UseVirtualization = true,
            MaxVisibleItems = 50,
            NodeSize = 120,
            Description = "Department scale - clustered views"
        },
        SwarmScale.Enterprise => new SwarmScaleConfig
        {
            CardSize = "compact",
            ShowNetworkGraph = false,
            DefaultView = "list",
            ShowDetailedInfo = false,
            UseVirtualization = true,
            MaxVisibleItems = 100,
            NodeSize = 80,
            Description = "Enterprise scale - search-first, hierarchical"
        },
        _ => GetConfig(SwarmScale.Team)
    };

    /// <summary>
    /// Gets CSS class modifiers for the current scale.
    /// </summary>
    public static string GetScaleClass(SwarmScale scale) => scale switch
    {
        SwarmScale.Solo => "scale-solo",
        SwarmScale.Team => "scale-team",
        SwarmScale.Department => "scale-department",
        SwarmScale.Enterprise => "scale-enterprise",
        _ => "scale-team"
    };
}

/// <summary>
/// Swarm scale tiers based on agent count.
/// </summary>
public enum SwarmScale
{
    /// <summary>1-3 agents: Personal workspace</summary>
    Solo,

    /// <summary>4-15 agents: Team collaboration</summary>
    Team,

    /// <summary>16-30 agents: Department scale</summary>
    Department,

    /// <summary>31+ agents: Enterprise deployment</summary>
    Enterprise
}

/// <summary>
/// UI configuration for a scale tier.
/// </summary>
public class SwarmScaleConfig
{
    /// <summary>Card size: large, medium, small, compact</summary>
    public string CardSize { get; set; } = "medium";

    /// <summary>Whether to show the network graph visualization</summary>
    public bool ShowNetworkGraph { get; set; } = true;

    /// <summary>Default view mode: network, grid, or list</summary>
    public string DefaultView { get; set; } = "network";

    /// <summary>Whether to show detailed agent information</summary>
    public bool ShowDetailedInfo { get; set; } = true;

    /// <summary>Whether to use virtualization for lists</summary>
    public bool UseVirtualization { get; set; } = false;

    /// <summary>Maximum visible items before pagination/virtualization</summary>
    public int MaxVisibleItems { get; set; } = 20;

    /// <summary>Node size in pixels for network view</summary>
    public int NodeSize { get; set; } = 160;

    /// <summary>Human-readable description of this scale tier</summary>
    public string Description { get; set; } = "";
}

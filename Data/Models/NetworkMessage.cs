namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// A persisted message in the Agent Network (DM or group message).
/// </summary>
public class NetworkMessage
{
    public int Id { get; set; }

    /// <summary>
    /// Sender agent name.
    /// </summary>
    public string From { get; set; } = "";

    /// <summary>
    /// For DMs - recipient agent name. Null for group messages.
    /// </summary>
    public string? To { get; set; }

    /// <summary>
    /// For group messages - the group/team name. Null for DMs.
    /// </summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// Message content.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Server timestamp when message was received.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this is a direct message (true) or group message (false).
    /// </summary>
    public bool IsDirectMessage { get; set; }
}

/// <summary>
/// A connected agent in the network.
/// </summary>
public class NetworkConnection
{
    public int Id { get; set; }

    /// <summary>
    /// Unique agent name.
    /// </summary>
    public string AgentName { get; set; } = "";

    /// <summary>
    /// Current status: online, working, offline.
    /// </summary>
    public string Status { get; set; } = "online";

    /// <summary>
    /// What the agent is currently working on.
    /// </summary>
    public string? CurrentTask { get; set; }

    /// <summary>
    /// When agent connected.
    /// </summary>
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// SignalR connection ID for real-time updates.
    /// </summary>
    public string? ConnectionId { get; set; }
}

/// <summary>
/// A chat group in the network.
/// </summary>
public class NetworkGroup
{
    public int Id { get; set; }

    /// <summary>
    /// Group name (unique).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// When group was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Members of this group (agent names, comma-separated).
    /// </summary>
    public string Members { get; set; } = "";
}

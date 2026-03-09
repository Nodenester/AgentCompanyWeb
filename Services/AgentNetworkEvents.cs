using AgentCompanyWeb.Data.Models;

namespace AgentCompanyWeb.Services;

/// <summary>
/// In-process event service for real-time notifications within the web app.
/// Used by Blazor components to receive updates without SignalR client.
/// Also used by TriggerExecutorService to detect events and fire triggers.
/// </summary>
public class AgentNetworkEvents
{
    public event Action<MessageEventArgs>? OnMessageReceived;
    public event Action<AgentStatusEventArgs>? OnAgentStatusChanged;
    public event Action<string>? OnGroupCreated;
    public event Action<AgentTaskEventArgs>? OnAgentTaskChanged;
    public event Action<FileEventArgs>? OnFileChanged;
    public event Action<AgentErrorEventArgs>? OnAgentError;
    public event Action<TriggerEventArgs>? OnTriggerEvent;

    public void NotifyMessageReceived(int messageId, string from, string? to, string? groupName, string content, DateTime timestamp, bool isDirectMessage)
    {
        OnMessageReceived?.Invoke(new MessageEventArgs
        {
            MessageId = messageId,
            From = from,
            To = to,
            GroupName = groupName,
            Content = content,
            Timestamp = timestamp,
            IsDirectMessage = isDirectMessage
        });
    }

    public void NotifyAgentStatusChanged(string agentName, string status, string? currentTask = null, string? groupId = null)
    {
        OnAgentStatusChanged?.Invoke(new AgentStatusEventArgs
        {
            AgentName = agentName,
            Status = status,
            CurrentTask = currentTask,
            GroupId = groupId,
            Timestamp = DateTime.UtcNow
        });
    }

    public void NotifyAgentTaskChanged(string agentName, string? previousTask, string? newTask, string? groupId = null)
    {
        OnAgentTaskChanged?.Invoke(new AgentTaskEventArgs
        {
            AgentName = agentName,
            PreviousTask = previousTask,
            NewTask = newTask,
            GroupId = groupId,
            Timestamp = DateTime.UtcNow
        });
    }

    public void NotifyFileChanged(string path, FileChangeType changeType, string? groupId = null)
    {
        OnFileChanged?.Invoke(new FileEventArgs
        {
            Path = path,
            ChangeType = changeType,
            GroupId = groupId,
            Timestamp = DateTime.UtcNow
        });
    }

    public void NotifyAgentError(string agentName, string error, string? groupId = null)
    {
        OnAgentError?.Invoke(new AgentErrorEventArgs
        {
            AgentName = agentName,
            Error = error,
            GroupId = groupId,
            Timestamp = DateTime.UtcNow
        });
    }

    public void NotifyGroupCreated(string groupName)
    {
        OnGroupCreated?.Invoke(groupName);
    }

    /// <summary>
    /// Fire a custom trigger event
    /// </summary>
    public void FireTriggerEvent(TriggerEventType eventType, string groupId, Dictionary<string, object?> eventData)
    {
        OnTriggerEvent?.Invoke(new TriggerEventArgs
        {
            EventType = eventType,
            GroupId = groupId,
            EventData = eventData,
            Timestamp = DateTime.UtcNow
        });
    }
}

public class MessageEventArgs
{
    public int MessageId { get; set; }
    public string From { get; set; } = "";
    public string? To { get; set; }
    public string? GroupName { get; set; }
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public bool IsDirectMessage { get; set; }
}

public class AgentStatusEventArgs
{
    public string AgentName { get; set; } = "";
    public string Status { get; set; } = "offline";
    public string? CurrentTask { get; set; }
    public string? GroupId { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AgentTaskEventArgs
{
    public string AgentName { get; set; } = "";
    public string? PreviousTask { get; set; }
    public string? NewTask { get; set; }
    public string? GroupId { get; set; }
    public DateTime Timestamp { get; set; }
}

public class FileEventArgs
{
    public string Path { get; set; } = "";
    public FileChangeType ChangeType { get; set; }
    public string? GroupId { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum FileChangeType
{
    Created,
    Modified,
    Deleted
}

public class AgentErrorEventArgs
{
    public string AgentName { get; set; } = "";
    public string Error { get; set; } = "";
    public string? GroupId { get; set; }
    public DateTime Timestamp { get; set; }
}

public class TriggerEventArgs
{
    public TriggerEventType EventType { get; set; }
    public string GroupId { get; set; } = "";
    public Dictionary<string, object?> EventData { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

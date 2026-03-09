namespace AgentCompanyWeb.Services;

/// <summary>
/// Service for displaying toast notifications across the application.
/// </summary>
public class ToastService
{
    public event Action<ToastMessage>? OnToast;
    public event Action<Guid>? OnDismiss;
    public event Action? OnDismissAll;

    private readonly List<ToastMessage> _toasts = new();

    public IReadOnlyList<ToastMessage> Toasts => _toasts.AsReadOnly();

    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 5000, string? actionText = null, Action? onAction = null)
    {
        var toast = new ToastMessage
        {
            Id = Guid.NewGuid(),
            Message = message,
            Type = type,
            DurationMs = durationMs,
            ActionText = actionText,
            OnAction = onAction,
            CreatedAt = DateTime.Now
        };

        _toasts.Add(toast);
        OnToast?.Invoke(toast);
    }

    public void Success(string message, int durationMs = 5000)
        => Show(message, ToastType.Success, durationMs);

    public void Warning(string message, int durationMs = 6000)
        => Show(message, ToastType.Warning, durationMs);

    public void Error(string message, int durationMs = 8000)
        => Show(message, ToastType.Error, durationMs);

    public void Info(string message, int durationMs = 5000)
        => Show(message, ToastType.Info, durationMs);

    public void ShowWithAction(string message, ToastType type, string actionText, Action onAction, int durationMs = 8000)
        => Show(message, type, durationMs, actionText, onAction);

    public void Dismiss(Guid id)
    {
        var toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast != null)
        {
            _toasts.Remove(toast);
            OnDismiss?.Invoke(id);
        }
    }

    public void DismissAll()
    {
        _toasts.Clear();
        OnDismissAll?.Invoke();
    }
}

public class ToastMessage
{
    public Guid Id { get; set; }
    public string Message { get; set; } = "";
    public ToastType Type { get; set; } = ToastType.Info;
    public int DurationMs { get; set; } = 5000;
    public string? ActionText { get; set; }
    public Action? OnAction { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

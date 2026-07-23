using System;

// Placeholder IAiSessionRunner for a provider that's registered (shows up in the picker) but
// doesn't have a real backend yet - Send() reports a friendly error through the normal OnError
// event instead of the picker UI needing its own special-case "this one doesn't work" path, and
// instead of silently doing nothing or throwing out of Send().
public class NotImplementedSessionRunner : IAiSessionRunner
{
    public event Action<string> OnSessionStarted;
    public event Action<string> OnAssistantText;
    public event Action<string> OnToolActivity;
    public event Action OnTurnComplete;
    public event Action<string> OnError;

    private readonly string providerDisplayName;

    public bool IsBusy => false;
    public string SessionId => null;

    public NotImplementedSessionRunner(string providerDisplayName)
    {
        this.providerDisplayName = providerDisplayName;
    }

    public void ResetSession()
    {
    }

    public void RestoreSession(string knownSessionId)
    {
    }

    public void Send(string message)
    {
        OnError?.Invoke($"{providerDisplayName}은(는) 아직 지원하지 않습니다. 설정에서 다른 AI를 선택해주세요.");
    }

    public void Kill()
    {
    }

    public void Dispose()
    {
    }
}

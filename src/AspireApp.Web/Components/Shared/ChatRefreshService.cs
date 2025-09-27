namespace AspireApp.Web.Components.Shared;

public class ChatRefreshService
{
    public event Action? OnRefresh;

    public void NotifyRefresh()
    {
        OnRefresh?.Invoke();
    }
}

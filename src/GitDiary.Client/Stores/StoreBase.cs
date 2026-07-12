namespace GitDiary.Client.Stores;

/// <summary>
/// Base class for stores that notify state changes.
/// </summary>
public abstract class StoreBase
{
    public event Action? StateChanged;

    protected void NotifyStateChanged() => StateChanged?.Invoke();
}

namespace GitDiary.Client.Models;

public enum SyncState
{
    Synced,
    Saving,
    Pending,
    Conflict,
    Failed
}

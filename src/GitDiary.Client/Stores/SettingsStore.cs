using GitDiary.Client.Models;

namespace GitDiary.Client.Stores;

public sealed class SettingsStore : StoreBase
{
    private RepositoryConfig? _config;
    private bool _isConfigured;

    public RepositoryConfig? Config
    {
        get => _config;
        private set
        {
            _config = value;
            NotifyStateChanged();
        }
    }

    public bool IsConfigured
    {
        get => _isConfigured;
        private set
        {
            _isConfigured = value;
            NotifyStateChanged();
        }
    }

    public void SetConfig(RepositoryConfig config)
    {
        Config = config;
        IsConfigured = !string.IsNullOrEmpty(config.Owner) &&
                       !string.IsNullOrEmpty(config.Repo) &&
                       !string.IsNullOrEmpty(config.Token);
    }

    public void ClearConfig()
    {
        Config = null;
        IsConfigured = false;
    }
}

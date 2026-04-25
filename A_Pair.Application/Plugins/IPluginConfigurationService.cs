namespace A_Pair.Application.Plugins
{
    public interface IPluginConfigurationService
    {
        Task<T?> LoadConfigurationAsync<T> (string pluginId , CancellationToken cancellationToken = default) where T : class, new();
        Task SaveConfigurationAsync<T> (string pluginId , T configuration , CancellationToken cancellationToken = default) where T : class;
        void WatchConfiguration (string pluginId , Action<string> onChange);
    }
}
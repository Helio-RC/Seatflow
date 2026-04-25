using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;

namespace A_Pair.Core.Providers
{
    public interface IAppSettingsRepository
    {
        Task<AppSettings> LoadAsync (CancellationToken cancellationToken = default);
        Task SaveAsync (AppSettings settings , CancellationToken cancellationToken = default);
        string SettingsFilePath { get; }
    }
}
using SeatFlow.Core.Models;

namespace SeatFlow.Core.Providers
{
    /// <summary>
    /// 应用程序设置仓储接口，定义 <see cref="AppSettings"/> 的持久化契约。
    /// 默认实现为 JSON 文件存储。
    /// </summary>
    public interface IAppSettingsRepository
    {
        /// <summary>加载应用程序设置。</summary>
        Task<AppSettings> LoadAsync (CancellationToken cancellationToken = default);

        /// <summary>保存应用程序设置。</summary>
        Task SaveAsync (AppSettings settings , CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置文件是否存在（走存储抽象，WASM 端不可用 File.Exists）。
        /// 用于首次启动检测与默认设置写入判断。
        /// </summary>
        Task<bool> ExistsAsync (CancellationToken cancellationToken = default);

        /// <summary>设置文件的路径。</summary>
        string SettingsFilePath { get; }
    }
}
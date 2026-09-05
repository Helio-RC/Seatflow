using System;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Avalonia.Services.Web;

/// <summary>
/// 浏览器端（WASM）更新服务（Post-install 更新不适用于浏览器，恒为 NotInstalled）。
/// </summary>
public sealed class WebNoopUpdateService : IUpdateService
{
    public UpdateServiceStatus Status => UpdateServiceStatus.NotInstalled;

    public bool UpdatePendingRestart => false;

    public Task<UpdateCheckResult> CheckForUpdatesAsync (CancellationToken ct = default)
        => Task.FromResult(new UpdateCheckResult
        {
            ServiceStatus = UpdateServiceStatus.NotInstalled ,
            CurrentVersion = VersionInfo.Version
        });

    public Task DownloadUpdatesAsync (IProgress<int>? progress = null , CancellationToken ct = default)
        => Task.CompletedTask;

    public void ApplyUpdatesAndRestart () { }

    public Task<string?> FetchReleaseNotesAsync (string? version = null , CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public string GetGitHubReleasesUrl (string? version = null)
        => "https://github.com/SeatFlow/SeatFlow/releases";
}

using System;
using System.Threading.Tasks;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 首次设置（OOBE）启动/完成契约。实现为 MainView 内的覆盖层，不是 PageKey。
/// </summary>
public interface IOobeService
{
    /// <summary>全屏 OOBE 或交接卡当前是否活跃。</summary>
    bool IsActive { get; }

    /// <summary>启动 OOBE；<paramref name="isRestart"/> 为 true 时从名单步骤开始且不重复询问遥测。</summary>
    Task StartAsync(bool isRestart = false);

    /// <summary>完成/跳过 OOBE，并在成功时持久化 IsFirstLaunch=false。</summary>
    Task CompleteAsync(bool skipped);

    /// <summary>OOBE 完成（含跳过）后触发。</summary>
    event EventHandler? Completed;
}

using System.Collections.Generic;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 启动时自动导入 <c>.seatsets</c> 的结果，供 OOBE 名单/会场步骤展示恢复提示。
/// 由 <c>App</c> 在启动初始化阶段写入，DI 单例。
/// </summary>
public sealed class StartupRestoreState
{
    /// <summary>本次启动是否恢复了至少一个文件。</summary>
    public bool SeatSetsImported { get; set; }

    /// <summary>成功恢复的文件数。</summary>
    public int RestoredFiles { get; set; }

    /// <summary>恢复失败的文件数。</summary>
    public int FailedFiles { get; set; }

    /// <summary>恢复过程中的错误信息（最多由导入服务返回的数量）。</summary>
    public IReadOnlyList<string> Errors { get; set; } = [];
}

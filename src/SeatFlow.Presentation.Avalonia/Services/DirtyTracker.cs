using System;
using ReactiveUI;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 统一脏检查（替代 5 套手写实现）：
/// 页面在载入/保存后调用 <see cref="MarkClean"/> 记录快照；状态变化时调用
/// <see cref="Update"/> 传入当前快照，由本类比较并驱动 <see cref="IsDirty"/>。
/// </summary>
public sealed class DirtyTracker : ReactiveObject
{
    private bool _isDirty;
    private string? _cleanSnapshot;

    /// <summary>是否存在未保存的更改（可直接绑定 UI）。</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    /// <summary>当前干净快照（测试/诊断用）。</summary>
    public string? CleanSnapshot => _cleanSnapshot;

    /// <summary>以当前状态作为干净基线（载入完成 / 保存成功后调用）。</summary>
    public void MarkClean(string currentSnapshot)
    {
        _cleanSnapshot = currentSnapshot;
        IsDirty = false;
    }

    /// <summary>传入当前状态快照，重新计算脏状态。</summary>
    public void Update(string currentSnapshot)
    {
        IsDirty = _cleanSnapshot is not null
            && !string.Equals(_cleanSnapshot, currentSnapshot, StringComparison.Ordinal);
    }

    /// <summary>强制标记为脏（例如新增了无法序列化的编辑）。</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>重置为未跟踪状态。</summary>
    public void Reset()
    {
        _cleanSnapshot = null;
        IsDirty = false;
    }
}

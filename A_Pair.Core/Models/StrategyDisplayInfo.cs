namespace A_Pair.Core.Models;

/// <summary>
/// 策略的完整展示信息，合并了不可变的 Manifest 和用户可修改的 Config。
/// 在 UI 层作为策略详情的数据源。
/// </summary>
public sealed class StrategyDisplayInfo
{
    // ── 来自 Manifest（不可变） ──

    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Source { get; init; } = "builtin";
    public int DefaultPriority { get; init; }
    public bool DefaultEnabled { get; init; } = true;

    // ── 来自 Config（用户可修改） ──

    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public Dictionary<string, object?> Parameters { get; set; } = [];

    // ── 便捷判断 ──

    public bool IsBuiltIn => Source == "builtin";
    public bool IsModified => Priority != DefaultPriority || IsEnabled != DefaultEnabled;
}

using System;

namespace SeatFlow.Presentation.Avalonia.Tokens;

/// <summary>
/// 方向 B 动效令牌（03-ui-redesign §5 动效政策）。
/// 以 C# 常量提供，供 XAML 经 {x:Static} 用于 Transitions 的 Duration（TimeSpan 无法在 XAML 中直接构造）。
/// </summary>
public static class MotionTokens
{
    /// <summary>悬停/按下反馈（80–120ms）。</summary>
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(120);

    /// <summary>侧栏折叠 / 浮层出现 / 引导卡片（150–180ms）。</summary>
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(180);

    /// <summary>对话框淡入 + 缩放（120ms）。</summary>
    public static readonly TimeSpan Modal = TimeSpan.FromMilliseconds(120);
}

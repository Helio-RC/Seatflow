using System;
using System.Collections.Generic;
using Avalonia;
using CodeWF.AvaloniaControls.Controls;

namespace SeatFlow.Presentation.Avalonia.Controls;

/// <summary>
/// 阶段进度指示器：Guide 控件内部按"步骤"同步 <see cref="GuideIndicator.StepCount"/>/
/// <see cref="GuideIndicator.ActiveIndex"/>，但启动引导的 UI 语义是"阶段"进度。
/// 本指示器通过 <see cref="PhaseStartIndexes"/>（各阶段起始步骤索引）把当前步骤映射为
/// 阶段序号，显示为 "n / m"。无论 Guide 何时重新同步，显示始终保持阶段语义，
/// 且不会因步骤数过多把"下一步"按钮挤出卡片。
/// </summary>
public sealed class PhaseGuideIndicator : GuideIndicator
{
    /// <summary>各阶段起始步骤索引（升序）。为 null/空时退化为按步骤显示。</summary>
    public static readonly StyledProperty<IReadOnlyList<int>?> PhaseStartIndexesProperty =
        AvaloniaProperty.Register<PhaseGuideIndicator, IReadOnlyList<int>?>(nameof(PhaseStartIndexes));

    public static readonly DirectProperty<PhaseGuideIndicator, string> TextProperty =
        AvaloniaProperty.RegisterDirect<PhaseGuideIndicator, string>(nameof(Text), indicator => indicator.Text);

    private string _text = string.Empty;

    /// <summary>待显示的进度文本（如 "3 / 9"）。</summary>
    public string Text
    {
        get => _text;
        private set => SetAndRaise(TextProperty, ref _text, value);
    }

    public IReadOnlyList<int>? PhaseStartIndexes
    {
        get => GetValue(PhaseStartIndexesProperty);
        set => SetValue(PhaseStartIndexesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StepCountProperty
            || change.Property == ActiveIndexProperty
            || change.Property == PhaseStartIndexesProperty)
        {
            UpdateText();
        }
    }

    private void UpdateText()
    {
        if (ActiveIndex < 0 || StepCount <= 0)
        {
            Text = string.Empty;
            return;
        }

        var phaseStarts = PhaseStartIndexes;
        if (phaseStarts is null || phaseStarts.Count == 0)
        {
            // 无阶段定义（如页面引导）：按步骤显示
            Text = $"{ActiveIndex + 1} / {StepCount}";
            return;
        }

        var phaseIndex = 0;
        for (var i = phaseStarts.Count - 1; i >= 0; i--)
        {
            if (phaseStarts[i] <= ActiveIndex)
            {
                phaseIndex = i;
                break;
            }
        }

        Text = $"{phaseIndex + 1} / {phaseStarts.Count}";
    }
}

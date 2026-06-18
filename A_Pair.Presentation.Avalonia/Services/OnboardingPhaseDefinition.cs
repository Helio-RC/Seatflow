using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace A_Pair.Presentation.Avalonia.Services;

/// <summary>引导配置文件根对象。</summary>
public sealed class OnboardingConfig
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.0";

    [JsonPropertyName("phases")]
    public List<OnboardingPhaseDefinition> Phases { get; set; } = [];
}

/// <summary>引导的一个阶段（对应一个页面）。</summary>
public sealed class OnboardingPhaseDefinition
{
    /// <summary>
    /// 此阶段要导航到的页面（<see cref="Services.PageKey"/> 枚举名称）。
    /// <c>null</c> 表示留在当前页。
    /// </summary>
    [JsonPropertyName("page")]
    public string? Page { get; set; }

    [JsonPropertyName("steps")]
    public List<OnboardingStepDefinition> Steps { get; set; } = [];
}

/// <summary>引导的一个步骤（对应 Guide 控件的一个 GuideStepOption）。</summary>
public sealed class OnboardingStepDefinition
{
    /// <summary>指向 <c>Resources.resx</c> 中标题字符串的键名。</summary>
    [JsonPropertyName("titleKey")]
    public string TitleKey { get; set; } = "";

    /// <summary>指向 <c>Resources.resx</c> 中描述字符串的键名。</summary>
    [JsonPropertyName("descKey")]
    public string DescKey { get; set; } = "";

    /// <summary>
    /// 目标控件的 <c>x:Name</c>。支持分号分隔的候选项（取第一个找到的）。
    /// 空字符串表示居中模态，无高亮目标。
    /// </summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    /// <summary>弹窗位置：Top / Bottom / Left / Right / Center。默认 Right。</summary>
    [JsonPropertyName("placement")]
    public string Placement { get; set; } = "Right";

    /// <summary>是否显示遮罩动画。居中纯文本步骤可设为 false。</summary>
    [JsonPropertyName("showMask")]
    public bool ShowMask { get; set; } = true;

    /// <summary>是否显示指向箭头。居中纯文本步骤可设为 false。</summary>
    [JsonPropertyName("showArrow")]
    public bool ShowArrow { get; set; } = true;
}

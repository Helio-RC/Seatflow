namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>OOBE 交接准备结果：会场/数据集是否已成功选中并加载。</summary>
public sealed record OobeHandoffPreparation(bool VenueReady, bool DatasetReady, string? Error)
{
    public bool IsReady => VenueReady && DatasetReady;
}

/// <summary>一次排座生成的结果（供 OOBE 交接卡消费）。</summary>
public enum SeatingGenerationStatus
{
    Success,
    Failed,
    Canceled,
    MissingData,
}

/// <summary>缺数据时具体缺失的输入；用于 OOBE 精确提示并跳转到对应步骤。</summary>
public enum SeatingMissingData
{
    None,
    Venue,
    Dataset,
    Both,
}

/// <summary>排座生成完成事件参数。</summary>
public sealed record SeatingGenerationResult(
    SeatingGenerationStatus Status,
    string? Message = null,
    SeatingMissingData MissingData = SeatingMissingData.None);

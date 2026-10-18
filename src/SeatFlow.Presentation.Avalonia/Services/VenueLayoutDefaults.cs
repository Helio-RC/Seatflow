namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 新建 Grid 会场的统一默认参数：OOBE 会场步骤与会场管理页「新建会场」共用，
/// 避免两处默认值漂移导致「行列/过道相同但布局不同」。
/// </summary>
public static class VenueLayoutDefaults
{
    public const int GridRows = 5;
    public const int GridColumns = 8;
    public const double GridHorizontalSpacing = 64;
    public const double GridVerticalSpacing = 56;
    public const double GridOriginX = 200;
    public const double GridOriginY = 200;
    public const int GridSeatsPerDesk = 2;
    public const double GridIntraDeskSpacing = 40;
    public const double GridInterDeskSpacing = 32;
    public const double GridAisleWidth = 60;
    public const int GridFrontRowCount = 1;
    public const bool GridHasPodium = true;
    public const double GridPodiumWidth = 100;
    public const double GridPodiumHeight = 40;
}

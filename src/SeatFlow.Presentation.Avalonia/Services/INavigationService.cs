using System;
using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Services;

public enum PageKey
{
    // M3：取消独立 Home，默认入口改为排座工作台（SeatingArrangement）
    MemberManagement,
    VenueConfiguration,
    StrategyConfiguration,
    SeatingArrangement,
    SnapshotHistory,
    Settings,
    About
}

public interface INavigationService
{
    ViewModelBase CurrentViewModel { get; }
    PageKey CurrentPage { get; }
    event Action? CurrentViewModelChanged;
    void NavigateTo(PageKey page);
    Task<bool> NavigateToAsync(PageKey page);
}

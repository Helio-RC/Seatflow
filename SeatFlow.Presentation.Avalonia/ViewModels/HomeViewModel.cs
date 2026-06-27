using SeatFlow.Presentation.Avalonia.Lang;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    public string Greeting { get; } = Resources.Home_Greeting;
    public string Subtitle { get; } = Resources.Home_Subtitle;
}

using A_Pair.Presentation.Avalonia.Lang;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    public string Greeting { get; } = Resources.Home_Greeting;
    public string Subtitle { get; } = Resources.Home_Subtitle;
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
#if USE_RUI
using ReactiveUI.Avalonia;
#endif

namespace ReactiveUiWasm;

#if USE_RUI
public sealed partial class MainView : ReactiveUserControl<MainViewModel>
#else
public sealed partial class MainView : UserControl
#endif
{
    public MainView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new MainViewModel();
    }
}

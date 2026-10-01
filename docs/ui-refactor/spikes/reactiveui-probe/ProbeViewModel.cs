using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ReactiveUiProbe;

// 最小 spike：验证 ReactiveUI.Avalonia 12.1.5 + ReactiveUI 25.1.1 在 net10.0 / net10.0-browser 下可编译。
// 覆盖：ReactiveObject、源生成器 [Reactive]、ReactiveCommand。
public sealed partial class ProbeViewModel : ReactiveObject
{
    [Reactive] private string _name = "seed";

    public string Greeting => $"Hello {Name}";

    public IReactiveCommand ResetCommand { get; }

    public ProbeViewModel()
    {
        ResetCommand = ReactiveCommand.Create(() => { });
    }
}

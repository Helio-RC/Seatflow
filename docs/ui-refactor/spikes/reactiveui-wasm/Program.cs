using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
#if USE_RUI
using ReactiveUI.Avalonia;
#endif

namespace ReactiveUiWasm;

internal sealed class Program
{
    private static async Task Main(string[] args)
        => await BuildAvaloniaApp().StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UseBrowser();
#if USE_RUI
        builder = builder.UseReactiveUI(_ => { });
#endif
        return builder;
    }
}

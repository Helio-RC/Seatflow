using System.Threading.Tasks;
using A_Pair.Presentation.Avalonia.Views;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Services;

public class DialogService : IDialogService
{
    private TopLevel? _topLevel;

    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;

    public Task ShowErrorAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Error);

    public Task ShowWarningAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Warning);

    public Task ShowInfoAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Info);

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        if (_topLevel is not Window window) return false;

        var dialog = new DialogWindow
        {
            Title = title,
            Message = message,
            Kind = DialogKind.Confirm
        };

        return await dialog.ShowDialog<bool>(window) == true;
    }

    public async Task<(bool Confirmed, string Input)> ShowInputAsync(string title, string prompt, string initialValue = "")
    {
        if (_topLevel is not Window window) return (false, "");

        var dialog = new InputWindow
        {
            Title = title,
            Prompt = prompt,
            Input = initialValue
        };

        var result = await dialog.ShowDialog<bool>(window);
        return (result, result ? dialog.Input : "");
    }

    private async Task ShowAsync(string title, string message, DialogKind kind)
    {
        if (_topLevel is not Window window) return;

        var dialog = new DialogWindow
        {
            Title = title,
            Message = message,
            Kind = kind
        };

        await dialog.ShowDialog<bool>(window);
    }
}

using System.Threading.Tasks;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Services;

public interface IDialogService
{
    void SetTopLevel(TopLevel topLevel);
    Task ShowErrorAsync(string title, string message);
    Task ShowWarningAsync(string title, string message);
    Task ShowInfoAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
}

using System;
using System.Threading.Tasks;
using A_Pair.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>全局对话框服务，由 App 启动时注入。</summary>
    internal static IDialogService Dialog { get; private set; } = default!;

    /// <summary>由 DI 在应用启动时调用一次。</summary>
    public static void InitializeDialogService (IDialogService dialog)
    {
        Dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
    }

    /// <summary>在 try-catch 中执行操作，出错时弹窗。</summary>
    protected async Task<bool> SafeExecuteAsync (Func<Task> action , string errorTitle = "操作失败")
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            await Dialog.ShowErrorAsync(errorTitle , ex.Message);
            return false;
        }
    }
}

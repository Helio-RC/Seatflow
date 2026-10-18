using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Avalonia.Views;

/// <summary>首次设置全屏向导。仅处理步骤焦点，业务状态全部在 <see cref="OobeViewModel"/>。</summary>
public partial class OobeView : UserControl
{
    private OobeViewModel? _viewModel;

    public OobeView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachViewModel(DataContext as OobeViewModel);
        ScheduleFocus();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachViewModel(null);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModel(DataContext as OobeViewModel);
    }

    private void AttachViewModel(OobeViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OobeViewModel.IsVisible) or nameof(OobeViewModel.Step))
            ScheduleFocus();
    }

    private void ScheduleFocus()
    {
        if (_viewModel is null || !_viewModel.IsVisible) return;

        // 手机档欢迎/名单/会场页均长于一屏：自动聚焦底部主按钮会把页面滚到底部，
        // 首屏反而看不到标题与步骤。手机档交给用户按需滚动。
        if (_viewModel.Layout.IsPhone) return;

        Dispatcher.UIThread.Post(FocusPrimary, DispatcherPriority.Background);
    }

    private void FocusPrimary()
    {
        if (_viewModel is null || !_viewModel.IsVisible) return;

        var name = _viewModel.Step switch
        {
            OobeStep.Welcome => "WelcomeStartButton",
            OobeStep.Preferences => "PreferencesNextButton",
            // 名单/会场是长表单，自动聚焦底部主按钮会把页面滚到底部、看不到页首。
            _ => null,
        };

        if (name is null) return;
        var button = this.FindControl<Button>(name);
        if (button is { IsEffectivelyVisible: true })
            button.Focus();
    }
}

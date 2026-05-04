using System;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvaloniaApplication = Avalonia.Application;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IDialogService _dialog;

    [ObservableProperty]
    private ThemeMode _theme;

    [ObservableProperty]
    private string _language = string.Empty;

    [ObservableProperty]
    private string _dataDirectory = string.Empty;

    [ObservableProperty]
    private int _autoSaveIntervalSeconds = 300;

    [ObservableProperty]
    private bool _confirmBeforeClear = true;

    [ObservableProperty]
    private double _defaultZoomLevel = 1.0;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private PageTransitionType _transitionAnimation;

    [ObservableProperty]
    private int _transitionAnimationIndex;

    [ObservableProperty]
    private bool _isSaving;

    public SettingsViewModel(IApplicationFacade facade, IDialogService dialog)
    {
        _facade = facade;
        _dialog = dialog;
        _ = LoadAsync(CancellationToken.None);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync(ct);
            Theme = settings.Theme;
            Language = settings.Language;
            DataDirectory = settings.DataDirectory;
            AutoSaveIntervalSeconds = settings.AutoSaveIntervalSeconds;
            ConfirmBeforeClear = settings.ConfirmBeforeClear;
            DefaultZoomLevel = settings.DefaultZoomLevel;
            TransitionAnimation = settings.TransitionAnimation;
            TransitionAnimationIndex = (int)settings.TransitionAnimation;
            ThemeIndex = Theme switch
            {
                ThemeMode.Light => 1,
                ThemeMode.Dark => 2,
                _ => 0
            };
        }
        catch
        {
            StatusMessage = "加载设置失败，将使用默认值";
        }
    }

    partial void OnThemeIndexChanged(int value)
    {
        var mode = value switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        if (Theme == mode) return;
        Theme = mode;

        if (AvaloniaApplication.Current is { } app)
        {
            app.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    partial void OnTransitionAnimationIndexChanged(int value)
    {
        if (!Enum.IsDefined(typeof(PageTransitionType), value)) return;
        var type = (PageTransitionType)value;
        if (TransitionAnimation == type) return;
        TransitionAnimation = type;

        if (AvaloniaApplication.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.DataContext is MainShellViewModel shell)
        {
            shell.ApplyTransitionType(type);
        }
    }

    [RelayCommand]
    private void SetTransitionAnimation(string parameter)
    {
        if (int.TryParse(parameter, out var index))
            TransitionAnimationIndex = index;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync(CancellationToken ct)
    {
        try
        {
            IsSaving = true;
            StatusMessage = "正在保存...";

            var settings = new AppSettings
            {
                Theme = Theme,
                Language = Language,
                DataDirectory = DataDirectory,
                AutoSaveIntervalSeconds = AutoSaveIntervalSeconds,
                ConfirmBeforeClear = ConfirmBeforeClear,
                DefaultZoomLevel = DefaultZoomLevel,
                TransitionAnimation = TransitionAnimation
            };

            await _facade.SaveAppSettingsAsync(settings, ct);
            StatusMessage = "设置已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存失败";
            await _dialog.ShowErrorAsync("保存设置失败", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ResetDefaultsAsync()
    {
        var confirmed = await _dialog.ShowConfirmAsync("重置设置",
            "确定要恢复所有设置为默认值吗？");
        if (!confirmed) return;

        Theme = ThemeMode.System;
        Language = string.Empty;
        DataDirectory = string.Empty;
        AutoSaveIntervalSeconds = 300;
        ConfirmBeforeClear = true;
        DefaultZoomLevel = 1.0;
        TransitionAnimation = PageTransitionType.CrossFade;
        TransitionAnimationIndex = (int)PageTransitionType.CrossFade;
        ThemeIndex = 0;
        StatusMessage = "已恢复默认值（尚未保存）";
    }

    [RelayCommand]
    private void SelectTheme(string parameter)
    {
        if (int.TryParse(parameter, out var index))
            ThemeIndex = index;
    }

    [RelayCommand]
    private void SetAutoSave(string parameter)
    {
        if (int.TryParse(parameter, out var seconds))
            AutoSaveIntervalSeconds = seconds;
    }

    [RelayCommand]
    private void SetZoom(string parameter)
    {
        if (double.TryParse(parameter, out var zoom))
            DefaultZoomLevel = zoom;
    }

    [RelayCommand]
    private async Task BrowseDataDirectoryAsync(CancellationToken ct)
    {
        try
        {
            if (AvaloniaApplication.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var storageProvider = desktop.MainWindow?.StorageProvider;
            if (storageProvider is null) return;

            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择数据目录",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                DataDirectory = folders[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("选择目录失败", ex.Message);
        }
    }
}

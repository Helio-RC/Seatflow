using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AvaloniaApplication = Avalonia.Application;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IDialogService _dialog;
    private readonly ILogger<SettingsViewModel> _logger;

    // ---- 外观 ----

    [ObservableProperty]
    private ThemeMode _theme;

    [ObservableProperty]
    private int _themeIndex;

    public List<string> ThemeOptions { get; } = ["跟随系统" , "浅色" , "深色"];

    // ---- 语言 ----

    [ObservableProperty]
    private string _language = string.Empty;

    // ---- 数据目录 ----

    [ObservableProperty]
    private string _dataDirectory = string.Empty;

    // ---- 清除确认 ----

    [ObservableProperty]
    private bool _confirmBeforeClear = true;

    // ---- 默认缩放 ----

    [ObservableProperty]
    private int _zoomIndex = 1;

    public List<string> ZoomOptions { get; } = ["75%" , "100%" , "125%" , "150%"];

    private double _defaultZoomLevel = 1.0;

    // ---- 通用 ----

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    public SettingsViewModel (IApplicationFacade facade , IDialogService dialog , ILogger<SettingsViewModel>? logger = null)
    {
        _facade = facade;
        _dialog = dialog;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        _ = LoadAsync(CancellationToken.None);
    }

    private async Task LoadAsync (CancellationToken ct)
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync(ct);

            Theme = settings.Theme;
            ThemeIndex = Theme switch { ThemeMode.Light => 1, ThemeMode.Dark => 2, _ => 0 };

            Language = settings.Language;
            DataDirectory = settings.DataDirectory;

            ConfirmBeforeClear = settings.ConfirmBeforeClear;

            _defaultZoomLevel = settings.DefaultZoomLevel;
            ZoomIndex = _defaultZoomLevel switch { 0.75 => 0, 1.0 => 1, 1.25 => 2, 1.5 => 3, _ => 1 };

        }
        catch
        {
            StatusMessage = "加载设置失败，将使用默认值";
        }
    }

    // ---- Index → Value 映射 ----

    partial void OnThemeIndexChanged (int value)
    {
        var mode = value switch { 1 => ThemeMode.Light, 2 => ThemeMode.Dark, _ => ThemeMode.System };
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

    partial void OnZoomIndexChanged (int value)
    {
        var zoom = value switch { 0 => 0.75, 1 => 1.0, 2 => 1.25, 3 => 1.5, _ => 1.0 };
        _defaultZoomLevel = zoom;
    }

    // ---- 命令 ----

    [RelayCommand]
    private async Task SaveSettingsAsync (CancellationToken ct)
    {
        try
        {
            IsSaving = true;
            StatusMessage = "正在保存...";

            // 保留已有的窗口状态，避免覆盖
            var existing = await _facade.LoadAppSettingsAsync(ct);

            var settings = new AppSettings
            {
                WindowState = existing.WindowState ,
                Theme = Theme ,
                Language = Language ,
                DataDirectory = DataDirectory ,
                ConfirmBeforeClear = ConfirmBeforeClear ,
                DefaultZoomLevel = _defaultZoomLevel
            };

            await _facade.SaveAppSettingsAsync(settings , ct);
            StatusMessage = "设置已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存失败";
            await _dialog.ShowErrorAsync("保存设置失败" , ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ResetDefaultsAsync ()
    {
        var confirmed = await _dialog.ShowConfirmAsync("重置设置" , "确定要恢复所有设置为默认值吗？");
        if (!confirmed) return;

        ThemeIndex = 0;
        Language = string.Empty;
        DataDirectory = string.Empty;
        ConfirmBeforeClear = true;
        ZoomIndex = 1;
        StatusMessage = "已恢复默认值（尚未保存）";
    }

    [RelayCommand]
    private async Task BrowseDataDirectoryAsync (CancellationToken ct)
    {
        try
        {
            if (AvaloniaApplication.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var storageProvider = desktop.MainWindow?.StorageProvider;
            if (storageProvider is null) return;

            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择数据目录" ,
                AllowMultiple = false
            });

            if (folders.Count > 0)
                DataDirectory = folders[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync("选择目录失败" , ex.Message);
        }
    }

    [RelayCommand]
    private void OpenDataDirectory ()
    {
        var path = string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(AppContext.BaseDirectory , "AppData")
            : DataDirectory;

        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                _ = _dialog.ShowWarningAsync("目录不存在" , $"数据目录不存在：\n{path}");
        }
        catch (Exception ex)
        {
            _ = _dialog.ShowErrorAsync("打开目录失败" , ex.Message);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Lang;
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

    [ObservableProperty]
    private ThemeMode _theme;

    [ObservableProperty]
    private int _themeIndex;

    public List<string> ThemeOptions { get; } = [Resources.Theme_System, Resources.Theme_Light, Resources.Theme_Dark];

    [ObservableProperty]
    private string _language = string.Empty;

    [ObservableProperty]
    private string _dataDirectory = string.Empty;

    [ObservableProperty]
    private bool _confirmBeforeClear = true;

    [ObservableProperty]
    private int _zoomIndex = 1;

    public List<string> ZoomOptions { get; } = [Resources.Zoom_75, Resources.Zoom_100, Resources.Zoom_125, Resources.Zoom_150];

    private double _defaultZoomLevel = 1.0;
    private string _originalLanguage = string.Empty;

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
            _originalLanguage = settings.Language;
            DataDirectory = settings.DataDirectory;

            ConfirmBeforeClear = settings.ConfirmBeforeClear;

            _defaultZoomLevel = settings.DefaultZoomLevel;
            ZoomIndex = _defaultZoomLevel switch { 0.75 => 0, 1.0 => 1, 1.25 => 2, 1.5 => 3, _ => 1 };

        }
        catch
        {
            StatusMessage = Resources.Settings_LoadFailed;
        }
    }

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

    [RelayCommand]
    private async Task SaveSettingsAsync (CancellationToken ct)
    {
        try
        {
            IsSaving = true;
            StatusMessage = Resources.Settings_Saving;

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

            var langChanged = !string.Equals(_originalLanguage , Language , StringComparison.Ordinal);
            _originalLanguage = Language;

            if (langChanged)
            {
                var clicked = await _dialog.ShowMultiOptionAsync(
                    Resources.Settings_LangChangedTitle ,
                    Resources.Settings_LangChangedMessage ,
                    Resources.Settings_LangChangedRestart ,
                    Resources.Common_Later);
                if (clicked == 0)
                {
                    Process.Start(Environment.ProcessPath!);
                    if (AvaloniaApplication.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.Shutdown();
                    return;
                }
            }

            StatusMessage = Resources.Settings_Saved;
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Settings_SaveFailed;
            await _dialog.ShowErrorAsync(Resources.Settings_SaveFailed , ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ResetDefaultsAsync ()
    {
        var confirmed = await _dialog.ShowConfirmAsync(Resources.Settings_ResetTitle , Resources.Settings_ResetConfirm);
        if (!confirmed) return;

        ThemeIndex = 0;
        Language = string.Empty;
        DataDirectory = string.Empty;
        ConfirmBeforeClear = true;
        ZoomIndex = 1;
        StatusMessage = Resources.Settings_ResetDone;
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
                Title = Resources.Settings_FolderTitle ,
                AllowMultiple = false
            });

            if (folders.Count > 0)
                DataDirectory = folders[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Settings_FolderFailed , ex.Message);
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
                _ = _dialog.ShowWarningAsync(Resources.Settings_DirNotFound ,
                    string.Format(Resources.Settings_DirNotFoundFormat , path));
        }
        catch (Exception ex)
        {
            _ = _dialog.ShowErrorAsync(Resources.Settings_OpenDirFailed , ex.Message);
        }
    }
}

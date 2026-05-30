using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using A_Pair.Presentation.Avalonia.Lang;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class PluginManagementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly ILogger<PluginManagementViewModel> _logger;

    
    public string PluginCountDisplay => string.Format(Resources.Plugin_FoundFmt, Plugins.Count);
    public string SelectedPluginVersionDisplay => SelectedPlugin != null ? string.Format(Resources.Plugin_VersionFmt, SelectedPlugin.Version) : "";
    public string SelectedPluginAuthorDisplay => SelectedPlugin != null ? string.Format(Resources.Plugin_AuthorFmt, SelectedPlugin.Author) : "";

    public PluginManagementViewModel (IApplicationFacade facade , ILogger<PluginManagementViewModel>? logger = null)
    {
        _facade = facade;
        _logger = logger ?? NullLogger<PluginManagementViewModel>.Instance;
    }

    /// <summary>页面标题。</summary>
    public string Title { get; } = Resources.Plugin_Title;

    // ── 插件列表 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlugins))]
    [NotifyPropertyChangedFor(nameof(EmptyHintVisible))]
    private ObservableCollection<PluginDisplayInfo> _plugins = [];

    /// <summary>是否有已发现的插件。</summary>
    public bool HasPlugins => Plugins.Count > 0;

    /// <summary>空列表提示是否可见。</summary>
    public bool EmptyHintVisible => Plugins.Count == 0;

    // ── 选中项 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPluginSelected))]
    [NotifyPropertyChangedFor(nameof(HasScript))]
    [NotifyPropertyChangedFor(nameof(ScriptEditorVisible))]
    [NotifyPropertyChangedFor(nameof(ConfigEditorVisible))]
    private PluginDisplayInfo? _selectedPlugin;

    /// <summary>是否有选中的插件。</summary>
    public bool HasPluginSelected => SelectedPlugin != null;

    /// <summary>当前选中插件是否为脚本插件。</summary>
    public bool HasScript => SelectedPlugin?.ScriptType != null;

    /// <summary>脚本编辑器是否可见。</summary>
    public bool ScriptEditorVisible => HasPluginSelected && HasScript;

    /// <summary>配置编辑器是否可见。</summary>
    public bool ConfigEditorVisible => HasPluginSelected;

    // ── 编辑器内容 ──

    [ObservableProperty]
    private string _scriptEditorText = string.Empty;

    [ObservableProperty]
    private string _configEditorText = string.Empty;

    [ObservableProperty]
    private bool _isScriptDirty;

    [ObservableProperty]
    private bool _isConfigDirty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private bool _isLoadingContent;
    private CancellationTokenSource? _loadCts;

    // ── 命令 ──

    [RelayCommand]
    private async Task RefreshPlugins ()
    {
        await SafeExecuteAsync(async () =>
        {
            var plugins = await _facade.GetPluginsAsync();
            // 保留当前选中插件的 Id
            var selectedId = SelectedPlugin?.Id;

            Plugins.Clear();
            foreach (var p in plugins.OrderBy(p => p.Priority).ThenBy(p => p.Name))
                Plugins.Add(p);

            OnPropertyChanged(nameof(HasPlugins));
            OnPropertyChanged(nameof(EmptyHintVisible));

            if (selectedId != null)
                SelectedPlugin = Plugins.FirstOrDefault(p => p.Id == selectedId);
        });
    }

    /// <summary>选中插件变更时加载脚本和配置。</summary>
    partial void OnSelectedPluginChanged (PluginDisplayInfo? value)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();

        if (value == null)
        {
            IsScriptDirty = false;
            IsConfigDirty = false;
            ScriptEditorText = string.Empty;
            ConfigEditorText = string.Empty;
            return;
        }

        _loadCts = new CancellationTokenSource();
        _ = LoadPluginContentAsync(value , _loadCts.Token);
    }

    private async Task LoadPluginContentAsync (PluginDisplayInfo plugin , CancellationToken ct)
    {
        _isLoadingContent = true;
        try
        {
            // 加载脚本（仅脚本插件）
            if (plugin.ScriptType != null)
            {
                await SafeExecuteAsync(async () =>
                {
                    var script = await _facade.GetPluginScriptAsync(plugin.Id , ct);
                    if (ct.IsCancellationRequested) return;
                    ScriptEditorText = script;
                } , Resources.Plugin_LoadScriptFailed);
            }
            else
            {
                ScriptEditorText = string.Empty;
            }

            if (ct.IsCancellationRequested) return;

            // 加载配置 JSON
            await SafeExecuteAsync(async () =>
            {
                var config = await _facade.GetPluginConfigJsonAsync(plugin.Id , ct);
                if (ct.IsCancellationRequested) return;
                ConfigEditorText = config;
            } , Resources.Plugin_LoadConfigFailed);
        }
        finally
        {
            _isLoadingContent = false;
            IsScriptDirty = false;
            IsConfigDirty = false;
        }
    }

    [RelayCommand]
    private async Task ToggleEnabled ()
    {
        if (SelectedPlugin == null) return;

        await SafeExecuteAsync(async () =>
        {
            var newEnabled = !SelectedPlugin.IsEnabled;
            await _facade.SetPluginEnabledAsync(SelectedPlugin.Id , newEnabled);
            SelectedPlugin.IsEnabled = newEnabled;
            StatusMessage = string.Format(Resources.Plugin_ToggledFmt, SelectedPlugin.Name, newEnabled ? Resources.Common_Enabled : Resources.Common_Disabled);
        } , Resources.Plugin_ToggleFailed);
    }

    [RelayCommand]
    private async Task SaveScript ()
    {
        if (SelectedPlugin == null) return;

        await SafeExecuteAsync(async (CancellationToken ct) =>
        {
            await _facade.SavePluginScriptAsync(SelectedPlugin.Id , ScriptEditorText , ct);
            IsScriptDirty = false;
            StatusMessage = string.Format(Resources.Plugin_ScriptSavedFmt, SelectedPlugin.Name);
        } , TimeSpan.FromSeconds(30) , Resources.Plugin_ScriptSaveFailed);
    }

    [RelayCommand]
    private async Task SaveConfig ()
    {
        if (SelectedPlugin == null) return;

        await SafeExecuteAsync(async (CancellationToken ct) =>
        {
            // 预验证 JSON 格式
            try
            {
                JsonDocument.Parse(ConfigEditorText);
            }
            catch (JsonException ex)
            {
                StatusMessage = string.Format(Resources.Plugin_JSONErrorFmt, ex.Message);
                return;
            }

            await _facade.SavePluginConfigJsonAsync(SelectedPlugin.Id , ConfigEditorText , ct);
            IsConfigDirty = false;
            StatusMessage = string.Format(Resources.Plugin_ConfigSavedFmt, SelectedPlugin.Name);
        } , TimeSpan.FromSeconds(30) , Resources.Plugin_ConfigSaveFailed);
    }

    // ── 编辑器内容变更标记 ──

    partial void OnScriptEditorTextChanged (string value)
    {
        if (!_isLoadingContent && SelectedPlugin?.ScriptType != null)
            IsScriptDirty = true;
    }

    partial void OnConfigEditorTextChanged (string value)
    {
        if (!_isLoadingContent && SelectedPlugin != null)
            IsConfigDirty = true;
    }
}

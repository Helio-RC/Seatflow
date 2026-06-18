using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Presentation.Avalonia.Lang;
using A_Pair.Presentation.Avalonia.ViewModels;
using A_Pair.Presentation.Avalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CodeWF.AvaloniaControls.Controls;
using Microsoft.Extensions.Logging;

namespace A_Pair.Presentation.Avalonia.Services;

/// <summary>
/// 引导服务——纯机械式桥接 JSON 配置与 Guide 控件。
/// JSON 是步骤定义的唯一权威来源，代码不做任何键名拼接或 ID 推断。
/// </summary>
public sealed class OnboardingService : IOnboardingService, IOnboardingStarter
{
    private readonly INavigationService _navigation;
    private readonly IApplicationFacade _facade;
    private readonly IDialogService _dialog;
    private readonly ILogger<OnboardingService> _logger;

    private OnboardingConfig? _config;
    private List<OnboardingStepDefinition> _flatStepDefs = [];
    private List<int> _phaseBoundaries = []; // 每个阶段在全量列表中的起始索引
    private Guide? _guide;
    private bool _isCompleting;

    public bool IsActive { get; private set; }

    public OnboardingService(
        INavigationService navigation,
        IApplicationFacade facade,
        IDialogService dialog,
        ILogger<OnboardingService> logger)
    {
        _navigation = navigation;
        _facade = facade;
        _dialog = dialog;
        _logger = logger;
    }

    // ──────────────────────── IOnboardingStarter (旧接口桥接) ────────────────────────
    void IOnboardingStarter.StartOnboarding() => StartOnboarding();

    // ──────────────────────── 公开 API ────────────────────────

    public void StartOnboarding()
    {
        if (_config is null)
        {
            _config = LoadConfig();
            FlattenSteps();
        }

        _isCompleting = false;
        IsActive = true;

        var mainWindow = GetMainWindow();
        if (mainWindow?.DataContext is MainShellViewModel vm)
        {
            vm.IsOnboardingActive = true;
            vm.EnsureSidebarExpanded();
            vm.OnboardingNavigateTo(PageKey.Home);
        }

        _guide = mainWindow?.OnboardingGuide;
        if (_guide is not null)
            _guide.StepOpening += OnStepOpening;

        // 等待首页渲染后显示引导
        Dispatcher.UIThread.Post(() =>
        {
            if (_guide is null) return;
            _guide.StepsSource = BuildAllSteps();
            _guide.GoTo(0);
            _guide.IsVisible = true;
            _guide.Show();
        }, DispatcherPriority.Loaded);
    }

    public void HandleStepOpening(int stepIndex, IGuideStepOption step)
    {
        if (stepIndex < 0 || stepIndex >= _flatStepDefs.Count) return;

        var stepDef = _flatStepDefs[stepIndex];

        // 1. 解析 Target 控件名 → 实际 Control
        if (!string.IsNullOrEmpty(stepDef.Target))
            step.Target = ResolveTarget(stepDef.Target);

        // 2. 若跨阶段边界，导航到目标页面（跳过首个阶段，它已在 StartOnboarding 中导航）
        var phaseIndex = GetPhaseIndex(stepIndex);
        if (phaseIndex > 0 && _phaseBoundaries[phaseIndex] == stepIndex)
        {
            var phase = _config!.Phases[phaseIndex];
            if (phase.Page is not null
                && Enum.TryParse<PageKey>(phase.Page, out var pageKey)
                && _navigation.CurrentPage != pageKey)
            {
                _navigation.NavigateTo(pageKey);
            }
        }
    }

    public void HandleGuideCompleted()
    {
        _isCompleting = true;
        _ = CompleteOnboardingAsync();
    }

    public async Task<bool> HandleGuideClosedAsync()
    {
        // 用户正常点击"完成"触发的关闭，不需要确认
        if (_isCompleting) return true;

        try
        {
            var confirmed = await _dialog.ShowConfirmAsync(
                Resources.Guide_CloseConfirm_Title,
                Resources.Guide_CloseConfirm_Message);
            if (!confirmed) return false;
        }
        catch
        {
            // 对话框显示失败，允许关闭
        }

        _ = CompleteOnboardingAsync();
        return true;
    }

    // ──────────────────────── 内部实现 ────────────────────────

    /// <summary>从嵌入资源加载 JSON 配置。</summary>
    private OnboardingConfig LoadConfig()
    {
        try
        {
            var assembly = typeof(OnboardingService).Assembly;
            const string resourceName = "A_Pair.Presentation.Avalonia.Data.onboarding_config.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogError("引导配置文件 onboarding_config.json 未找到（嵌入资源）");
                return new OnboardingConfig();
            }
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<OnboardingConfig>(json) ?? new OnboardingConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载引导配置文件失败");
            return new OnboardingConfig();
        }
    }

    /// <summary>将阶段→步骤树形结构平铺为全量列表，记录阶段边界。</summary>
    private void FlattenSteps()
    {
        _flatStepDefs.Clear();
        _phaseBoundaries.Clear();

        foreach (var phase in _config!.Phases)
        {
            _phaseBoundaries.Add(_flatStepDefs.Count);
            _flatStepDefs.AddRange(phase.Steps);
        }
        _phaseBoundaries.Add(_flatStepDefs.Count); // 哨兵
    }

    /// <summary>构造全量 GuideStepOption 列表。文本通过 resx 资源键查找。</summary>
    private List<IGuideStepOption> BuildAllSteps()
    {
        var steps = new List<IGuideStepOption>();
        var resMgr = global::A_Pair.Presentation.Avalonia.Lang.Resources.ResourceManager;
        var culture = global::A_Pair.Presentation.Avalonia.Lang.Resources.Culture;

        // 委托：从键名获取字符串，优先当前 culture，回退 zh-CN
        string R(string key)
        {
            try { return resMgr.GetString(key, culture) ?? key; }
            catch { return key; }
        }

        foreach (var stepDef in _flatStepDefs)
        {
            var placement = Enum.TryParse<GuidePlacementMode>(stepDef.Placement, ignoreCase: true, out var p)
                ? p
                : (GuidePlacementMode?)null;

            var option = new GuideStepOption
            {
                Title = R(stepDef.TitleKey),
                Description = R(stepDef.DescKey),
                Placement = placement,
                IsShowMask = stepDef.ShowMask,
                IsArrowVisible = stepDef.ShowArrow,
                // Target 留空——在 StepOpening 事件中延迟解析
            };

            steps.Add(option);
        }

        return steps;
    }

    /// <summary>查找目标控件：先在 MainWindow 名字域找，再在当前页面找。</summary>
    /// <remarks>支持分号分隔的多个候选名，取第一个找到的。</remarks>
    private Control? ResolveTarget(string name)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null) return null;

        // 支持分号分隔的候选项
        var names = name.Split(';');

        foreach (var n in names)
        {
            var trimmed = n.Trim();

            // 1. 先在 MainWindow 的名字域中找
            var mainScope = global::Avalonia.Controls.NameScope.GetNameScope(mainWindow);
            if (mainScope is not null)
            {
                var element = mainScope.Find(trimmed);
                if (element is Control c) return c;
            }

            // 2. 再在当前页面中找
            if (mainWindow.PageHost.Content is Control page)
            {
                var pageScope = global::Avalonia.Controls.NameScope.GetNameScope(page);
                if (pageScope is not null)
                {
                    var element = pageScope.Find(trimmed);
                    if (element is Control c) return c;
                }
            }
        }

        return null;
    }

    /// <summary>获取步骤所在阶段的索引。</summary>
    private int GetPhaseIndex(int stepIndex)
    {
        for (int i = _phaseBoundaries.Count - 2; i >= 0; i--)
            if (_phaseBoundaries[i] <= stepIndex)
                return i;
        return 0;
    }

    /// <summary>完成引导：清理事件订阅、恢复 UI 状态、持久化标记。</summary>
    private async Task CompleteOnboardingAsync()
    {
        if (_guide is not null)
            _guide.StepOpening -= OnStepOpening;

        IsActive = false;

        var mainWindow = GetMainWindow();
        if (mainWindow?.DataContext is MainShellViewModel vm)
        {
            await vm.CompleteOnboardingAsync();
        }

        if (_guide is not null)
        {
            _guide.Close();
            _guide.IsVisible = false;
            _guide.StepsSource = null;
        }
    }

    /// <summary>Guide.StepOpening 事件处理器。</summary>
    private void OnStepOpening(object? sender, GuideStepEventArgs e)
    {
        HandleStepOpening(e.Index, e.Step);
    }

    /// <summary>获取当前 MainWindow 实例。</summary>
    private static MainWindow? GetMainWindow()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow as MainWindow;
        return null;
    }
}

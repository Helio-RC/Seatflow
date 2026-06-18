using System;
using System.Collections.Generic;
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
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodeWF.AvaloniaControls.Controls;
using Microsoft.Extensions.Logging;

namespace A_Pair.Presentation.Avalonia.Services;

/// <summary>
/// 引导服务——纯机械式桥接 JSON 配置与 Guide 控件。
/// 支持启动引导（startupPhases）和页面独立引导块（pageGuides，首次访问触发）。
/// </summary>
public sealed class OnboardingService : IOnboardingService, IOnboardingStarter
{
    private readonly INavigationService _navigation;
    private readonly IApplicationFacade _facade;
    private readonly IDialogService _dialog;
    private readonly ILogger<OnboardingService> _logger;

    private OnboardingConfig? _config;
    // 当前活跃的步骤定义列表（启动引导=全量平铺，页面引导=单页面步骤）
    private List<OnboardingStepDefinition> _activeStepDefs = [];
    // 各阶段在 _activeStepDefs 中的起始索引（仅启动引导有效）
    private List<int> _activePhaseBoundaries = [];
    private Dictionary<string, bool> _completedPageGuides = []; // PageKey名称 → true
    private bool _completedPageGuidesLoaded; // LoadCompletedPageGuidesAsync 是否已完成
    private string? _currentPageGuide; // null=启动引导, 非null=页面引导的PageKey名称
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
        _logger.LogInformation("[Onboarding] StartOnboarding 开始");

        if (_config is null)
        {
            _config = LoadConfig();
            _logger.LogInformation("[Onboarding] 配置已加载，StartupPhases={Count}", _config.StartupPhases.Count);
            _ = LoadCompletedPageGuidesAsync();
        }
        // 始终重新平铺——_config 可能已被 TryShowPageGuide 提前加载（MainShellViewModel 构造时触发），
        // 此时 _activeStepDefs 存的是页面引导的步骤而非启动引导步骤，必须重建
        FlattenStartupSteps();
        _logger.LogInformation("[Onboarding] 步骤已平铺，共 {Count} 步", _activeStepDefs.Count);

        _isCompleting = false;
        _currentPageGuide = null;
        IsActive = true;

        var mainWindow = GetMainWindow();
        _logger.LogInformation("[Onboarding] MainWindow={NotNull}", mainWindow is not null);
        if (mainWindow?.DataContext is MainShellViewModel vm)
        {
            _logger.LogInformation("[Onboarding] 设置 IsOnboardingActive=true，导航到 Home");
            vm.IsOnboardingActive = true;
            vm.EnsureSidebarExpanded();
            vm.OnboardingNavigateTo(PageKey.Home);
        }
        else
        {
            _logger.LogWarning("[Onboarding] MainShellViewModel 不可用！DataContext={Type}", mainWindow?.DataContext?.GetType().FullName);
        }

        _guide = mainWindow?.OnboardingGuide;
        _logger.LogInformation("[Onboarding] Guide 控件={NotNull}", _guide is not null);
        if (_guide is not null)
        {
            _guide.StepOpening += OnStepOpening;
            _guide.StepOpened += OnStepOpened;
        }
        else
            _logger.LogError("[Onboarding] OnboardingGuide 控件为 null！无法显示引导");

        // 使用 Background 优先级（而非 Loaded），因为 Loaded 依赖布局 pass，
        // 但如果当前页面已是 Home，NavigateTo 会跳过导航，不触发布局 pass，
        // 导致 Loaded 回调永远不执行。Guide 控件自带 TargetResolveDelay 重试。
        Dispatcher.UIThread.Post(() =>
        {
            _logger.LogInformation("[Onboarding] Post 回调执行，_guide={NotNull}", _guide is not null);
            if (_guide is null) return;
            var steps = BuildAllStartupSteps();
            _logger.LogInformation("[Onboarding] 构建了 {Count} 个 GuideStepOption", steps.Count);
            _guide.StepsSource = steps;
            _guide.GoTo(0);
            _guide.IsVisible = true;
            _guide.Show();
            // 首次出场：卡片缩放弹出动画
            AnimateCardBounce(_guide, delayMs: 16);
            _logger.LogInformation("[Onboarding] Guide.Show() 已调用，IsOpen={IsOpen}", _guide.IsOpen);
        }, DispatcherPriority.Background);
    }

    /// <summary>检查并触发页面的独立引导块（首次访问时）。返回 true 表示触发了引导。</summary>
    public bool TryShowPageGuide(PageKey page)
    {
        if (IsActive) return false;

        // 延迟加载：首次访问页面时可能尚未触发过启动引导
        if (_config is null)
        {
            _config = LoadConfig();
            _ = LoadCompletedPageGuidesAsync();
        }

        var pageKey = page.ToString();
        if (!_config.PageGuides.TryGetValue(pageKey, out var guideBlock))
            return false;

        // 已展示过则跳过。若 CompletedPageGuides 尚未加载完成，
        // 内存中的 _completedPageGuides 为空，首次访问会触发引导；
        // 后续访问时 LoadCompletedPageGuidesAsync 已完成，正确跳过。
        if (_completedPageGuidesLoaded && _completedPageGuides.ContainsKey(pageKey))
            return false;

        var mainWindow = GetMainWindow();
        var guideControl = mainWindow?.OnboardingGuide;
        if (guideControl is null) return false;

        _isCompleting = false;
        _currentPageGuide = pageKey;
        IsActive = true;

        // 构建仅此页面的步骤
        _activeStepDefs.Clear();
        _activePhaseBoundaries.Clear();
        _activeStepDefs.AddRange(guideBlock.Steps);

        _guide = guideControl;
        _guide.StepOpening += OnStepOpening;
        _guide.StepOpened += OnStepOpened;

        Dispatcher.UIThread.Post(() =>
        {
            if (_guide is null) return;
            _guide.StepsSource = BuildStepsFromDefs(_activeStepDefs);
            _guide.GoTo(0);
            _guide.IsVisible = true;
            _guide.Show();
        }, DispatcherPriority.Background);

        return true;
    }

    /// <summary>标记页面引导已完成并持久化。</summary>
    public async Task MarkPageGuideShownAsync(PageKey page)
    {
        var pageKey = page.ToString();
        _completedPageGuides[pageKey] = true;

        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            settings.CompletedPageGuides[pageKey] = true;
            await _facade.SaveAppSettingsAsync(settings);
        }
        catch
        {
            // 持久化失败不影响用户体验
        }
    }

    public void HandleStepOpening(int stepIndex, IGuideStepOption step)
    {
        if (stepIndex < 0 || stepIndex >= _activeStepDefs.Count) return;

        var stepDef = _activeStepDefs[stepIndex];

        // 解析 Target 控件名 → 实际 Control
        if (!string.IsNullOrEmpty(stepDef.Target))
        {
            var ctrl = ResolveTarget(stepDef.Target);
            step.Target = ctrl;
        }

        // 2. 仅启动引导需要跨阶段页面导航（页面引导已在其目标页面上）
        if (_currentPageGuide is null)
        {
            var phaseIndex = GetPhaseIndex(stepIndex);
            if (phaseIndex > 0 && _activePhaseBoundaries[phaseIndex] == stepIndex)
            {
                var phase = _config!.StartupPhases[phaseIndex];
                if (phase.Page is not null
                    && Enum.TryParse<PageKey>(phase.Page, out var pageKey)
                    && _navigation.CurrentPage != pageKey)
                {
                    _navigation.NavigateTo(pageKey);
                }
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
        if (_isCompleting) return true;

        try
        {
            var confirmed = await _dialog.ShowConfirmAsync(
                Resources.Guide_CloseConfirm_Title,
                Resources.Guide_CloseConfirm_Message);
            if (!confirmed) return false;
        }
        catch { }

        _ = CompleteOnboardingAsync();
        return true;
    }

    // ──────────────────────── 内部实现 ────────────────────────

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

    private async Task LoadCompletedPageGuidesAsync()
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync();
            _completedPageGuides = settings.CompletedPageGuides ?? [];
        }
        catch
        {
            _completedPageGuides = [];
        }
        finally
        {
            _completedPageGuidesLoaded = true;
        }
    }

    /// <summary>将启动引导的阶段→步骤平铺为全量列表，记录阶段边界。</summary>
    private void FlattenStartupSteps()
    {
        _activeStepDefs.Clear();
        _activePhaseBoundaries.Clear();

        foreach (var phase in _config!.StartupPhases)
        {
            _activePhaseBoundaries.Add(_activeStepDefs.Count);
            _activeStepDefs.AddRange(phase.Steps);
        }
        _activePhaseBoundaries.Add(_activeStepDefs.Count); // 哨兵
    }

    /// <summary>构造启动引导的全量 GuideStepOption 列表。</summary>
    private List<IGuideStepOption> BuildAllStartupSteps()
        => BuildStepsFromDefs(_activeStepDefs);

    /// <summary>从步骤定义列表构造 GuideStepOption 列表。纯机械转换。</summary>
    private static List<IGuideStepOption> BuildStepsFromDefs(List<OnboardingStepDefinition> defs)
    {
        var steps = new List<IGuideStepOption>();
        var resMgr = global::A_Pair.Presentation.Avalonia.Lang.Resources.ResourceManager;
        var culture = global::A_Pair.Presentation.Avalonia.Lang.Resources.Culture;

        string R(string key)
        {
            try { return resMgr.GetString(key, culture) ?? key; }
            catch { return key; }
        }

        foreach (var stepDef in defs)
        {
            var placement = Enum.TryParse<GuidePlacementMode>(stepDef.Placement, ignoreCase: true, out var p)
                ? p
                : (GuidePlacementMode?)null;

            steps.Add(new GuideStepOption
            {
                Title = R(stepDef.TitleKey),
                Description = R(stepDef.DescKey),
                Placement = placement,
                IsShowMask = stepDef.ShowMask,
                IsArrowVisible = stepDef.ShowArrow,
            });
        }

        return steps;
    }

    private static Control? ResolveTarget(string name)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null) return null;

        var names = name.Split(';');

        foreach (var n in names)
        {
            var trimmed = n.Trim();

            // 1. MainWindow 的 NameScope（ToggleSidebarButton 等）
            var mainScope = global::Avalonia.Controls.NameScope.GetNameScope(mainWindow);
            if (mainScope is not null)
            {
                var element = mainScope.Find(trimmed);
                if (element is Control c) return c;
            }

            // 2. 通过 ContentPresenter → Child → 当前页面 View → NameScope
            var presenter = mainWindow.PageHost.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .FirstOrDefault();
            if (presenter?.Child is Control pageView)
            {
                var pageScope = global::Avalonia.Controls.NameScope.GetNameScope(pageView);
                if (pageScope is not null)
                {
                    var element = pageScope.Find(trimmed);
                    if (element is Control c) return c;
                }
            }
        }

        return null;
    }

    private int GetPhaseIndex(int stepIndex)
    {
        for (int i = _activePhaseBoundaries.Count - 2; i >= 0; i--)
            if (_activePhaseBoundaries[i] <= stepIndex)
                return i;
        return 0;
    }

    /// <summary>
    /// 完成引导：清理事件订阅、恢复 UI 状态、持久化标记。
    /// 关键：IsActive 和 _currentPageGuide 的清理必须在第一个 await 之前，
    /// 否则在 I/O 挂起期间用户触发 RestartGuide 会导致竞态条件。
    /// </summary>
    private async Task CompleteOnboardingAsync()
    {
        if (_guide is not null)
            _guide.StepOpening -= OnStepOpening;
            _guide.StepOpened -= OnStepOpened;

        var wasPageGuide = _currentPageGuide;

        // ✅ 在任何 await 之前清理可变状态，防止与 StartOnboarding 竞态
        IsActive = false;
        _currentPageGuide = null;

        // 持久化页面引导标记（可在后台安全执行，不影响竞态）
        if (wasPageGuide is not null)
        {
            _completedPageGuides[wasPageGuide] = true;
            try
            {
                var settings = await _facade.LoadAppSettingsAsync();
                settings.CompletedPageGuides[wasPageGuide] = true;
                await _facade.SaveAppSettingsAsync(settings);
            }
            catch { }
        }

        // 启动引导完成：恢复 UI 状态
        if (wasPageGuide is null)
        {
            var mainWindow = GetMainWindow();
            if (mainWindow?.DataContext is MainShellViewModel vm)
            {
                await vm.CompleteOnboardingAsync();
            }
        }

        if (_guide is not null)
        {
            _guide.Close();
            _guide.IsVisible = false;
            _guide.StepsSource = null;
        }
    }

    private void OnStepOpening(object? sender, GuideStepEventArgs e)
    {
        HandleStepOpening(e.Index, e.Step);
    }

    /// <summary>步骤打开后触发卡片弹出动画。</summary>
    private static void OnStepOpened(object? sender, GuideStepEventArgs e)
    {
        if (sender is Guide guide)
            AnimateCardBounce(guide, delayMs: 0);
    }

    /// <summary>卡片缩放弹出动画：0.96 → 1.0。</summary>
    private static async void AnimateCardBounce(Guide guide, int delayMs)
    {
        // 查找模板中的卡片 Border
        var card = FindTemplateChild<Border>(guide, "PART_CardRoot");
        if (card?.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.96;
            await Task.Delay(delayMs);
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        }
    }

    /// <summary>从控件模板中按名称查找子元素。</summary>
    private static T? FindTemplateChild<T>(Control control, string name) where T : class
    {
        // 遍历视觉树查找命名元素
        var children = control.GetVisualDescendants();
        foreach (var child in children)
        {
            if (child is T typed && child.Name == name)
                return typed;
        }
        return null;
    }

    private static MainWindow? GetMainWindow()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow as MainWindow;
        return null;
    }
}

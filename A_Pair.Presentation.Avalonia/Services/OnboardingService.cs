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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;

namespace A_Pair.Presentation.Avalonia.Services;

/// <summary>
/// 引导服务——使用 Grid 层控件（非 Popup）渲染遮罩和卡片，与主页面同层级。
/// JSON 配置驱动所有步骤定义。
/// </summary>
public sealed class OnboardingService : IOnboardingService, IOnboardingStarter
{
    private readonly INavigationService _navigation;
    private readonly IApplicationFacade _facade;
    private readonly IDialogService _dialog;
    private readonly ILogger<OnboardingService> _logger;

    private OnboardingConfig? _config;
    private List<OnboardingStepDefinition> _activeStepDefs = [];
    private List<int> _activePhaseBoundaries = [];
    private Dictionary<string, bool> _completedPageGuides = [];
    private bool _completedPageGuidesLoaded;
    private string? _currentPageGuide;
    private int _currentStepIndex;
    private bool _isCompleting;

    // MainWindow 中的 Grid 层控件（非 Popup）
    private Border? _mask;
    private Border? _card;
    private Border? _highlight;
    private TextBlock? _cardTitle;
    private TextBlock? _cardDesc;
    private Button? _prevBtn;
    private Button? _nextBtn;
    private Button? _finishBtn;

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

    // ── IOnboardingStarter 桥接 ──
    void IOnboardingStarter.StartOnboarding() => StartOnboarding();

    // ═══════════════════════════════════════════════
    // 公开 API
    // ═══════════════════════════════════════════════

    public void StartOnboarding()
    {
        if (_config is null) { _config = LoadConfig(); _ = LoadCompletedPageGuidesAsync(); }
        FlattenStartupSteps();
        _isCompleting = false;
        _currentPageGuide = null;
        _currentStepIndex = 0;
        IsActive = true;

        var mw = GetMainWindow();
        CaptureOverlayControls(mw);
        if (mw?.DataContext is MainShellViewModel vm)
        {
            vm.IsOnboardingActive = true;
            vm.EnsureSidebarExpanded();
            vm.OnboardingNavigateTo(PageKey.Home);
        }

        Dispatcher.UIThread.Post(() => ShowCurrentStep(), DispatcherPriority.Background);
    }

    public bool TryShowPageGuide(PageKey page)
    {
        if (IsActive) return false;
        if (_config is null) { _config = LoadConfig(); _ = LoadCompletedPageGuidesAsync(); }
        var key = page.ToString();
        if (!_config.PageGuides.TryGetValue(key, out var block)) return false;
        if (_completedPageGuidesLoaded && _completedPageGuides.ContainsKey(key)) return false;

        _isCompleting = false;
        _currentPageGuide = key;
        _currentStepIndex = 0;
        IsActive = true;
        _activeStepDefs.Clear();
        _activePhaseBoundaries.Clear();
        _activeStepDefs.AddRange(block.Steps);

        var mw = GetMainWindow();
        CaptureOverlayControls(mw);
        Dispatcher.UIThread.Post(() => ShowCurrentStep(), DispatcherPriority.Background);
        return true;
    }

    public async Task MarkPageGuideShownAsync(PageKey page)
    {
        var key = page.ToString();
        _completedPageGuides[key] = true;
        try
        {
            var s = await _facade.LoadAppSettingsAsync();
            s.CompletedPageGuides[key] = true;
            await _facade.SaveAppSettingsAsync(s);
        }
        catch { }
    }

    public void GoToPreviousStep()
    {
        if (_currentStepIndex <= 0) return;
        _currentStepIndex--;
        NavigateToPhaseStart();
        ShowCurrentStep();
    }

    public void GoToNextStep()
    {
        if (_currentStepIndex >= _activeStepDefs.Count - 1) return;
        _currentStepIndex++;
        NavigateToPhaseStart();
        ShowCurrentStep();
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
            if (!await _dialog.ShowConfirmAsync(Resources.Guide_CloseConfirm_Title, Resources.Guide_CloseConfirm_Message))
                return false;
        }
        catch { }
        _ = CompleteOnboardingAsync();
        return true;
    }

    public void ShowCard()
    {
        if (_card is not null) _card.IsVisible = true;
        if (_mask is not null) _mask.IsVisible = true;
    }

    // ═══════════════════════════════════════════════
    // 内部实现
    // ═══════════════════════════════════════════════

    private void CaptureOverlayControls(MainWindow? mw)
    {
        if (mw is null || _mask is not null) return;
        _mask = mw.GuideMaskOverlay;
        _card = mw.GuideCardOverlay;
        _highlight = mw.GuideTargetHighlight;
        _cardTitle = mw.GuideCardTitle;
        _cardDesc = mw.GuideCardDesc;
        _prevBtn = mw.GuideCardPrevButton;
        _nextBtn = mw.GuideCardNextButton;
        _finishBtn = mw.GuideCardFinishButton;
    }

    /// <summary>若当前步骤落在阶段边界，导航到对应页面。</summary>
    private void NavigateToPhaseStart()
    {
        if (_currentPageGuide is not null) return; // 页面引导无需导航
        for (int i = 0; i < _activePhaseBoundaries.Count - 1; i++)
        {
            if (_activePhaseBoundaries[i] == _currentStepIndex && i > 0)
            {
                var pageName = _config!.StartupPhases[i].Page;
                if (pageName is not null && Enum.TryParse<PageKey>(pageName, out var pk))
                    _navigation.NavigateTo(pk);
                return;
            }
        }
    }

    private void ShowCurrentStep()
    {
        if (_mask is null || _card is null) { CaptureOverlayControls(GetMainWindow()); }
        if (_mask is null || _card is null) return;

        if (_currentStepIndex >= _activeStepDefs.Count) { _ = CompleteOnboardingAsync(); return; }

        var def = _activeStepDefs[_currentStepIndex];
        var resMgr = global::A_Pair.Presentation.Avalonia.Lang.Resources.ResourceManager;
        var culture = global::A_Pair.Presentation.Avalonia.Lang.Resources.Culture;
        string R(string k) { try { return resMgr.GetString(k, culture) ?? k; } catch { return k; } }

        if (_cardTitle is not null) _cardTitle.Text = R(def.TitleKey);
        if (_cardDesc is not null) _cardDesc.Text = R(def.DescKey);

        // 按钮可见性
        var isFirst = _currentStepIndex == 0;
        var isLast = _currentStepIndex >= _activeStepDefs.Count - 1;
        if (_prevBtn is not null) _prevBtn.IsVisible = !isFirst;
        if (_nextBtn is not null) _nextBtn.IsVisible = !isLast;
        if (_finishBtn is not null) _finishBtn.IsVisible = isLast;

        // 遮罩
        _mask.IsVisible = true;
        _mask.Opacity = 1;

        // 定位高亮 + 卡片
        var target = ResolveTarget(def.Target);
        if (target is not null)
        {
            PositionHighlight(target);
            PositionCardNear(target, def.Placement);
        }
        else
        {
            if (_highlight is not null) _highlight.IsVisible = false;
            _card.HorizontalAlignment = HorizontalAlignment.Center;
            _card.VerticalAlignment = VerticalAlignment.Center;
            _card.Margin = new Thickness(0);
        }

        _card.IsVisible = true;
        _card.Focus();
    }

    private void PositionHighlight(Control target)
    {
        if (_highlight is null) return;
        var mw = GetMainWindow();
        if (mw is null) return;
        var pt = target.TranslatePoint(new Point(0, 0), mw) ?? new Point(0, 0);
        var padding = 4; // GapRadius
        _highlight.Margin = new Thickness(pt.X - padding, pt.Y - padding, 0, 0);
        _highlight.Width = target.Bounds.Width + padding * 2;
        _highlight.Height = target.Bounds.Height + padding * 2;
        _highlight.IsVisible = true;
    }

    private void PositionCardNear(Control target, string placement)
    {
        var mw = GetMainWindow();
        if (mw is null) return;

        // 获取 target 在窗口中的绝对位置
        var targetBounds = target.Bounds;
        var point = target.TranslatePoint(new Point(0, 0), mw) ?? new Point(0, 0);
        var targetRect = new Rect(point, targetBounds.Size);

        double cardW = _card!.MinWidth;
        double margin = 14;

        _card.HorizontalAlignment = HorizontalAlignment.Left;
        _card.VerticalAlignment = VerticalAlignment.Top;

        double left, top;
        switch (placement.ToLowerInvariant())
        {
            case "bottom":
                left = targetRect.X;
                top = targetRect.Bottom + margin;
                break;
            case "left":
                left = targetRect.Left - cardW - margin;
                top = targetRect.Y;
                break;
            case "top":
                left = targetRect.X;
                top = targetRect.Top - 200 - margin; // 估算卡片高度
                break;
            default: // right
                left = targetRect.Right + margin;
                top = targetRect.Y;
                break;
        }

        // 边界约束
        var winW = mw.Bounds.Width;
        var winH = mw.Bounds.Height;
        if (left + cardW > winW) left = winW - cardW - 20;
        if (left < 20) left = 20;
        if (top < 40) top = 40;
        if (top > winH - 200) top = winH - 220;

        _card.Margin = new Thickness(left, top, 0, 0);
    }

    private Control? ResolveTarget(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var mw = GetMainWindow();
        if (mw is null) return null;

        foreach (var n in name.Split(';'))
        {
            var trimmed = n.Trim();
            // MainWindow NameScope
            var ms = global::Avalonia.Controls.NameScope.GetNameScope(mw);
            if (ms?.Find(trimmed) is Control c) return c;
            // 页面 View → NameScope
            var presenter = mw.PageHost.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
            if (presenter?.Child is Control pv)
            {
                var ps = global::Avalonia.Controls.NameScope.GetNameScope(pv);
                if (ps?.Find(trimmed) is Control c2) return c2;
            }
        }
        return null;
    }

    private void FlattenStartupSteps()
    {
        _activeStepDefs.Clear();
        _activePhaseBoundaries.Clear();
        foreach (var ph in _config!.StartupPhases)
        {
            _activePhaseBoundaries.Add(_activeStepDefs.Count);
            _activeStepDefs.AddRange(ph.Steps);
        }
        _activePhaseBoundaries.Add(_activeStepDefs.Count);
    }

    private OnboardingConfig LoadConfig()
    {
        try
        {
            using var stream = typeof(OnboardingService).Assembly
                .GetManifestResourceStream("A_Pair.Presentation.Avalonia.Data.onboarding_config.json");
            if (stream is null) return new OnboardingConfig();
            return JsonSerializer.Deserialize<OnboardingConfig>(new StreamReader(stream).ReadToEnd()) ?? new OnboardingConfig();
        }
        catch (Exception ex) { _logger.LogError(ex, "加载引导配置失败"); return new OnboardingConfig(); }
    }

    private async Task LoadCompletedPageGuidesAsync()
    {
        try { _completedPageGuides = (await _facade.LoadAppSettingsAsync()).CompletedPageGuides ?? []; }
        catch { _completedPageGuides = []; }
        finally { _completedPageGuidesLoaded = true; }
    }

    private async Task CompleteOnboardingAsync()
    {
        IsActive = false;
        var wasPageGuide = _currentPageGuide;
        _currentPageGuide = null;

        if (_mask is not null) _mask.IsVisible = false;
        if (_highlight is not null) _highlight.IsVisible = false;
        if (_card is not null) _card.IsVisible = false;

        if (wasPageGuide is not null)
        {
            _completedPageGuides[wasPageGuide] = true;
            try
            {
                var s = await _facade.LoadAppSettingsAsync();
                s.CompletedPageGuides[wasPageGuide] = true;
                await _facade.SaveAppSettingsAsync(s);
            }
            catch { }
        }
        else
        {
            var mw = GetMainWindow();
            if (mw?.DataContext is MainShellViewModel vm) await vm.CompleteOnboardingAsync();
        }
    }

    private static MainWindow? GetMainWindow()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            return d.MainWindow as MainWindow;
        return null;
    }
}

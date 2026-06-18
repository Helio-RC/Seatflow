using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Lang;
using A_Pair.Presentation.Avalonia.Services;
using A_Pair.Presentation.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CodeWF.AvaloniaControls.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window , IOnboardingStarter
    {
        private int _onboardingPhase = 0;

        // 各阶段引导文本（通过静态属性延迟加载以支持 i18n）
        private static (string Title, string Desc)[][] PhaseTexts => _phaseTexts ??= BuildPhaseTexts();
        private static (string Title, string Desc)[][]? _phaseTexts;

        private static (string Title, string Desc)[][] BuildPhaseTexts ()
        {
            // 使用完全限定名称避免 Window.Resources 属性遮蔽 Lang.Resources 类
            var resMgr = global::A_Pair.Presentation.Avalonia.Lang.Resources.ResourceManager;
            var culture = global::A_Pair.Presentation.Avalonia.Lang.Resources.Culture;
            string T(string key) => resMgr.GetString(key, culture)!;
            return [
                [(T("Guide_Phase0_Step0_Title"), T("Guide_Phase0_Step0_Desc")),
                 (T("Guide_Phase0_Step1_Title"), T("Guide_Phase0_Step1_Desc"))],
                [(T("Guide_Phase1_Step0_Title"), T("Guide_Phase1_Step0_Desc")),
                 (T("Guide_Phase1_Step1_Title"), T("Guide_Phase1_Step1_Desc"))],
                [(T("Guide_Phase2_Step0_Title"), T("Guide_Phase2_Step0_Desc")),
                 (T("Guide_Phase2_Step1_Title"), T("Guide_Phase2_Step1_Desc")),
                 (T("Guide_Phase2_Step2_Title"), T("Guide_Phase2_Step2_Desc"))],
                [(T("Guide_Phase3_Step0_Title"), T("Guide_Phase3_Step0_Desc")),
                 (T("Guide_Phase3_Step1_Title"), T("Guide_Phase3_Step1_Desc"))],
                [(T("Guide_Phase4_Step0_Title"), T("Guide_Phase4_Step0_Desc")),
                 (T("Guide_Phase4_Step1_Title"), T("Guide_Phase4_Step1_Desc")),
                 (T("Guide_Phase4_Step2_Title"), T("Guide_Phase4_Step2_Desc"))],
                [(T("Guide_Phase5_Step0_Title"), T("Guide_Phase5_Step0_Desc"))],
            ];
        }

        // 每个阶段需要导航到的页面（null 表示不导航）
        private static readonly PageKey?[] PhasePages =
        [
            null,                               // 阶段 0：留在当前页（Home）
            PageKey.MemberManagement,           // 阶段 1
            PageKey.VenueConfiguration,         // 阶段 2
            PageKey.StrategyConfiguration,      // 阶段 3
            PageKey.SeatingArrangement,         // 阶段 4
            null,                               // 阶段 5：回到 Home
        ];

        // 每个阶段需要查找的控件名（null 表示无目标/居中弹层）
        private static readonly string?[][] PhaseTargetNames =
        [
            [null, "ToggleSidebarButton"],                                              // 阶段 0
            ["ImportButton", "StudentListBox"],                                         // 阶段 1
            ["NewVenueButton", "LayoutTypePanel", "SaveVenueButton"],                   // 阶段 2
            ["StrategyListBox", "EditEnabledSwitch"],                                   // 阶段 3
            ["VenueListBox;DatasetListBox", "GenerateButton", "ExportButton"],          // 阶段 4
            [null],                                                                     // 阶段 5
        ];

        public MainWindow ()
        {
            InitializeComponent();
        }

        /// <summary>启动首次使用引导。</summary>
        public void StartOnboarding ()
        {
            _onboardingPhase = 0;
            if (DataContext is MainShellViewModel vm)
            {
                vm.IsOnboardingActive = true;
                vm.EnsureSidebarExpanded();
                // 从 Home 页面开始
                vm.OnboardingNavigateTo(PageKey.Home);
            }

            // 等待首页加载后显示引导
            Dispatcher.UIThread.Post(() =>
            {
                LoadAndShowPhase(0);
            }, DispatcherPriority.Loaded);
        }

        /// <summary>Guide 步骤全部完成（用户点击最后一步的"完成"按钮）。</summary>
        private void OnGuideCompleted (object? sender , EventArgs e)
        {
            AdvanceToNextPhase();
        }

        /// <summary>Guide 被用户关闭（点击 × 或按 Esc）。</summary>
        private void OnGuideClosed (object? sender , EventArgs e)
        {
            CompleteOnboarding();
        }

        private void AdvanceToNextPhase ()
        {
            _onboardingPhase++;
            if (_onboardingPhase >= PhaseTexts.Length)
            {
                CompleteOnboarding();
                return;
            }

            // 导航到目标页面
            var targetPage = PhasePages[_onboardingPhase];
            if (targetPage.HasValue && DataContext is MainShellViewModel vm)
            {
                vm.OnboardingNavigateTo(targetPage.Value);
            }
            else if (_onboardingPhase == PhasePages.Length - 1 && DataContext is MainShellViewModel vm2)
            {
                // 最后一阶段（完成）导航回首页
                vm2.OnboardingNavigateTo(PageKey.Home);
            }

            // 等待页面渲染后加载新阶段的步骤
            Dispatcher.UIThread.Post(() =>
            {
                LoadAndShowPhase(_onboardingPhase);
            }, DispatcherPriority.Loaded);
        }

        private void LoadAndShowPhase (int phase)
        {
            if (phase >= PhaseTexts.Length) return;

            var texts = PhaseTexts[phase];
            var targetNames = PhaseTargetNames[phase];
            var steps = new List<IGuideStepOption>();

            for (int i = 0; i < texts.Length; i++)
            {
                var (title, desc) = texts[i];
                Control? target = null;

                if (targetNames[i] != null)
                {
                    // 支持分号分隔的多个候选名（取第一个找到的）
                    var names = targetNames[i]!.Split(';');
                    foreach (var name in names)
                    {
                        target = ResolveTarget(name.Trim());
                        if (target != null) break;
                    }
                }

                steps.Add(new GuideStepOption
                {
                    Title = title,
                    Description = desc,
                    Target = target,
                    Placement = target != null ? GuidePlacementMode.Right : GuidePlacementMode.Center,
                });
            }

            OnboardingGuide.StepsSource = steps;
            OnboardingGuide.GoTo(0);
            OnboardingGuide.IsVisible = true;
            OnboardingGuide.Show();
        }

        /// <summary>查找目标控件：先在 MainWindow 中找，再到当前页面中找。</summary>
        /// <remarks>
        /// 使用 global:: 前缀以避免与 CodeWF.AvaloniaControls.Controls 命名空间中的 Controls 冲突。
        /// </remarks>
        private Control? ResolveTarget (string name)
        {
            // 先在 MainWindow 的名字域中找（侧边栏按钮等）
            var mainScope = global::Avalonia.Controls.NameScope.GetNameScope(this);
            if (mainScope != null)
            {
                var element = mainScope.Find(name);
                if (element is Control c) return c;
            }

            // 再在当前页面中找
            if (PageHost.Content is Control page)
            {
                var pageScope = global::Avalonia.Controls.NameScope.GetNameScope(page);
                if (pageScope != null)
                {
                    var element = pageScope.Find(name);
                    if (element is Control c) return c;
                }
            }

            return null;
        }

        private async void CompleteOnboarding ()
        {
            OnboardingGuide.Close();
            OnboardingGuide.IsVisible = false;

            if (DataContext is MainShellViewModel vm)
            {
                await vm.CompleteOnboardingAsync();
            }
        }

        protected override void OnPropertyChanged (AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is ViewModels.MainShellViewModel vm)
                vm.OnWindowWidthChanged(Bounds.Width);
        }

        protected override void OnClosing (WindowClosingEventArgs e)
        {
            var state = new WindowStateSettings
            {
                Left = Position.X ,
                Top = Position.Y ,
                Width = Width ,
                Height = Height ,
                IsMaximized = WindowState == WindowState.Maximized
            };

            SaveWindowStateBlocking(state);
            base.OnClosing(e);
        }

        /// <summary>同步保存窗口状态，确保在窗口关闭前完成 I/O 写入。</summary>
        /// <remarks>
        /// 不能使用 fire-and-forget 异步：Avalonia dispatcher 在窗口关闭后停止，
        /// 异步 continuation 无法回发到 UI 线程，导致写入丢失。
        /// 使用 Task.Run 将整个操作放到线程池，避免阻塞 UI 线程时发生死锁。
        /// </remarks>
        private static void SaveWindowStateBlocking (WindowStateSettings state)
        {
            try
            {
                if (global::Avalonia.Application.Current is not App appInstance) return;
                var facade = appInstance.ServiceProvider.GetRequiredService<IApplicationFacade>();
                Task.Run(async () =>
                {
                    var settings = await facade.LoadAppSettingsAsync();
                    settings.WindowState = state;
                    await facade.SaveAppSettingsAsync(settings);
                }).GetAwaiter().GetResult();
            }
            catch
            {
                // 关闭时保存失败不应阻止退出
            }
        }

    }
}

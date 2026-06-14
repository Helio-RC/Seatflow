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
    public partial class MainWindow : Window
    {
        private int _onboardingPhase = 0;

        // 各阶段引导文本（中文，后续可改为从 Resources 读取）
        private static readonly (string Title, string Desc)[][] PhaseTexts =
        [
            // 阶段 0：欢迎 & 导航
            [
                ("欢迎使用 A_Pair",
                 "A_Pair 是一款智能座位编排工具。接下来将引导您完成首次排座的完整流程：导入学生名单 → 创建会场布局 → 配置排座策略 → 生成座位表 → 导出结果。预计需要 3-5 分钟。"),
                ("功能导航",
                 "左侧导航栏包含所有功能入口。引导过程中会自动切换页面，跟随指引操作即可。您也可以随时点击右上角 × 退出引导。"),
            ],
            // 阶段 1：成员管理
            [
                ("导入学生名单",
                 "首先需要导入学生数据。点击「导入」按钮，支持 CSV、Excel 格式。您也可以先下载模板按格式填写。"),
                ("管理学生信息",
                 "导入后学生信息会显示在此列表中。您可以手动编辑、添加或删除学生。新增行在列表底部，填写后点击 + 按钮即可添加。"),
            ],
            // 阶段 2：会场配置
            [
                ("创建会场",
                 "接下来为教室/考场创建座位布局。点击「新建会场」开始。"),
                ("设置网格参数",
                 "选择布局类型（通常为网格），设置行列数来定义座位排布。右侧预览区会实时显示布局效果。"),
                ("保存会场",
                 "配置完成后点击保存。您可以创建多个会场（如不同教室），在排座时选择使用。"),
            ],
            // 阶段 3：策略配置
            [
                ("排座策略",
                 "选择并配置排座策略。列表中的策略按优先级从高到低排列，高优先级策略先分配座位。"),
                ("启用并调整策略",
                 "点击策略可查看和修改其参数。使用开关启用/禁用策略。建议首次使用保持默认配置，点击「保存全部」。"),
            ],
            // 阶段 4：生成座位 & 导出
            [
                ("选择名单和会场",
                 "在左侧面板中选择学生名单和会场布局，这是生成座位的前置条件。"),
                ("一键生成座位",
                 "点击「生成座位」按钮，系统将按照您配置的策略自动编排座位。生成后可在中央画布查看和手动调整。"),
                ("导出结果",
                 "完成后可将座位表导出为 Excel、CSV、PDF 或图片格式。也可以保存为快照，方便日后回溯。"),
            ],
            // 阶段 5：完成
            [
                ("开始使用吧！",
                 "您已经了解了 A_Pair 的核心工作流程。现在可以独立完成座位编排了！如需帮助，请随时查阅文档。"),
            ],
        ];

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

            return null!;
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

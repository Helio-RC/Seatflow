using System;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using CodeWF.AvaloniaControls.Controls;

namespace SeatFlow.Presentation.Avalonia.Views
{
    /// <summary>
    /// 共享外壳视图：桌面端由 <see cref="MainWindow"/> 承载，
    /// 浏览器端（WASM）直接作为 <c>ISingleViewApplicationLifetime.MainView</c>。
    /// 所有命名控件（PageHost、OnboardingGuide、DialogOverlay*）均位于此视图的 NameScope。
    /// </summary>
    public partial class MainView : UserControl
    {
        private readonly IOnboardingService _onboarding;

        public MainView(IOnboardingService onboarding)
        {
            _onboarding = onboarding;
            InitializeComponent();
        }

        /// <summary>Guide 步骤全部完成（用户点击最后一步的"完成"按钮）。</summary>
        private void OnGuideCompleted(object? sender, EventArgs e)
            => _onboarding.HandleGuideCompleted();

        /// <summary>Guide 被用户关闭（点击 × 或按 Esc）。</summary>
        private async void OnGuideClosed(object? sender, EventArgs e)
        {
            if (!await _onboarding.HandleGuideClosedAsync())
                OnboardingGuide.Show();
        }

        /// <summary>Guide 步骤切换前，解析 Target、处理跨阶段页面导航。</summary>
        private void OnGuideStepOpening(object? sender, GuideStepEventArgs e)
            => _onboarding.HandleStepOpening(e.Index, e.Step);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is MainShellViewModel vm)
                vm.OnWindowWidthChanged(Bounds.Width);
        }
    }
}

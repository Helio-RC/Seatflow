using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SeatFlow.Presentation.Avalonia.Views
{
    /// <summary>
    /// 共享外壳视图：桌面端由 <see cref="MainWindow"/> 承载，
    /// 浏览器端（WASM）直接作为 <c>ISingleViewApplicationLifetime.MainView</c>。
    /// 所有命名控件（PageHost、DialogOverlay*、OobeCoachMark）均位于此视图的 NameScope。
    /// </summary>
    public partial class MainView : UserControl
    {
        private OobeViewModel? _oobe;
        private int _handoffTargetAttempts;

        public MainView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => AttachOobe(DataContext as MainShellViewModel);
            LayoutUpdated += (_, _) => RefreshHandoffTarget(allowRetry: false);
            EffectiveViewportChanged += (_, _) => RefreshHandoffTarget(allowRetry: false);
            AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged, RoutingStrategies.Bubble);
        }

        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
            => RefreshHandoffTarget(allowRetry: false);

        private void AttachOobe(MainShellViewModel? shell)
        {
            if (_oobe is not null)
                _oobe.PropertyChanged -= OnOobePropertyChanged;

            _oobe = shell?.Oobe;
            if (_oobe is not null)
                _oobe.PropertyChanged += OnOobePropertyChanged;

            ScheduleHandoffTargetRefresh(resetAttempts: true);
        }

        private void OnOobePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(OobeViewModel.IsHandoffVisible)
                or nameof(OobeViewModel.HandoffState))
            {
                ScheduleHandoffTargetRefresh(resetAttempts: e.PropertyName == nameof(OobeViewModel.IsHandoffVisible));
            }
        }

        private void ScheduleHandoffTargetRefresh(bool resetAttempts)
        {
            if (resetAttempts) _handoffTargetAttempts = 0;
            Dispatcher.UIThread.Post(
                () => RefreshHandoffTarget(allowRetry: true),
                DispatcherPriority.Background);
        }

        private void RefreshHandoffTarget(bool allowRetry)
        {
            if (DataContext is not MainShellViewModel shell || !shell.Oobe.IsHandoffVisible)
            {
                OobeCoachMark.SetTarget(null);
                _handoffTargetAttempts = 0;
                return;
            }

            var target = ResolveHandoffTarget(shell);
            OobeCoachMark.SetTarget(target);

            if (target is not null)
            {
                if (IsWithinViewport(target))
                    _handoffTargetAttempts = 0;
                return;
            }

            if (allowRetry && _handoffTargetAttempts++ < 40)
            {
                DispatcherTimer.RunOnce(
                    () => RefreshHandoffTarget(allowRetry: true),
                    TimeSpan.FromMilliseconds(50));
            }
        }

        private Control? ResolveHandoffTarget(MainShellViewModel shell)
        {
            var preferWelcome = shell.CurrentViewModel is SeatingArrangementViewModel { HasGenerated: false };
            var names = preferWelcome
                ? new[] { "WelcomeGenerateButton", "GenerateButton" }
                : new[] { "GenerateButton", "WelcomeGenerateButton" };

            var candidates = new List<Control>();
            foreach (var name in names)
            {
                var control = this.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(c => c.Name == name
                        && c.IsEffectivelyVisible
                        && c.Bounds.Width > 0
                        && c.Bounds.Height > 0);

                if (control is not null) candidates.Add(control);
            }

            if (candidates.Count == 0) return null;

            // 优先选择完整落在可视区域内的目标（手机档/滚动页面避免高亮到视口外）。
            foreach (var candidate in candidates)
            {
                if (IsWithinViewport(candidate)) return candidate;
            }

            // 否则优先工具栏内（不可滚动）的候选，避免把卡片指向屏幕外。
            foreach (var candidate in candidates)
            {
                if (IntersectsViewport(candidate) && !IsInsideScrollViewer(candidate))
                    return candidate;
            }

            var fallback = candidates[0];
            if (_handoffTargetAttempts < 3 && IsInsideScrollViewer(fallback))
            {
                _handoffTargetAttempts++;
                fallback.BringIntoView();
            }

            return fallback;
        }

        private bool IsWithinViewport(Control control)
        {
            var rect = GetViewportRect(control);
            return rect is { } r
                && r.X >= -1 && r.Y >= -1
                && r.Right <= Bounds.Width + 1
                && r.Bottom <= Bounds.Height + 1;
        }

        private bool IntersectsViewport(Control control)
        {
            var rect = GetViewportRect(control);
            if (rect is not { } r) return false;
            return r.Right > 0 && r.Bottom > 0 && r.X < Bounds.Width && r.Y < Bounds.Height;
        }

        private Rect? GetViewportRect(Control control)
        {
            var point = control.TranslatePoint(new Point(0, 0), this);
            if (point is null) return null;
            return new Rect(point.Value, control.Bounds.Size);
        }

        private static bool IsInsideScrollViewer(Control control)
            => control.GetVisualAncestors().OfType<ScrollViewer>().Any();

        /// <summary>紧凑档抽屉遮罩点击：关闭导航抽屉。</summary>
        private void OnNavMaskPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is MainShellViewModel vm)
                vm.CloseNavDrawerCommand.Execute(null);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is MainShellViewModel vm)
            {
                vm.OnWindowWidthChanged(Bounds.Width);
                RefreshHandoffTarget(allowRetry: false);
            }
        }

    }
}

using System;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using A_Pair.Presentation.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        private readonly IOnboardingService _onboarding;

        public MainWindow(IOnboardingService onboarding)
        {
            _onboarding = onboarding;
            InitializeComponent();
            GuideCardPrevButton.Click += (s, e) => _onboarding.GoToPreviousStep();
            GuideCardNextButton.Click += (s, e) => _onboarding.GoToNextStep();
            GuideCardFinishButton.Click += (s, e) => _onboarding.HandleGuideCompleted();
            GuideCardCloseButton.Click += async (s, e) =>
            {
                if (!await _onboarding.HandleGuideClosedAsync()) _onboarding.ShowCard();
            };
        }

        // ── 窗口管理 ──

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is MainShellViewModel vm)
                vm.OnWindowWidthChanged(Bounds.Width);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            var state = new WindowStateSettings
            {
                Left = Position.X,
                Top = Position.Y,
                Width = Width,
                Height = Height,
                IsMaximized = WindowState == WindowState.Maximized
            };

            SaveWindowStateBlocking(state);
            base.OnClosing(e);
        }

        private static void SaveWindowStateBlocking(WindowStateSettings state)
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
            catch { }
        }
    }
}

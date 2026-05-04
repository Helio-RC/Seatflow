using System;
using Avalonia;
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        private IDisposable? _contentSub;

        public MainWindow()
        {
            InitializeComponent();

            _contentSub = PageHost.GetObservable(ContentControl.ContentProperty)
                .Subscribe(new Observer<object?>(o => OnPageContentChanged(o as Control)));
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == BoundsProperty && DataContext is ViewModels.MainShellViewModel vm)
                vm.OnWindowWidthChanged(Bounds.Width);
        }

        private void OnPageContentChanged(Control? newPage)
        {
            if (newPage is null) return;
            if (DataContext is not ViewModels.MainShellViewModel vm) return;

            if (newPage.IsLoaded)
            {
                vm.SignalPageLoaded();
            }
            else
            {
                void OnLoaded(object? s, global::Avalonia.Interactivity.RoutedEventArgs args)
                {
                    newPage.Loaded -= OnLoaded;
                    vm.SignalPageLoaded();
                }

                newPage.Loaded += OnLoaded;
            }
        }

        private sealed class Observer<T> : IObserver<T>
        {
            private readonly Action<T> _onNext;
            public Observer(Action<T> onNext) => _onNext = onNext;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _onNext(value);
        }
    }
}

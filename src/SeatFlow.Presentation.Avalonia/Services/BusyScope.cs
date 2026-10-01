using System;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// <see cref="IBusyScope"/> 的默认实现（ReactiveObject，供 XAML 绑定）。
/// 每个页面 ViewModel 注入独立实例（DI Transient）。
/// </summary>
public sealed partial class BusyScope : ReactiveObject, IBusyScope
{
    [Reactive] private string? _message;
    private int _count;

    public bool IsBusy => _count > 0;

    public IDisposable Begin(string? message = null)
    {
        _count++;
        if (!string.IsNullOrEmpty(message))
            Message = message;
        this.RaisePropertyChanged(nameof(IsBusy));
        return new Scope(this);
    }

    private void End()
    {
        if (_count > 0) _count--;
        if (_count == 0) Message = null;
        this.RaisePropertyChanged(nameof(IsBusy));
    }

    private sealed class Scope(BusyScope owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.End();
        }
    }
}

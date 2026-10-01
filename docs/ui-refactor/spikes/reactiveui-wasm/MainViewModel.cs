using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
#if USE_RUI
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
#endif

namespace ReactiveUiWasm;

#if USE_RUI
/// <summary>
/// ReactiveUI 变体：验证 ReactiveObject / [Reactive] 源生成器 / ReactiveCommand /
/// WhenAnyValue + Throttle 在 WASM 裁剪发布下可运行（spike #1）。
/// </summary>
public sealed partial class MainViewModel : ReactiveObject
{
    [Reactive] private int _count;
    [Reactive] private string _pipeline = "等待点击…";
    [Reactive] private string _throttled = "(Throttle 300ms 等待首次输出)";

    public string Headline => "ReactiveUI 25.1.1 + ReactiveUI.Avalonia 12.1.5（spike）";

    public IReactiveCommand IncrementCommand { get; }

    public MainViewModel()
    {
        IncrementCommand = ReactiveCommand.Create(() =>
        {
            Count++;
            Pipeline = $"已点击 {Count} 次 · {DateTime.UtcNow:HH:mm:ss.fff}";
            Console.WriteLine($"[SPIKE] increment -> {Count}");
        });

        // RC-1 对策验证：对高频变化做 300ms 节流
        this.WhenAnyValue(x => x.Pipeline)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Subscribe(s =>
            {
                Throttled = $"节流输出（300ms）: {s}";
                Console.WriteLine($"[SPIKE] throttled -> {s}");
            });

        Console.WriteLine("[SPIKE] ReactiveUI pipeline ready");
    }
}
#else
/// <summary>对照组：纯 INPC + 手写命令，无任何 MVVM 框架依赖。</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _count;
    private string _pipeline = "等待点击…";

    public string Headline => "Baseline（无 ReactiveUI，纯 INPC）";
    public string Pipeline { get => _pipeline; private set => Set(ref _pipeline, value); }
    public string Throttled => "(baseline 无节流)";
    public ICommand IncrementCommand { get; }

    public MainViewModel()
    {
        IncrementCommand = new SimpleCommand(() =>
        {
            _count++;
            Pipeline = $"已点击 {_count} 次 · {DateTime.UtcNow:HH:mm:ss.fff}";
            Console.WriteLine($"[SPIKE] increment -> {_count}");
        });
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class SimpleCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
#endif

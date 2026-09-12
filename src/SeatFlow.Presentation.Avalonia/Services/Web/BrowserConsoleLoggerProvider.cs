using System;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Presentation.Avalonia.Services.Web;

/// <summary>
/// 浏览器端（WASM）控制台日志 Provider。
/// 浏览器无文件系统，Serilog File sink 不可用；将 <see cref="ILogger"/> 输出转发到
/// 浏览器开发者工具 Console，避免 WASM 端日志静默丢失。
/// </summary>
public sealed class BrowserConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger (string categoryName) => new BrowserConsoleLogger(categoryName);

    public void Dispose () { }

    private sealed class BrowserConsoleLogger (string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState> (TState state) where TState : notnull => null;

        public bool IsEnabled (LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState> (LogLevel logLevel , EventId eventId , TState state ,
            Exception? exception , Func<TState , Exception? , string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = $"[{logLevel}] {categoryName}: {formatter(state , exception)}";
            Console.WriteLine(exception is null ? message : $"{message}\n{exception}");
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using A_Pair.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace A_Pair.Infrastructure.Providers;

/// <summary>
/// 文件日志提供器，支持按大小 + 日期轮转。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider, IDisposable
{
    private readonly string _logDir;
    private readonly LogLevel _minLevel;
    private readonly long _maxFileSize;
    private readonly int _maxFiles;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private StreamWriter? _writer;
    private DateTime _currentDate;
    private int _fileIndex;
    private long _currentSize;
    private readonly object _lock = new();

    public FileLoggerProvider (string logDir, LogLevel minLevel, long maxFileSize, int maxFiles)
    {
        _logDir = logDir;
        _minLevel = minLevel;
        _maxFileSize = maxFileSize;
        _maxFiles = maxFiles;
        _currentDate = DateTime.Today;
        try { OpenWriter(); } catch { /* 启动时日志文件创建失败不影响运行 */ }
    }

    public ILogger CreateLogger (string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));
    }

    public void Dispose ()
    {
        foreach (var logger in _loggers.Values)
            logger.Dispose();
        _loggers.Clear();
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal void Write (string categoryName, LogLevel level, string message)
    {
        if (level < _minLevel) return;

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => level.ToString().ToUpperInvariant()
        };
        var line = $"{timestamp} [{levelStr}] [{categoryName}] {message}";

        lock (_lock)
        {
            if (_writer is null) return;

            // 检查日期轮转
            if (DateTime.Today != _currentDate)
            {
                _currentDate = DateTime.Today;
                _fileIndex = 0;
                OpenWriter();
            }

            // 检查大小轮转
            var bytes = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            if (_currentSize + bytes > _maxFileSize)
            {
                _fileIndex++;
                OpenWriter();
            }

            _writer.WriteLine(line);
            _writer.Flush();
            _currentSize += bytes;
        }

        // 清理过期文件
        CleanupOldFiles();
    }

    private void OpenWriter ()
    {
        try
        {
            _writer?.Dispose();
            Directory.CreateDirectory(_logDir);

            var dateStr = _currentDate.ToString("yyyyMMdd");
            var suffix = _fileIndex > 0 ? $"_{_fileIndex:000}" : "";
            var path = Path.Combine(_logDir, $"A_Pair_{dateStr}{suffix}.log");

            _writer = new StreamWriter(path, append: true, Encoding.UTF8);
            _currentSize = new FileInfo(path).Length;
        }
        catch
        {
            _writer = null;
        }
    }

    private void CleanupOldFiles ()
    {
        try
        {
            var files = Directory.GetFiles(_logDir, "A_Pair_*.log")
                .OrderByDescending(f => f)
                .Skip(_maxFiles);

            foreach (var file in files)
                File.Delete(file);
        }
        catch { /* 清理失败忽略 */ }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _provider;
        private readonly IDisposable? _scope;

        public FileLogger (string categoryName, FileLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
            _scope = provider as IDisposable;
        }

        public IDisposable? BeginScope<TState> (TState state) where TState : notnull => null;

        public bool IsEnabled (LogLevel logLevel) => logLevel >= _provider._minLevel;

        public void Log<TState> (LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message += Environment.NewLine + exception;
            }

            _provider.Write(_categoryName, logLevel, message);
        }

        public void Dispose () { }
    }
}

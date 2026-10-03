using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeatFlow.Presentation.Avalonia;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>启动自述：字段完整、无日志服务时不抛、写入一条中文自述。</summary>
public class StartupBannerTests
{
    [Fact]
    public void Format_应包含全部自述字段()
    {
        var info = new StartupBannerInfo(
            Version: "2.1.0",
            ReleaseTag: "v2.1.0",
            CommitId: "abc1234",
            BuildDate: "2026-10-03T12:00:00+08:00",
            Runtime: ".NET 10.0.0",
            OS: "测试OS",
            Architecture: "X64",
            DataDirectory: "/home/user/.local/share/SeatFlow",
            StartedAt: new DateTimeOffset(2026, 10, 3, 20, 30, 0, TimeSpan.FromHours(8)));

        var text = StartupBanner.Format(info);

        text.Should().Contain("启动自述")
            .And.Contain("2.1.0").And.Contain("v2.1.0").And.Contain("abc1234")
            .And.Contain("2026-10-03T12:00:00+08:00")
            .And.Contain(".NET 10.0.0")
            .And.Contain("测试OS").And.Contain("X64")
            .And.Contain("/home/user/.local/share/SeatFlow")
            .And.Contain("2026-10-03 20:30:00 +08:00");
    }

    [Fact]
    public void Write_无日志服务时不应抛出()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => StartupBanner.Write(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Write_应写入一条中文自述()
    {
        var capture = new CapturingLoggerProvider();
        using var provider = new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(capture).SetMinimumLevel(LogLevel.Information))
            .BuildServiceProvider();

        StartupBanner.Write(provider);

        capture.Messages.Should().ContainSingle()
            .Which.Should().Contain("启动自述")
            .And.Contain(VersionInfo.Version);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, exception));
        }
    }
}

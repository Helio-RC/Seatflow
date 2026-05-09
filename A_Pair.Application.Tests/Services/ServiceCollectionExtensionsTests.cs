using A_Pair.Core.Exporters;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Application.Tests.Services;

public class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _tempDir;

    public ServiceCollectionExtensionsTests ()
    {
        _tempDir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose ()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir , true);
    }

    [Fact]
    public void AddA_PairApplication_ShouldRegisterAllExpectedServices ()
    {
        var services = new ServiceCollection();
        var snapshotBasePath = Path.Combine(_tempDir , "Snapshots");
        var pluginsPath = Path.Combine(_tempDir , "Plugins");

        services.AddA_PairApplication(snapshotBasePath , pluginsPath);
        var provider = services.BuildServiceProvider();

        // 外观
        provider.GetService<IApplicationFacade>().Should().NotBeNull();
        // 策略
        var strategies = provider.GetServices<ISeatingStrategy>().ToList();
        strategies.Should().Contain(s => s is FixedSeatStrategy);
        strategies.Should().Contain(s => s is RandomFillStrategy);
        strategies.Should().Contain(s => s is FrontRowRotationStrategy);
        strategies.Should().Contain(s => s is DeskMateStrategy);
        // 冲突解决器
        provider.GetService<IConflictResolver>().Should().NotBeNull();
        // 导出器
        var exporters = provider.GetServices<ISeatingPlanExporter>().ToList();
        exporters.Should().HaveCount(3);
        // 学生写入器
        var writers = provider.GetServices<IStudentWriter>().ToList();
        writers.Should().HaveCount(3);
        // 快照存储库（接口）
        provider.GetService<ISeatingSnapshotRepository>().Should().NotBeNull();
        // 插件管理器
        provider.GetService<PluginManager>().Should().NotBeNull();
        // 插件配置服务
        provider.GetService<IPluginConfigurationService>().Should().NotBeNull();
        // 场地仓储
        provider.GetService<IVenueRepository>().Should().NotBeNull();
        // 应用设置仓储
        provider.GetService<IAppSettingsRepository>().Should().NotBeNull();
    }

    [Fact]
    public void AddA_PairApplication_CreatesExpectedDirectories ()
    {
        var services = new ServiceCollection();
        var snapshotBasePath = Path.Combine(_tempDir , "Snapshots");
        var pluginsPath = Path.Combine(_tempDir , "Plugins");

        services.AddA_PairApplication(snapshotBasePath , pluginsPath);
        var provider = services.BuildServiceProvider();

        // 解析 PluginManager 以触发其构造函数创建目录
        provider.GetService<PluginManager>();
        Directory.Exists(pluginsPath).Should().BeTrue();

        // 快照目录由 Repository 在保存时按需创建
        var snapshotRepo = provider.GetRequiredService<ISeatingSnapshotRepository>();
        snapshotRepo.SaveAsync(new SeatingSnapshot
        {
            Id = "test_snap",
            LayoutId = "venue1",
            CreatedAt = DateTime.UtcNow
        }).GetAwaiter().GetResult();
        Assert.True(Directory.Exists(Path.Combine(snapshotBasePath, "Assignments", "venue1")));
    }
}
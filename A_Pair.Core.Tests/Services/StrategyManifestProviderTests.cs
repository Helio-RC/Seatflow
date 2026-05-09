using A_Pair.Core.Services;

namespace A_Pair.Core.Tests.Services;

public class StrategyManifestProviderTests
{
    [Fact]
    public void GetBuiltInManifests_ReturnsAllFour ()
    {
        var provider = new StrategyManifestProvider();
        var manifests = provider.GetBuiltInManifests();
        manifests.Should().NotBeNull();
        manifests.Should().HaveCount(4);
    }

    [Fact]
    public void Manifests_HaveRequiredFields ()
    {
        var provider = new StrategyManifestProvider();
        foreach (var m in provider.GetBuiltInManifests())
        {
            m.Id.Should().NotBeNullOrEmpty();
            m.DisplayName.Should().NotBeNullOrEmpty();
            m.Category.Should().NotBeNullOrEmpty();
            m.DefaultPriority.Should().BeGreaterThan(0);
            m.Version.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void Manifests_HaveUniqueIds ()
    {
        var provider = new StrategyManifestProvider();
        var ids = provider.GetBuiltInManifests().Select(m => m.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ContainsExpectedStrategies ()
    {
        var provider = new StrategyManifestProvider();
        var ids = provider.GetBuiltInManifests().Select(m => m.Id).ToHashSet();
        ids.Should().Contain("FixedSeat");
        ids.Should().Contain("DeskMate");
        ids.Should().Contain("FrontRowRotation");
        ids.Should().Contain("RandomFill");
    }
}

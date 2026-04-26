using A_Pair.Contracts.Interfaces;

namespace A_Pair.Application.Tests.Plugins;

public class PluginStrategyAdapterTests
{
    [Fact]
    public void Properties_ShouldProxyCorrectly ()
    {
        var inner = Substitute.For<IPluginSeatingStrategy>();
        inner.Id.Returns("p1");
        inner.Name.Returns("Plugin1");
        inner.Priority.Returns(42);
        inner.IsEnabled.Returns(true);

        var adapter = new PluginStrategyAdapter(inner);
        adapter.Id.Should().Be("p1");
        adapter.Name.Should().Be("Plugin1");
        adapter.Priority.Should().Be(42);
        adapter.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnAdaptedResult ()
    {
        var inner = Substitute.For<IPluginSeatingStrategy>();
        inner.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(new PluginStrategyResult { Success = true , Message = "ok" });

        var adapter = new PluginStrategyAdapter(inner);
        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        var result = await adapter.ExecuteAsync(ws , CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Message.Should().Be("ok");
    }
}
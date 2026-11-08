using FluentAssertions;
using SeatFlow.Core;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

public class ArrangementCounterServiceEndpointTests
{
    [Fact]
    public void Constructor_ShouldUseBackendApiBase()
    {
        using var service = new ArrangementCounterService();

        service.BaseAddress.AbsoluteUri.Should().Be(SeatFlowEndpoints.ApiBase);
        service.BaseAddress.Host.Should().Be("api.seatflow.work");
    }

    [Fact]
    public void CounterPaths_ShouldResolveUnderApiPrefix()
    {
        using var service = new ArrangementCounterService();

        new Uri(service.BaseAddress, "/api/counters/token?name=arrangements").ToString()
            .Should().Be("https://api.seatflow.work/api/counters/token?name=arrangements");
        new Uri(service.BaseAddress, "/api/counters/public/increment").ToString()
            .Should().Be("https://api.seatflow.work/api/counters/public/increment");
    }
}

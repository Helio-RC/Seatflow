namespace SeatFlow.Core.Tests;

public class SeatFlowEndpointsTests
{
    [Fact]
    public void ApiBase_ShouldPointToBackendHostWithTrailingSlash()
    {
        SeatFlowEndpoints.ApiBase.Should().Be("https://api.seatflow.work/api/");
        SeatFlowEndpoints.ApiBase.Should().EndWith("/");
    }

    [Fact]
    public void DerivedEndpoints_ShouldComposeFromApiBase()
    {
        SeatFlowEndpoints.TelemetryUrl.Should().Be("https://api.seatflow.work/api/app/telemetry");
        SeatFlowEndpoints.ReleaseNotesUrl.Should().Be("https://api.seatflow.work/api/app/note/");
        SeatFlowEndpoints.OnlineLaunchUrl.Should().Be("https://api.seatflow.work/api/app/online/launch");
    }

    [Fact]
    public void CounterPath_ResolvedAgainstApiBase_ShouldNotDuplicateApiSegment()
    {
        // 计数器用带前导斜杠的路径（覆盖基址路径段）——必须仍是 /api/counters/…，不能变成 /api/api/counters/…
        var uri = new Uri(new Uri(SeatFlowEndpoints.ApiBase), "/api/counters/token?name=arrangements");

        uri.ToString().Should().Be("https://api.seatflow.work/api/counters/token?name=arrangements");
    }
}

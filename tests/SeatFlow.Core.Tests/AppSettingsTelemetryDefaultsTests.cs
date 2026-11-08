namespace SeatFlow.Core.Tests;

public class AppSettingsTelemetryDefaultsTests
{
    [Fact]
    public void TelemetryServerUrl_Default_ShouldUseEndpoints()
    {
        var settings = new AppSettings();

        settings.Telemetry.ServerUrl.Should().Be(SeatFlowEndpoints.TelemetryUrl);
    }
}

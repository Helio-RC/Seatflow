namespace A_Pair.Infrastructure.Tests.LayoutBuilders;

public class PolarLayoutBuilderTests
{
    [Fact]
    public void BuildPolar_ShouldCreateCorrectNumberOfSeats ()
    {
        var layout = PolarLayoutBuilder.BuildPolar(1.0 , 2 , 8);
        layout.Seats.Should().HaveCount(16);
        layout.LayoutType.Should().Be(LayoutType.Polar);
        var meta = layout.Metadata as PolarLayoutMetadata;
        meta.Should().NotBeNull();
        meta!.Rings.Should().Be(2);
        meta.SeatsPerRing.Should().Be(8);
    }
}
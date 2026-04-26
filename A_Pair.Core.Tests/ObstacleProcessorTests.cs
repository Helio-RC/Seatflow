using System.Linq;

namespace A_Pair.Core.Tests;

public class ObstacleProcessorTests
{
    [Fact]
    public void ApplyObstacles_SeatInsideObstacle_ShouldBeUnavailable ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata { OriginX = 0 , OriginY = 0 , HorizontalSpacing = 1 , VerticalSpacing = 1 } ,
            Obstacles = { new Obstacle { X = 1 , Y = 1 , Width = 1 , Height = 1 } } // covers (1,1) to (2,2)
        };
        layout.Seats.Add(new GridSeat { Row = 2 , Column = 2 }); // position (1,1) -> inside
        layout.Seats.Add(new GridSeat { Row = 3 , Column = 1 }); // position (0,2) -> outside

        ObstacleProcessor.ApplyObstacles(layout);
        layout.Seats[0].IsAvailable.Should().BeFalse();
        layout.Seats[1].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void ApplyObstacles_NoObstacles_ShouldDoNothing ()
    {
        var layout = new ClassroomLayoutDefinition();
        layout.Seats.Add(new GridSeat());
        ObstacleProcessor.ApplyObstacles(layout);
        layout.Seats[0].IsAvailable.Should().BeTrue();
    }
}
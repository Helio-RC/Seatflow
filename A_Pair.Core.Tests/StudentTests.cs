namespace A_Pair.Core.Tests;

public class StudentTests
{
    [Fact]
    public void NewStudent_ShouldHaveDefaultIdAndEmptyName ()
    {
        var s = new Student();
        s.Id.Should().NotBeNullOrEmpty();
        s.Name.Should().BeEmpty();
        s.Height.Should().BeNull();
        s.Gender.Should().BeNull();
        s.NeedsFrontRow.Should().BeFalse();
        s.FrontRowPreferenceScore.Should().Be(0);
        s.RecentSeatHistory.Should().NotBeNull();
        s.Extensions.Should().NotBeNull();
    }

    [Fact]
    public void RecentSeatHistory_ShouldBeCircular ()
    {
        var s = new Student();
        s.RecentSeatHistory.Add("seat1");
        s.RecentSeatHistory.Add("seat2");
        s.RecentSeatHistory.Add("seat3");
        s.RecentSeatHistory.Add("seat4"); // over capacity 3
        s.RecentSeatHistory.GetAll().Should().BeEquivalentTo(["seat2" , "seat3" , "seat4"]);
    }
}
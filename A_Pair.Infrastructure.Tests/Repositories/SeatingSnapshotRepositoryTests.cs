namespace A_Pair.Infrastructure.Tests.Repositories;

public class SeatingSnapshotRepositoryTests : IDisposable
{
    private readonly string _testDir;

    public SeatingSnapshotRepositoryTests ()
    {
        _testDir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose ()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir , true);
    }

    [Fact]
    public async Task SaveAndLoad_ShouldRoundTrip ()
    {
        var repo = new SeatingSnapshotRepository(_testDir);
        var snapshot = new SeatingSnapshot
        {
            Description = "Test" ,
            LayoutId = "venue1" ,
            SeatAssignments = new Dictionary<string , string> { { "s1" , "p1" } }
        };
        await repo.SaveAsync(snapshot);
        var loaded = repo.Load(snapshot.Id);
        loaded.Should().NotBeNull();
        loaded!.Description.Should().Be("Test");
        loaded.SeatAssignments["s1"].Should().Be("p1");
    }

    [Fact]
    public async Task ListByVenueAsync_ShouldFilterCorrectly ()
    {
        var repo = new SeatingSnapshotRepository(_testDir);
        var snap1 = new SeatingSnapshot { LayoutId = "venue1" };
        var snap2 = new SeatingSnapshot { LayoutId = "venue2" };
        await repo.SaveAsync(snap1);
        await repo.SaveAsync(snap2);

        var list = await repo.ListByVenueAsync("venue1");
        list.Should().HaveCount(1);
        list[0].LayoutId.Should().Be("venue1");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveFile ()
    {
        var repo = new SeatingSnapshotRepository(_testDir);
        var snap = new SeatingSnapshot();
        await repo.SaveAsync(snap);
        await repo.DeleteAsync(snap.Id);
        repo.Load(snap.Id).Should().BeNull();
    }
}
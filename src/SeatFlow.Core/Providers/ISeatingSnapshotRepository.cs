using SeatFlow.Core.Models;

namespace SeatFlow.Core.Providers;

public interface ISeatingSnapshotRepository
{
    Task SaveAsync (SeatingSnapshot snapshot , CancellationToken ct = default);
    Task<SeatingSnapshot?> LoadAsync (string id , CancellationToken ct = default);
    Task<IReadOnlyList<SeatingSnapshot>> ListByVenueAsync (string venueId , CancellationToken ct = default);
    Task<IReadOnlyList<SeatingSnapshot>> ListAllAsync ();
    Task DeleteAsync (string id , CancellationToken ct = default);
    Task SaveVenueInfoAsync (string venueId , VenueSnapshotInfo info , CancellationToken ct = default);
}

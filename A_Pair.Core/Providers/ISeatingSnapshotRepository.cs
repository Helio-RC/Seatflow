using A_Pair.Core.Models;

namespace A_Pair.Core.Providers;

public interface ISeatingSnapshotRepository
{
    Task SaveAsync(SeatingSnapshot snapshot, CancellationToken ct = default);
    SeatingSnapshot? Load(string id);
    Task<IReadOnlyList<SeatingSnapshot>> ListByVenueAsync(string venueId, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SaveVenueInfoAsync(string venueId, VenueSnapshotInfo info, CancellationToken ct = default);
}
using A_Pair.Core.Models;

namespace A_Pair.Core.Providers
{
    public interface ISeatingSnapshotRepository
    {
        Task SaveAsync (SeatingSnapshot snapshot);
        SeatingSnapshot? Load (string id);
        Task<IReadOnlyList<SeatingSnapshot>> ListByVenueAsync (string venueId);
        Task DeleteAsync (string id);
        Task SaveVenueInfoAsync (string venueId, VenueSnapshotInfo info);
    }
}
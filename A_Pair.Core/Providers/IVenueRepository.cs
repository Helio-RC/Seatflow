using A_Pair.Core.Models;

namespace A_Pair.Core.Providers
{
    public interface IVenueRepository
    {
        Task SaveAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default);
        Task<ClassroomLayoutDefinition?> LoadAsync (string venueId , CancellationToken cancellationToken = default);
        Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default);
    }
}
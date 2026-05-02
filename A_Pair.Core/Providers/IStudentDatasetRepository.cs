using A_Pair.Core.Models;

namespace A_Pair.Core.Providers;

public interface IStudentDatasetRepository
{
    Task SaveAsync (string id , string name , List<Student> students ,
        string? originalFileName = null , CancellationToken ct = default);
    Task<List<Student>?> LoadAsync (string id , CancellationToken ct = default);
    Task<IReadOnlyList<StudentDatasetInfo>> ListAsync (CancellationToken ct = default);
    Task DeleteAsync (string id , CancellationToken ct = default);
}

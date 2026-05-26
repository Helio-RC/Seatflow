using A_Pair.Core.Models;

namespace A_Pair.Core.Providers;

public interface IStudentDatasetRepository
{
    Task SaveAsync (string id , string name , List<Student> students ,
        string? originalFileName = null , CancellationToken ct = default);
    Task<List<Student>?> LoadAsync (string id , CancellationToken ct = default);
    Task<IReadOnlyList<StudentDatasetInfo>> ListAsync (CancellationToken ct = default);
    Task DeleteAsync (string id , CancellationToken ct = default);
    /// <summary>原地重命名数据集，保持 ID 不变。</summary>
    Task RenameAsync (string id , string newName , CancellationToken ct = default);
    /// <summary>获取数据集的 ContentHash（轻量读取）。</summary>
    Task<string?> GetContentHashAsync (string id , CancellationToken ct = default);
}

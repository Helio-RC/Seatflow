using A_Pair.Core.Models;

namespace A_Pair.Core.Providers
{
    public interface IStudentWriter
    {
        Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default);
    }
}
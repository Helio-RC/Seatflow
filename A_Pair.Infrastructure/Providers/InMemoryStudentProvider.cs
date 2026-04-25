using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers
{
    public class InMemoryStudentProvider : IStudentProvider
    {
        public Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
        {
            var list = new List<Student>
            {
                new() { Name = "Alice" },
                new() { Name = "Bob" },
                new() { Name = "Charlie" }
            };
            return Task.FromResult(list);
        }
    }
}

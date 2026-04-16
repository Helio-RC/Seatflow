using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers
{
    public class InMemoryStudentProvider : IStudentProvider
    {
        public Task<List<Student>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            var list = new List<Student>
            {
                new Student { Name = "Alice" },
                new Student { Name = "Bob" },
                new Student { Name = "Charlie" }
            };
            return Task.FromResult(list);
        }
    }
}

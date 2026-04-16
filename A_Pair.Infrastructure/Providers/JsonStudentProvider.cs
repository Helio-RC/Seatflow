using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers
{
    public class JsonStudentProvider : IStudentProvider
    {
        public async Task<List<Student>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return new List<Student>();
            using var stream = File.OpenRead(source);
            var list = await JsonSerializer.DeserializeAsync<List<Student>>(stream, cancellationToken: cancellationToken);
            return list ?? new List<Student>();
        }
    }
}

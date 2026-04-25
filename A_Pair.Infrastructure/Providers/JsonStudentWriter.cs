using System.Text.Json;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers
{
    public class JsonStudentWriter : IStudentWriter
    {
        public async Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default)
        {
            var roster = new RosterFile
            {
                Version = "1.0" ,
                Students = new List<Student>(students)
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true ,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(roster , options);
            await File.WriteAllTextAsync(path , json , cancellationToken);
        }
    }
}
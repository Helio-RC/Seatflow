using System.Globalization;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using CsvHelper;

namespace A_Pair.Infrastructure.Providers
{
    public class CsvStudentWriter : IStudentWriter
    {
        public async Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default)
        {
            await using var writer = new StreamWriter(path);
            await using var csv = new CsvWriter(writer , CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(students , cancellationToken);
        }
    }
}
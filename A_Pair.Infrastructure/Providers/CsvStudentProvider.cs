using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using CsvHelper;

namespace A_Pair.Infrastructure.Providers
{
    public class CsvStudentProvider : IStudentProvider
    {
        public async Task<List<Student>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(source)) return new List<Student>();

            using var reader = new StreamReader(source);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var records = csv.GetRecordsAsync<Student>();
            var list = new List<Student>();
            await foreach (var r in records.WithCancellation(cancellationToken))
            {
                list.Add(r);
            }

            return list;
        }
    }
}

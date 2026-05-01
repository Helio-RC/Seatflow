using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Core.Models;
using A_Pair.Core.Providers;
using A_Pair.Infrastructure.Providers;

namespace A_Pair.Infrastructure.Providers;

public class CompositeStudentProvider : IStudentProvider
{
    private readonly Dictionary<string, IStudentProvider> _providers;

    public CompositeStudentProvider()
    {
        _providers = new(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"] = new CsvStudentProvider(),
            [".xlsx"] = new XlsxStudentProvider(),
            [".json"] = new JsonStudentProvider()
        };
    }

    public async Task<List<Student>> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return [];

        var ext = Path.GetExtension(source);
        if (_providers.TryGetValue(ext, out var provider))
            return await provider.LoadAsync(source, cancellationToken);

        return [];
    }
}

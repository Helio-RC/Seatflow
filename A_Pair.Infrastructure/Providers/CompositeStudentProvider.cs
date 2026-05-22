using A_Pair.Core.Models;
using A_Pair.Core.Providers;

namespace A_Pair.Infrastructure.Providers;

public class CompositeStudentProvider : IStudentProvider
{
    private readonly Dictionary<string, IStudentProvider> _providers;

    public CompositeStudentProvider(
        CsvStudentProvider csvProvider,
        XlsxStudentProvider xlsxProvider,
        JsonStudentProvider jsonProvider)
    {
        _providers = new(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"] = csvProvider,
            [".xlsx"] = xlsxProvider,
            [".json"] = jsonProvider
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

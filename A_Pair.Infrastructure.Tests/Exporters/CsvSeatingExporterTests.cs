namespace A_Pair.Infrastructure.Tests.Exporters;

public class CsvSeatingExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldCreateCsvFile ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" },
                { "seat2", "student2" }
            }
        };
        var exporter = new CsvSeatingExporter();
        var path = Path.GetTempFileName() + ".csv";

        try
        {
            await exporter.ExportAsync(plan , path , CancellationToken.None);
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("seat1,student1");
            content.Should().Contain("seat2,student2");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_WithAnonymize_ShouldMaskStudentId ()
    {
        var plan = new SeatingPlan
        {
            Assignments = new Dictionary<string , string>
            {
                { "seat1", "student1" }
            }
        };
        var exporter = new CsvSeatingExporter();
        var path = Path.GetTempFileName() + ".csv";

        try
        {
            var options = new ExportOptions { Format = ExportFormat.Csv , Anonymize = true };
            await exporter.ExportAsync(plan , path , options , CancellationToken.None);
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("***");
            content.Should().NotContain("student1");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
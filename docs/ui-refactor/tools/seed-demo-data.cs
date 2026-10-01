#:project ../../../src/SeatFlow.Core/SeatFlow.Core.csproj
#:project ../../../src/SeatFlow.Infrastructure/SeatFlow.Infrastructure.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

// 生成阶段 2 性能采样所需的演示数据（与仓储写入格式完全一致）。
// 运行：dotnet run seed-demo-data.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Infrastructure.Serialization;

var opts = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
opts.Converters.Add(new SeatJsonConverter());

var outDir = args.Length > 0 ? args[0] : "/tmp/seatflow-seed";
Directory.CreateDirectory(outDir);

// ---------- 会场：8×8 网格，每桌 2 人，第 4 列/行后过道 ----------
var seats = new List<Seat>();
for (var r = 1; r <= 8; r++)
{
    for (var c = 1; c <= 8; c++)
    {
        seats.Add(new GridSeat
        {
            Id = $"demo-s-{r:D2}-{c:D2}",
            Row = r,
            Column = c,
            LogicalGroup = $"第{(c - 1) / 2 + 1}组"
        });
    }
}

var layout = new ClassroomLayoutDefinition
{
    Id = "demo-layout-01",
    Name = "演示教室（64座）",
    LayoutType = LayoutType.Grid,
    Seats = seats,
    Obstacles = [],
    Metadata = new GridLayoutMetadata
    {
        Rows = 8,
        Columns = 8,
        SeatsPerDesk = 2,
        HorizontalSpacing = 1.0,
        VerticalSpacing = 1.0,
        IntraDeskSpacing = 12,
        InterDeskSpacing = 40,
        AisleAfterColumns = [4],
        AisleAfterRows = [4],
        AisleWidth = 60,
        FrontRowCount = 1,
        HasPodium = true,
        HasFrontDoor = false
    }
};

var venueFile = new VenueFile
{
    Version = "1.1",
    VenueId = "demo-venue-01",
    Layout = layout
};
var venueJson = JsonSerializer.Serialize(venueFile, opts);
venueFile.ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(venueJson))).ToLowerInvariant();
var venuePath = Path.Combine(outDir, "demo-venue-01.venue.json");
File.WriteAllText(venuePath, JsonSerializer.Serialize(venueFile, opts));
Console.WriteLine($"venue  -> {venuePath} ({seats.Count} seats)");

// ---------- 名单：240 人（中文姓名，性别交替，身高 150–189，每 30 人一个前排需求） ----------
string[] surnames = ["王", "李", "张", "刘", "陈", "杨", "赵", "黄", "周", "吴", "徐", "孙", "胡", "朱", "高", "林", "何", "郭", "马", "罗"];
string[] given = ["伟", "芳", "娜", "敏", "静", "磊", "军", "洋", "勇", "艳", "杰", "涛", "明", "超", "秀英", "霞", "平", "刚", "桂英", "文博"];

var students = new List<Student>();
for (var i = 0; i < 240; i++)
{
    students.Add(new Student
    {
        Id = $"demo-stu-{i:D4}",
        Name = surnames[i % surnames.Length] + given[(i * 7) % given.Length],
        Height = 150 + (i * 7) % 40,
        Gender = i % 2 == 0 ? Gender.Male : Gender.Female,
        NeedsFrontRow = i % 30 == 0
    });
}

var sorted = students.OrderBy(s => s.Id).ToList();
var roster = new RosterFile
{
    Version = "1.1",
    Description = "演示名单（240人）",
    Students = sorted,
    Metadata = new Dictionary<string, object>
    {
        ["importedAt"] = DateTime.Now.ToString("O"),
        ["studentCount"] = students.Count,
        ["originalFileName"] = "demo-roster.csv"
    }
};
roster.StudentsHash = Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sorted, opts)))).ToLowerInvariant();
var rosterPath = Path.Combine(outDir, "demo-roster-01.roster.json");
File.WriteAllText(rosterPath, JsonSerializer.Serialize(roster, opts));
Console.WriteLine($"roster -> {rosterPath} ({students.Count} students)");

// ---------- AppSettings：确定性会话状态（跳过遥测弹窗与引导，中文/浅色） ----------
// 注意：AppSettings 仓储使用默认（PascalCase）序列化 + 大小写敏感读取（JsonOptions.WriteIndented），
// 与 venue/roster 的 camelCase 不同。
var settingsOpts = new JsonSerializerOptions { WriteIndented = true };
var settings = new AppSettings
{
    Version = "1.1",
    Theme = ThemeMode.Light,
    Language = "zh-CN",
    LastVenueId = "demo-venue-01",
    IsFirstLaunch = false,
    CompletedPageGuides = new Dictionary<string, bool> { ["FreeformManagement"] = true },
    Telemetry = new TelemetryConfig { Enabled = false, ConsentShown = true },
    KeyboardShortcuts = new KeyboardShortcutConfig()
};
var settingsPath = Path.Combine(outDir, "AppSettings.json");
File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, settingsOpts));
Console.WriteLine($"settings -> {settingsPath}");

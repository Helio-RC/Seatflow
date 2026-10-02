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

// ---------- 会场生成辅助 ----------
static VenueFile CreateGridVenue(string venueId, string layoutId, string name, int rows, int cols, int seatsPerDesk,
    int intraDesk = 12, int interDesk = 40, int aisleAfterCol = 0, int aisleAfterRow = 0, int aisleWidth = 60)
{
    var seatList = new List<Seat>();
    for (var r = 1; r <= rows; r++)
    {
        for (var c = 1; c <= cols; c++)
        {
            seatList.Add(new GridSeat
            {
                Id = $"{venueId}-s-{r:D3}-{c:D3}",
                Row = r,
                Column = c,
                LogicalGroup = $"第{((c - 1) / seatsPerDesk) + 1}组"
            });
        }
    }

    return new VenueFile
    {
        Version = "1.1",
        VenueId = venueId,
        Layout = new ClassroomLayoutDefinition
        {
            Id = layoutId,
            Name = name,
            LayoutType = LayoutType.Grid,
            Seats = seatList,
            Obstacles = [],
            Metadata = new GridLayoutMetadata
            {
                Rows = rows,
                Columns = cols,
                SeatsPerDesk = seatsPerDesk,
                HorizontalSpacing = 1.0,
                VerticalSpacing = 56,
                IntraDeskSpacing = intraDesk,
                InterDeskSpacing = interDesk,
                AisleAfterColumns = aisleAfterCol > 0 ? [aisleAfterCol] : [],
                AisleAfterRows = aisleAfterRow > 0 ? [aisleAfterRow] : [],
                AisleWidth = aisleWidth,
                FrontRowCount = 1,
                HasPodium = true,
                HasFrontDoor = false
            }
        }
    };
}

static VenueFile CreatePolarVenue(string venueId, string layoutId, string name, int[] ringCounts, double radiusStep, double origin = 300)
{
    var seatList = new List<Seat>();
    for (var ring = 1; ring <= ringCounts.Length; ring++)
    {
        var count = ringCounts[ring - 1];
        for (var i = 0; i < count; i++)
        {
            seatList.Add(new PolarSeat
            {
                Id = $"{venueId}-s-{ring:D2}-{i + 1:D2}",
                Ring = ring,
                Radius = ring * radiusStep,
                AngleDegrees = 360.0 * i / count,
                LogicalGroup = $"第{ring}环"
            });
        }
    }

    return new VenueFile
    {
        Version = "1.1",
        VenueId = venueId,
        Layout = new ClassroomLayoutDefinition
        {
            Id = layoutId,
            Name = name,
            LayoutType = LayoutType.Polar,
            Seats = seatList,
            Obstacles = [],
            Metadata = new PolarLayoutMetadata
            {
                Rings = ringCounts.Length,
                SeatsPerRing = ringCounts.Max(),
                RadiusStep = radiusStep,
                OriginX = origin,
                OriginY = origin,
                RingSeatCounts = [.. ringCounts],
                FrontRowCount = 1,
                HasPodium = true
            }
        }
    };
}

static VenueFile CreateFreeformVenue(string venueId, string layoutId, string name, int rows, int cols)
{
    var seatList = new List<Seat>();
    for (var r = 0; r < rows; r++)
    {
        for (var c = 0; c < cols; c++)
        {
            seatList.Add(new FreeformSeat
            {
                Id = $"{venueId}-s-{r + 1:D2}-{c + 1:D2}",
                X = 80 + (c * 96) + ((r % 2) * 24),
                Y = 80 + (r * 72),
                Row = r + 1,
                Column = c + 1,
                LogicalGroup = $"第{c + 1}列"
            });
        }
    }

    return new VenueFile
    {
        Version = "1.1",
        VenueId = venueId,
        Layout = new ClassroomLayoutDefinition
        {
            Id = layoutId,
            Name = name,
            LayoutType = LayoutType.Freeform,
            Seats = seatList,
            Obstacles = [],
            Metadata = new LayoutMetadata()
        }
    };
}

static void WriteVenue(VenueFile file, string outDir, string fileName, JsonSerializerOptions opts)
{
    var json = JsonSerializer.Serialize(file, opts);
    file.ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    var path = Path.Combine(outDir, fileName);
    File.WriteAllText(path, JsonSerializer.Serialize(file, opts));
    Console.WriteLine($"venue  -> {path} ({file.Layout.Seats.Count} seats)");
}

WriteVenue(CreateGridVenue("demo-venue-01", "demo-venue-01", "演示教室（64座）", 8, 8, 2, 12, 40, 4, 4, 60), outDir, "demo-venue-01.venue.json", opts);
WriteVenue(CreatePolarVenue("demo-venue-polar", "demo-venue-polar", "演示环形教室（24座）", [8, 8, 8], 60), outDir, "demo-venue-polar.venue.json", opts);
WriteVenue(CreateFreeformVenue("demo-venue-freeform", "demo-venue-freeform", "演示自由布局（30座）", 6, 5), outDir, "demo-venue-freeform.venue.json", opts);
WriteVenue(CreateGridVenue("demo-venue-large", "demo-venue-large", "演示大教室（300座）", 15, 20, 2, 12, 40, 10, 8, 60), outDir, "demo-venue-large.venue.json", opts);
WriteVenue(CreateGridVenue("demo-venue-huge", "demo-venue-huge", "演示超大教室（800座）", 25, 32, 2, 10, 28, 0, 0, 0), outDir, "demo-venue-huge.venue.json", opts);

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

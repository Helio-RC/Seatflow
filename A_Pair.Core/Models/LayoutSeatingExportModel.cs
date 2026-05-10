namespace A_Pair.Core.Models;

/// <summary>
/// 结构化座位安排导出模型，包含布局结构和学生姓名信息。
/// </summary>
public class LayoutSeatingExportModel
{
    public string LayoutName { get; set; } = string.Empty;
    public LayoutType LayoutType { get; set; }
    public List<ExportRow> Rows { get; set; } = [];

    /// <summary>
    /// 从布局定义和座位分配构建导出模型。
    /// </summary>
    public static LayoutSeatingExportModel FromLayout(
        ClassroomLayoutDefinition layout,
        Dictionary<string, string> assignments,
        Dictionary<string, string> studentNames)
    {
        return layout.LayoutType switch
        {
            LayoutType.Grid => BuildGrid(layout, assignments, studentNames),
            LayoutType.Polar => BuildPolar(layout, assignments, studentNames),
            _ => BuildFreeform(layout, assignments, studentNames)
        };
    }

    private static LayoutSeatingExportModel BuildGrid(
        ClassroomLayoutDefinition layout,
        Dictionary<string, string> assignments,
        Dictionary<string, string> studentNames)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name, LayoutType = LayoutType.Grid };
        if (layout.Metadata is not GridLayoutMetadata meta) return model;

        var seatMap = layout.Seats.OfType<GridSeat>()
            .ToDictionary(s => (s.Row, s.Column), s => s);
        var aisleCols = new HashSet<int>(meta.AisleAfterColumns);
        var aisleRows = new HashSet<int>(meta.AisleAfterRows);
        var emptyPos = new HashSet<(int, int)>(
            meta.EmptyPositions.Select(p => (p.Row, p.Column)));

        // 计算最大列数（含过道列）
        int maxCol = meta.Columns + aisleCols.Count;

        // 讲台行（如果 HasPodium）
        if (meta.HasPodium)
        {
            var podiumRow = new ExportRow();
            podiumRow.Cells.Add(new ExportCell { IsPodium = true, Text = "讲台", ColSpan = maxCol });
            model.Rows.Add(podiumRow);
        }

        for (int r = 1; r <= meta.Rows; r++)
        {
            var row = new ExportRow();
            for (int c = 1; c <= meta.Columns; c++)
            {
                // 过道列
                if (aisleCols.Contains(c - 1))
                    row.Cells.Add(new ExportCell { IsAisle = true, Text = "过道" });

                if (emptyPos.Contains((r, c)))
                    row.Cells.Add(new ExportCell { Text = "" });
                else if (seatMap.TryGetValue((r, c), out var seat))
                {
                    string label = $"R{r}C{c}";
                    string? studentName = null;
                    if (assignments.TryGetValue(seat.Id, out var sid) &&
                        studentNames.TryGetValue(sid, out var name))
                        studentName = name;

                    row.Cells.Add(new ExportCell
                    {
                        IsSeat = true,
                        Text = studentName ?? label
                    });
                }
                else
                {
                    row.Cells.Add(new ExportCell { Text = "" });
                }
            }
            model.Rows.Add(row);

            // 过道行
            if (aisleRows.Contains(r))
                model.Rows.Add(new ExportRow { Cells = { new ExportCell { IsAisle = true, Text = "过道", ColSpan = maxCol } } });
        }

        return model;
    }

    private static LayoutSeatingExportModel BuildPolar(
        ClassroomLayoutDefinition layout,
        Dictionary<string, string> assignments,
        Dictionary<string, string> studentNames)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name, LayoutType = LayoutType.Polar };
        if (layout.Metadata is not PolarLayoutMetadata meta) return model;

        var polarSeats = layout.Seats.OfType<PolarSeat>().ToList();
        var rings = polarSeats.GroupBy(s => s.Ring).OrderBy(g => g.Key).ToList();

        if (rings.Count == 0) return model;

        int maxRingSeats = rings.Max(g => g.Count());

        // 讲台行（居中）
        if (meta.HasPodium && meta.PodiumRadius > 0)
        {
            var podiumRow = new ExportRow();
            int padBefore = (maxRingSeats - 1) / 2;
            int padAfter = maxRingSeats - 1 - padBefore;
            for (int i = 0; i < padBefore; i++)
                podiumRow.Cells.Add(new ExportCell { Text = "" });
            podiumRow.Cells.Add(new ExportCell { IsPodium = true, Text = "讲台" });
            for (int i = 0; i < padAfter; i++)
                podiumRow.Cells.Add(new ExportCell { Text = "" });
            model.Rows.Add(podiumRow);
        }

        // 从内环到外环，等腰梯形排列
        foreach (var ringGroup in rings)
        {
            var row = new ExportRow();
            var seatsInRing = ringGroup.OrderBy(s => s.AngleDegrees).ToList();
            int ringSeatCount = seatsInRing.Count;

            // 居中：计算前置空格
            int padding = (maxRingSeats - ringSeatCount) / 2;
            for (int i = 0; i < padding; i++)
                row.Cells.Add(new ExportCell { Text = "" });

            foreach (var seat in seatsInRing)
            {
                string label = $"环{seat.Ring} {seat.AngleDegrees:F0}°";
                string? studentName = null;
                if (assignments.TryGetValue(seat.Id, out var sid) &&
                    studentNames.TryGetValue(sid, out var name))
                    studentName = name;

                row.Cells.Add(new ExportCell
                {
                    IsSeat = true,
                    Text = studentName ?? label
                });
            }

            // 后置空格补齐
            while (row.Cells.Count < maxRingSeats)
                row.Cells.Add(new ExportCell { Text = "" });

            model.Rows.Add(row);
        }

        return model;
    }

    private static LayoutSeatingExportModel BuildFreeform(
        ClassroomLayoutDefinition layout,
        Dictionary<string, string> assignments,
        Dictionary<string, string> studentNames)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name, LayoutType = LayoutType.Freeform };
        var freeSeats = layout.Seats.OfType<FreeformSeat>().ToList();

        // Freeform 不要求保持布局，简单按行列表
        int idx = 0;
        foreach (var seat in freeSeats)
        {
            idx++;
            string label = $"#{idx} ({seat.X:F0}, {seat.Y:F0})";
            string? studentName = null;
            if (assignments.TryGetValue(seat.Id, out var sid) &&
                studentNames.TryGetValue(sid, out var name))
                studentName = name;

            // 讲台和门作为单独行
            var row = new ExportRow();
            row.Cells.Add(new ExportCell
            {
                IsSeat = true,
                Text = studentName ?? label
            });
            model.Rows.Add(row);
        }

        foreach (var obs in layout.Obstacles)
        {
            var row = new ExportRow();
            row.Cells.Add(new ExportCell { Text = $"[{obs.Type}] ({obs.X:F0}, {obs.Y:F0})" });
            model.Rows.Add(row);
        }

        return model;
    }
}

public class ExportRow
{
    public List<ExportCell> Cells { get; set; } = [];
}

public class ExportCell
{
    public string Text { get; set; } = "";
    public bool IsSeat { get; set; }
    public bool IsAisle { get; set; }
    public bool IsPodium { get; set; }
    public int ColSpan { get; set; } = 1;
}

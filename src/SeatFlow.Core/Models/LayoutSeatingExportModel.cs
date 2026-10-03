using SeatFlow.Core.DomainServices;

namespace SeatFlow.Core.Models;

/// <summary>
/// 结构化座位安排导出模型，包含布局结构、学生姓名信息与门等边侧标注。
/// 门按几何坐标落入「所在行 + 左侧/右侧边列」，供四种导出格式统一消费。
/// </summary>
public class LayoutSeatingExportModel
{
    public string LayoutName { get; set; } = string.Empty;
    public LayoutType LayoutType { get; set; }
    public List<ExportRow> Rows { get; set; } = [];

    /// <summary>是否存在左侧门标注列。</summary>
    public bool HasLeftMargin => Rows.Any(r => !string.IsNullOrEmpty(r.LeftMarginText));

    /// <summary>是否存在右侧门标注列。</summary>
    public bool HasRightMargin => Rows.Any(r => !string.IsNullOrEmpty(r.RightMarginText));

    /// <summary>
    /// 应用导出视角：教师视角 = 行前后反转 + 列左右镜像（门标注随之换侧）。
    /// 学生视角为默认生成顺序，不做改动。
    /// </summary>
    public void ApplyPerspective(LayoutPerspective perspective)
    {
        if (perspective != LayoutPerspective.TeacherView) return;

        Rows.Reverse();
        foreach (var row in Rows)
        {
            row.Cells.Reverse();
            (row.LeftMarginText, row.RightMarginText) = (row.RightMarginText, row.LeftMarginText);
        }
    }

    public static LayoutSeatingExportModel FromLayout(
        ClassroomLayoutDefinition layout,
        Dictionary<string, string> assignments,
        Dictionary<string, string> studentNames,
        ExportTexts? texts = null)
    {
        texts ??= new ExportTexts();
        return layout.LayoutType switch
        {
            LayoutType.Grid => BuildGrid(layout, assignments, studentNames, texts),
            LayoutType.Polar => BuildPolar(layout, assignments, studentNames, texts),
            _ => BuildFreeform(layout, assignments, studentNames, texts)
        };
    }

    private static LayoutSeatingExportModel BuildGrid(
        ClassroomLayoutDefinition layout,
        Dictionary<string, string> assignments,
        Dictionary<string, string> studentNames,
        ExportTexts texts)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name, LayoutType = LayoutType.Grid };
        if (layout.Metadata is not GridLayoutMetadata meta) return model;

        var gridSeats = layout.Seats.OfType<GridSeat>().ToList();
        var seatMap = gridSeats.ToDictionary(s => (s.Row, s.Column), s => s);
        var aisleColSet = new HashSet<int>(meta.AisleAfterColumns);
        var aisleRowSet = new HashSet<int>(meta.AisleAfterRows);
        var emptyPos = new HashSet<(int, int)>(
            meta.EmptyPositions.Select(p => (p.Row, p.Column)));

        // 构建列计划: 每个槽位是 (列号, 是否过道)
        var colPlan = new List<(int? Col, bool IsAisle)>();
        for (int c = 1; c <= meta.Columns; c++)
        {
            colPlan.Add((c, false));
            if (aisleColSet.Contains(c))
                colPlan.Add((null, true));
        }

        // 讲台行（居中）
        if (meta.HasPodium)
        {
            var podiumRow = new ExportRow();
            int mid = colPlan.Count / 2;
            for (int i = 0; i < colPlan.Count; i++)
            {
                if (i == mid)
                    podiumRow.Cells.Add(new ExportCell { IsPodium = true, Text = texts.Podium });
                else
                    podiumRow.Cells.Add(new ExportCell { Text = "" });
            }
            model.Rows.Add(podiumRow);
        }

        int maxRows = meta.ColumnRowCounts is { Count: > 0 }
            ? meta.ColumnRowCounts.Max()
            : meta.Rows;

        // 网格行号 → model.Rows 下标（供门落行使用）
        var seatRowIndex = new Dictionary<int, int>();

        for (int r = 1; r <= maxRows; r++)
        {
            var row = new ExportRow();
            foreach (var (col, isAisle) in colPlan)
            {
                if (isAisle)
                {
                    row.Cells.Add(new ExportCell { IsAisle = true, Text = "" });
                }
                else if (col.HasValue && emptyPos.Contains((r, col.Value)))
                {
                    row.Cells.Add(new ExportCell { Text = "" });
                }
                else if (col.HasValue && seatMap.TryGetValue((r, col.Value), out var seat))
                {
                    string? studentName = null;
                    bool isUnassigned = true;
                    if (assignments.TryGetValue(seat.Id, out var sid) &&
                        studentNames.TryGetValue(sid, out var name))
                    {
                        studentName = name;
                        isUnassigned = false;
                    }

                    row.Cells.Add(new ExportCell
                    {
                        IsSeat = true,
                        IsUnassigned = isUnassigned,
                        Text = studentName ?? texts.Unassigned
                    });
                }
                else
                {
                    row.Cells.Add(new ExportCell { Text = "" });
                }
            }

            seatRowIndex[r] = model.Rows.Count;
            model.Rows.Add(row);

            // 过道行
            if (aisleRowSet.Contains(r))
            {
                var aisleRow = new ExportRow();
                for (int i = 0; i < colPlan.Count; i++)
                    aisleRow.Cells.Add(new ExportCell { IsAisle = true, Text = "" });
                model.Rows.Add(aisleRow);
            }
        }

        ApplyGridDoors(model, gridSeats, meta, layout.Obstacles, seatRowIndex, texts);

        return model;
    }

    /// <summary>
    /// 把门按几何坐标落到「最近座位行 + 左侧/右侧边列」。
    /// 门坐标（左上角 + 宽高）与座位坐标同源于会场坐标系。
    /// </summary>
    private static void ApplyGridDoors(
        LayoutSeatingExportModel model,
        List<GridSeat> gridSeats,
        GridLayoutMetadata meta,
        List<Obstacle> obstacles,
        Dictionary<int, int> seatRowIndex,
        ExportTexts texts)
    {
        var doors = obstacles.Where(o => o.Type == "Door").ToList();
        if (doors.Count == 0 || gridSeats.Count == 0) return;

        // 每行座位顶部 Y
        var rowTop = gridSeats
            .GroupBy(s => s.Row)
            .ToDictionary(g => g.Key, g => SeatGeometryHelper.GetPosition(g.First(), meta).Y);
        var rowOrder = rowTop.Keys.OrderBy(r => r).ToList();
        if (rowOrder.Count == 0) return;

        // 行中心 Y：相邻行顶部的中点；最后一行按竖直间距外推
        var rowCenter = new Dictionary<int, double>();
        for (int i = 0; i < rowOrder.Count; i++)
        {
            double y = rowTop[rowOrder[i]];
            double nextY = i + 1 < rowOrder.Count
                ? rowTop[rowOrder[i + 1]]
                : y + Math.Max(meta.VerticalSpacing, 20);
            rowCenter[rowOrder[i]] = (y + nextY) / 2.0;
        }

        double minX = double.MaxValue, maxX = double.MinValue;
        foreach (var seat in gridSeats)
        {
            double x = SeatGeometryHelper.GetPosition(seat, meta).X;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }
        double centerX = (minX + maxX) / 2.0;

        var ordered = doors.OrderBy(d => d.Y).ThenBy(d => d.X).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var door = ordered[i];
            double doorCenterY = door.Y + door.Height / 2.0;
            double doorCenterX = door.X + door.Width / 2.0;

            int nearestRow = rowOrder
                .OrderBy(r => Math.Abs(rowCenter[r] - doorCenterY))
                .First();
            if (!seatRowIndex.TryGetValue(nearestRow, out var modelIndex)) continue;

            string label = DoorLabel(texts, i, ordered.Count);
            var row = model.Rows[modelIndex];
            if (doorCenterX < centerX)
                row.LeftMarginText = JoinDoorLabel(row.LeftMarginText, label, texts);
            else
                row.RightMarginText = JoinDoorLabel(row.RightMarginText, label, texts);
        }
    }

    private static LayoutSeatingExportModel BuildPolar(
        ClassroomLayoutDefinition layout,
        Dictionary<string, string> assignments,
        Dictionary<string, string> studentNames,
        ExportTexts texts)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name, LayoutType = LayoutType.Polar };
        if (layout.Metadata is not PolarLayoutMetadata meta) return model;

        var polarSeats = layout.Seats.OfType<PolarSeat>().ToList();
        var rings = polarSeats.GroupBy(s => s.Ring).OrderBy(g => g.Key).ToList();

        if (rings.Count == 0) return model;

        int maxRingSeats = Math.Min(rings.Max(g => g.Count()), 30);

        // 讲台行（居中）
        if (meta.HasPodium && meta.PodiumRadius > 0)
        {
            var podiumRow = new ExportRow();
            int padBefore = (maxRingSeats - 1) / 2;
            for (int i = 0; i < padBefore; i++)
                podiumRow.Cells.Add(new ExportCell { Text = "" });
            podiumRow.Cells.Add(new ExportCell { IsPodium = true, Text = texts.Podium });
            while (podiumRow.Cells.Count < maxRingSeats)
                podiumRow.Cells.Add(new ExportCell { Text = "" });
            model.Rows.Add(podiumRow);
        }

        // 环号 → model.Rows 下标（供门落环使用）
        var ringRowIndex = new Dictionary<int, int>();

        // 从内环到外环，等腰梯形排列
        foreach (var ringGroup in rings)
        {
            var row = new ExportRow();
            var seatsInRing = ringGroup.OrderBy(s => s.AngleDegrees).ToList();
            int ringSeatCount = Math.Min(seatsInRing.Count, maxRingSeats);

            int padding = (maxRingSeats - ringSeatCount) / 2;
            for (int i = 0; i < padding; i++)
                row.Cells.Add(new ExportCell { Text = "" });

            foreach (var seat in seatsInRing.Take(maxRingSeats))
            {
                string? studentName = null;
                bool isUnassigned = true;
                if (assignments.TryGetValue(seat.Id, out var sid) &&
                    studentNames.TryGetValue(sid, out var name))
                {
                    studentName = name;
                    isUnassigned = false;
                }

                row.Cells.Add(new ExportCell
                {
                    IsSeat = true,
                    IsUnassigned = isUnassigned,
                    Text = studentName ?? texts.Unassigned
                });
            }

            while (row.Cells.Count < maxRingSeats)
                row.Cells.Add(new ExportCell { Text = "" });

            ringRowIndex[ringGroup.Key] = model.Rows.Count;
            model.Rows.Add(row);
        }

        ApplyPolarDoors(model, polarSeats, meta, layout.Obstacles, ringRowIndex, texts);

        return model;
    }

    /// <summary>
    /// 极坐标布局：门按圆心距落到最近环行，按圆心的左右侧决定边列。
    /// </summary>
    private static void ApplyPolarDoors(
        LayoutSeatingExportModel model,
        List<PolarSeat> polarSeats,
        PolarLayoutMetadata meta,
        List<Obstacle> obstacles,
        Dictionary<int, int> ringRowIndex,
        ExportTexts texts)
    {
        var doors = obstacles.Where(o => o.Type == "Door").ToList();
        if (doors.Count == 0 || polarSeats.Count == 0) return;

        var ringRadius = polarSeats
            .GroupBy(s => s.Ring)
            .ToDictionary(g => g.Key, g => g.First().Radius);

        var ordered = doors.OrderBy(d => d.Y).ThenBy(d => d.X).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var door = ordered[i];
            double doorCenterX = door.X + door.Width / 2.0;
            double doorCenterY = door.Y + door.Height / 2.0;
            double radius = Math.Sqrt(
                Math.Pow(doorCenterX - meta.OriginX, 2) +
                Math.Pow(doorCenterY - meta.OriginY, 2));

            int nearestRing = ringRadius.Keys
                .OrderBy(r => Math.Abs(ringRadius[r] - radius))
                .First();
            if (!ringRowIndex.TryGetValue(nearestRing, out var modelIndex)) continue;

            string label = DoorLabel(texts, i, ordered.Count);
            var row = model.Rows[modelIndex];
            if (doorCenterX < meta.OriginX)
                row.LeftMarginText = JoinDoorLabel(row.LeftMarginText, label, texts);
            else
                row.RightMarginText = JoinDoorLabel(row.RightMarginText, label, texts);
        }
    }

    private static LayoutSeatingExportModel BuildFreeform(
        ClassroomLayoutDefinition layout,
        Dictionary<string, string> assignments,
        Dictionary<string, string> studentNames,
        ExportTexts texts)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name, LayoutType = LayoutType.Freeform };
        var freeSeats = layout.Seats.OfType<FreeformSeat>().ToList();

        foreach (var seat in freeSeats)
        {
            string? studentName = null;
            bool isUnassigned = true;
            if (assignments.TryGetValue(seat.Id, out var sid) &&
                studentNames.TryGetValue(sid, out var name))
            {
                studentName = name;
                isUnassigned = false;
            }

            var row = new ExportRow();
            row.Cells.Add(new ExportCell { IsSeat = true, IsUnassigned = isUnassigned, Text = studentName ?? texts.Unassigned });
            model.Rows.Add(row);
        }

        foreach (var obs in layout.Obstacles)
        {
            var row = new ExportRow();
            row.Cells.Add(new ExportCell { Text = $"[{LocalizeObstacleType(obs.Type, texts)}] ({obs.X:F0}, {obs.Y:F0})" });
            model.Rows.Add(row);
        }

        return model;
    }

    private static string LocalizeObstacleType(string type, ExportTexts texts) => type switch
    {
        "Podium" => texts.Podium,
        "Door" => texts.Door,
        _ => type
    };

    private static string DoorLabel(ExportTexts texts, int index, int total)
        => total == 1 ? texts.Door : string.Format(texts.DoorNumberFormat, index + 1);

    private static string JoinDoorLabel(string? existing, string label, ExportTexts texts)
        => string.IsNullOrEmpty(existing) ? label : $"{existing}{texts.DoorSeparator}{label}";
}

public class ExportRow
{
    public List<ExportCell> Cells { get; set; } = [];

    /// <summary>左侧门标注（仅门所在行非空）。</summary>
    public string? LeftMarginText { get; set; }

    /// <summary>右侧门标注（仅门所在行非空）。</summary>
    public string? RightMarginText { get; set; }
}

public class ExportCell
{
    public string Text { get; set; } = "";
    public bool IsSeat { get; set; }
    public bool IsAisle { get; set; }
    public bool IsPodium { get; set; }
    public bool IsUnassigned { get; set; }
}

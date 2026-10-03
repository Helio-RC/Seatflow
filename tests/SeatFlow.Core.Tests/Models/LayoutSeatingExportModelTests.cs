namespace SeatFlow.Core.Tests.Models;

public class LayoutSeatingExportModelTests
{
    [Fact]
    public void FromLayout_Grid_Basic_ShouldBuildRows()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "测试网格",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata { Rows = 3, Columns = 4, HasPodium = false }
        };
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 4; c++)
                layout.Seats.Add(new GridSeat { Row = r, Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        model.LayoutType.Should().Be(LayoutType.Grid);
        model.Rows.Should().HaveCount(3);
        model.Rows[0].Cells.Should().HaveCount(4);
    }

    [Fact]
    public void FromLayout_Grid_WithPodium_ShouldAddPodiumRow()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "有讲台",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata { Rows = 2, Columns = 3, HasPodium = true }
        };
        for (int r = 1; r <= 2; r++)
            for (int c = 1; c <= 3; c++)
                layout.Seats.Add(new GridSeat { Row = r, Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        model.Rows.Should().HaveCount(3); // podium + 2 seat rows
        model.Rows[0].Cells.Any(c => c.IsPodium).Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Grid_WithAisleColumns_ShouldInsertAisleCells()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "有过道",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata
            {
                Rows = 2,
                Columns = 4,
                AisleAfterColumns = [2],
                HasPodium = false
            }
        };
        for (int r = 1; r <= 2; r++)
            for (int c = 1; c <= 4; c++)
                layout.Seats.Add(new GridSeat { Row = r, Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        // col plan: C1, C2, aisle, C3, C4 = 5 cells
        model.Rows[0].Cells.Should().HaveCount(5);
        model.Rows[0].Cells[2].IsAisle.Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Grid_WithStudentNames_ShouldUseNames()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "测试",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata { Rows = 1, Columns = 2, HasPodium = false }
        };
        var s1 = new GridSeat { Row = 1, Column = 1, Id = "s1" };
        var s2 = new GridSeat { Row = 1, Column = 2, Id = "s2" };
        layout.Seats.Add(s1);
        layout.Seats.Add(s2);

        var model = LayoutSeatingExportModel.FromLayout(layout,
            new Dictionary<string, string> { { "s1", "stu1" } },
            new Dictionary<string, string> { { "stu1", "张三" } });

        model.Rows[0].Cells[0].Text.Should().Be("张三");
        model.Rows[0].Cells[1].Text.Should().Be("未分配"); // no assignment
        model.Rows[0].Cells[1].IsUnassigned.Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Polar_ShouldBuildRingRows()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "极坐标",
            LayoutType = LayoutType.Polar,
            Metadata = new PolarLayoutMetadata
            {
                RingSeatCounts = [4, 6],
                HasPodium = false,
                RadiusStep = 40
            }
        };
        for (int r = 1; r <= 2; r++)
        {
            int count = r == 1 ? 4 : 6;
            for (int s = 0; s < count; s++)
                layout.Seats.Add(new PolarSeat { Ring = r, AngleDegrees = s * 360.0 / count });
        }

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        model.Rows.Should().HaveCount(2);
        // ring 1 has 4 seats, max is 6, so 1 padding on each side → 6 cells
        model.Rows[0].Cells.Should().HaveCount(6);
        model.Rows[0].Cells[0].Text.Should().Be("");
        model.Rows[0].Cells[1].IsSeat.Should().BeTrue();
        model.Rows[0].Cells[4].IsSeat.Should().BeTrue();
        model.Rows[0].Cells[5].Text.Should().Be("");
    }

    [Fact]
    public void FromLayout_Freeform_ShouldListPoints()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "自由点",
            LayoutType = LayoutType.Freeform
        };
        layout.Seats.Add(new FreeformSeat { X = 100, Y = 200 });
        layout.Obstacles.Add(new Obstacle { X = 300, Y = 100, Width = 60, Height = 40, Type = "Podium" });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        model.Rows.Should().HaveCount(2); // seat + podium
        model.Rows[0].Cells[0].IsSeat.Should().BeTrue();
        model.Rows[1].Cells[0].Text.Should().Contain("讲台");
    }

    [Fact]
    public void FromLayout_Grid_DefaultPerspective_PodiumFirst()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "默认视角",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata { Rows = 3, Columns = 2, HasPodium = true }
        };
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 2; c++)
                layout.Seats.Add(new GridSeat { Row = r, Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        // 学生视角（默认）：讲台在第一行
        model.Rows[0].Cells.Any(c => c.IsPodium).Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Grid_TeacherView_PodiumLast()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "教师视角",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata { Rows = 3, Columns = 2, HasPodium = true }
        };
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 2; c++)
                layout.Seats.Add(new GridSeat { Row = r, Column = c });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);
        // 教师视角：行前后反转 + 列左右镜像
        model.ApplyPerspective(LayoutPerspective.TeacherView);

        // 行反转：讲台在最后一行
        model.Rows[^1].Cells.Any(c => c.IsPodium).Should().BeTrue();
        // 列镜像：讲台行 cells 左右颠倒，讲台应移至最左侧
        model.Rows[^1].Cells[0].IsPodium.Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Polar_TeacherView_PodiumLast()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "极坐标教师视角",
            LayoutType = LayoutType.Polar,
            Metadata = new PolarLayoutMetadata
            {
                RingSeatCounts = [4, 6],
                HasPodium = true,
                PodiumRadius = 20,
                RadiusStep = 40
            }
        };
        for (int r = 1; r <= 2; r++)
        {
            int count = r == 1 ? 4 : 6;
            for (int s = 0; s < count; s++)
                layout.Seats.Add(new PolarSeat { Ring = r, AngleDegrees = s * 360.0 / count });
        }

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);
        // 教师视角：行前后反转 + 列左右镜像
        model.ApplyPerspective(LayoutPerspective.TeacherView);

        // 教师视角：讲台在最后一行（Polar 讲台居中有填充，不做列级断言）
        model.Rows[^1].Cells.Any(c => c.IsPodium).Should().BeTrue();
    }

    [Fact]
    public void FromLayout_Grid_WithDoor_ShouldPlaceLeftMarginAtNearestRow()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "有门网格",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata
            {
                Rows = 3,
                Columns = 2,
                HasPodium = false,
                OriginX = 200,
                OriginY = 100,
                VerticalSpacing = 40
            }
        };
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 2; c++)
                layout.Seats.Add(new GridSeat { Row = r, Column = c });

        // 门位于网格左侧、第 2 行高度（中心 Y = 142，最近行中心 160）
        layout.Obstacles.Add(new Obstacle { X = 100, Y = 130, Width = 36, Height = 24, Type = "Door" });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        model.HasLeftMargin.Should().BeTrue();
        model.HasRightMargin.Should().BeFalse();
        model.Rows[0].LeftMarginText.Should().BeNull();
        model.Rows[1].LeftMarginText.Should().Be("门");
        model.Rows[2].LeftMarginText.Should().BeNull();
    }

    [Fact]
    public void FromLayout_Grid_WithTwoDoors_ShouldNumberAndSplitSides()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "双门网格",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata
            {
                Rows = 3,
                Columns = 2,
                HasPodium = false,
                OriginX = 200,
                OriginY = 100,
                VerticalSpacing = 40
            }
        };
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 2; c++)
                layout.Seats.Add(new GridSeat { Row = r, Column = c });

        // 门 1：左侧第一行；门 2：右侧第三行
        layout.Obstacles.Add(new Obstacle { X = 100, Y = 110, Width = 36, Height = 24, Type = "Door" });
        layout.Obstacles.Add(new Obstacle { X = 260, Y = 265, Width = 36, Height = 24, Type = "Door" });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        model.HasLeftMargin.Should().BeTrue();
        model.HasRightMargin.Should().BeTrue();
        model.Rows[0].LeftMarginText.Should().Be("门 #1");
        model.Rows[2].RightMarginText.Should().Be("门 #2");
    }

    [Fact]
    public void ApplyPerspective_TeacherView_ShouldMoveDoorToOppositeSide()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "门视角",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata
            {
                Rows = 2,
                Columns = 2,
                HasPodium = false,
                OriginX = 200,
                OriginY = 100,
                VerticalSpacing = 40
            }
        };
        for (int r = 1; r <= 2; r++)
            for (int c = 1; c <= 2; c++)
                layout.Seats.Add(new GridSeat { Row = r, Column = c });

        layout.Obstacles.Add(new Obstacle { X = 100, Y = 102, Width = 36, Height = 24, Type = "Door" });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);
        model.Rows[0].LeftMarginText.Should().Be("门");

        model.ApplyPerspective(LayoutPerspective.TeacherView);

        // 行反转后，原第 1 行变为最后一行；门由左换到右
        model.Rows[^1].RightMarginText.Should().Be("门");
        model.Rows.Should().OnlyContain(r => r.LeftMarginText == null);
    }

    [Fact]
    public void FromLayout_Polar_WithDoors_ShouldPlaceMarginByRadius()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "极坐标门",
            LayoutType = LayoutType.Polar,
            Metadata = new PolarLayoutMetadata
            {
                RingSeatCounts = [8, 12],
                HasPodium = false,
                OriginX = 300,
                OriginY = 200
            }
        };
        for (int s = 0; s < 8; s++)
            layout.Seats.Add(new PolarSeat { Ring = 1, Radius = 100, AngleDegrees = s * 45.0 });
        for (int s = 0; s < 12; s++)
            layout.Seats.Add(new PolarSeat { Ring = 2, Radius = 200, AngleDegrees = s * 30.0 });

        // 门 1：右侧、半径接近内环；门 2：右侧、半径接近外环
        layout.Obstacles.Add(new Obstacle { X = 402, Y = 188, Width = 36, Height = 24, Type = "Door" });
        layout.Obstacles.Add(new Obstacle { X = 482, Y = 188, Width = 36, Height = 24, Type = "Door" });

        var model = LayoutSeatingExportModel.FromLayout(layout, [], []);

        model.Rows.Should().HaveCount(2); // 无讲台：内环 + 外环
        model.Rows[0].RightMarginText.Should().Be("门 #1");
        model.Rows[1].RightMarginText.Should().Be("门 #2");
        model.HasLeftMargin.Should().BeFalse();
    }

    [Fact]
    public void FromLayout_WithCustomTexts_ShouldUseProvidedLabels()
    {
        var layout = new ClassroomLayoutDefinition
        {
            Name = "文案",
            LayoutType = LayoutType.Grid,
            Metadata = new GridLayoutMetadata { Rows = 1, Columns = 2, HasPodium = true }
        };
        layout.Seats.Add(new GridSeat { Row = 1, Column = 1 });
        layout.Seats.Add(new GridSeat { Row = 1, Column = 2 });

        var texts = new ExportTexts { SeatingChart = "Chart", Unassigned = "Empty", Podium = "Stage" };
        var model = LayoutSeatingExportModel.FromLayout(layout, [], [], texts);

        model.Rows[0].Cells.Any(c => c.Text == "Stage").Should().BeTrue();
        model.Rows[1].Cells.Should().OnlyContain(c => c.Text == "Empty");
    }
}

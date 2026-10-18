using System.Collections.ObjectModel;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Controls;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>旧 IGuideSeedTarget 移除后，视觉/布局测试使用的直接数据夹具。</summary>
internal static class TestSeedFixtures
{
    public static void SeedSeating(SeatingArrangementViewModel vm)
    {
        vm.VenueItems.Clear();
        vm.VenueItems.Add(new VenueItem("demo-v", "演示教室"));
        vm.DatasetItems.Clear();
        vm.DatasetItems.Add(new StudentDatasetInfo { Id = "demo-ds", Name = "演示班级", StudentCount = 6 });
        vm.SelectedVenue = vm.VenueItems[0];
        vm.SelectedDataset = vm.DatasetItems[0];

        var names = new[] { "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank" };
        var seats = new ObservableCollection<SeatDisplayItem>();
        var visuals = new List<SeatVisual>();
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                int idx = (r * 3) + c;
                double x = 200 + (c * 80);
                double y = 200 + (r * 60);
                seats.Add(new SeatDisplayItem
                {
                    SeatId = $"R{r}C{c}",
                    SeatLabel = $"R{r}C{c}",
                    X = x,
                    Y = y,
                    Width = 50,
                    Height = 30,
                    IsOccupied = idx < 6,
                    StudentName = idx < 6 ? names[idx] : null,
                    OccupancyStatus = idx < 6 ? SeatOccupancyStatus.Occupied : SeatOccupancyStatus.Empty,
                });
                visuals.Add(new SeatVisual(
                    $"R{r}C{c}", x, y, 50, 30,
                    IsOccupied: idx < 6,
                    Label: idx < 6 ? names[idx] : null,
                    SeatLabel: $"R{r}C{c}",
                    Row: r + 1,
                    Column: c + 1));
            }
        }

        vm.SeatItems = seats;
        vm.OverlayItems = [];
        vm.CanvasSnapshot = new SeatLayoutSnapshot(visuals, 800, 600);
        vm.TotalSeats = 12;
        vm.AssignedSeats = 6;
        vm.HasGenerated = true;
        vm.StatusMessage = "已分配 6/12 个座位";
    }

    public static void SeedMember(MemberManagementViewModel vm)
    {
        var students = new List<Student>
        {
            new() { Name = "Alice", Height = 165, Gender = Gender.Female },
            new() { Name = "Bob", Height = 175, Gender = Gender.Male, NeedsFrontRow = true },
            new() { Name = "Charlie", Height = 180, Gender = Gender.Male },
            new() { Name = "Diana", Height = 160, Gender = Gender.Female },
            new() { Name = "Eve", Height = 170, Gender = Gender.Female },
            new() { Name = "Frank", Height = 178, Gender = Gender.Male },
        };

        vm.Students = new ObservableCollection<StudentRowViewModel>(
            students.Select(s => new StudentRowViewModel(s)));
        vm.StudentCount = students.Count;
        vm.IsEmpty = false;

        var dataset = new StudentDatasetInfo
        {
            Id = "demo-ds",
            Name = "演示班级",
            StudentCount = students.Count,
            CreatedAt = DateTime.Now,
        };
        vm.SavedDatasets = new ObservableCollection<StudentDatasetInfo>([dataset]);
        vm.CurrentDatasetId = dataset.Id;
        vm.CurrentDatasetName = dataset.Name;
        vm.StatusMessage = $"已加载 {students.Count} 名学生";
    }
}

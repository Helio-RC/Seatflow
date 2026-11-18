using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FluentAssertions;
using NSubstitute;
using SeatFlow.Application.Commands;
using SeatFlow.Application.Interfaces;
using SeatFlow.Application.Services;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Models;
using SeatFlow.Core.Workspace;
using SeatFlow.Presentation.Avalonia.Services;
using SeatFlow.Presentation.Avalonia.ViewModels;
using SeatFlow.Presentation.Avalonia.Views;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 触摸拖动未分配学生（右侧姓名列表 → 座位画布）：触摸没有系统 DnD，
/// 视图自己捕获指针、跨控件跟踪手指，落到座位上时派发落座。
/// </summary>
public class SeatingArrangementViewTouchDragTests
{
    private static SeatingArrangementViewModel CreateVm(IApplicationFacade facade) => new(
        facade,
        Substitute.For<IFileService>(),
        Substitute.For<IArrangementCounterService>(),
        new ShellLayoutService(),
        new WelcomeCardViewModel(NullDialogService.Instance),
        Substitute.For<IServiceProvider>(),
        NullDialogService.Instance);

    /// <summary>真实的 SeatingWorkspace + facade，让落座命令真正改变工作区。</summary>
    private static (IApplicationFacade Facade, List<Student> Students) CreateFacade()
    {
        var students = new List<Student>
        {
            new() { Id = "stu-1", Name = "未分配甲" },
            new() { Id = "stu-2", Name = "未分配乙" },
        };
        var seats = new List<Seat>();
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                seats.Add(new GridSeat { Id = $"R{r}C{c}", Row = r + 1, Column = c + 1 });
            }
        }

        var workspace = new SeatingWorkspace(students, seats);
        var facade = Substitute.For<IApplicationFacade>();
        facade.GetCurrentWorkspaceAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SeatingWorkspace?>(workspace));
        facade.GetStrategiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<StrategyDisplayInfo>()));
        facade.ExecuteCommandAsync(Arg.Any<SeatFlow.Application.Commands.IUndoableCommand>(),
                Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(async call =>
            {
                var cmd = (SeatFlow.Application.Commands.IUndoableCommand)call[0];
                return await cmd.ExecuteAsync(workspace, (CancellationToken)call[1]);
            });
        return (facade, students);
    }

    private static (Window Window, SeatingArrangementView View, SeatingArrangementViewModel Vm, List<Student> Students) CreatePage()
    {
        var (facade, students) = CreateFacade();
        var vm = CreateVm(facade);
        var view = new SeatingArrangementView { DataContext = vm };
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();
        TestSeedFixtures.SeedSeating(vm);
        vm.UnassignedStudents = new System.Collections.ObjectModel.ObservableCollection<Student>(students);
        vm.SelectedUnassignedStudent = null;

        // 未分配列表在检查器的「未分配」页签里：先切到该页签，列表才会生成行容器
        var tabs = view.GetVisualDescendants()
            .OfType<TabControl>()
            .FirstOrDefault(t => t.Items.OfType<TabItem>()
                .Any(i => (i.Header as Control)?.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault()?.Text?.Contains("未分配", StringComparison.Ordinal) == true))
            ?? throw new InvalidOperationException("未找到检查器页签");
        tabs.SelectedIndex = tabs.Items.OfType<TabItem>()
            .Select((item, index) => (item, index))
            .First(x => (x.item.Header as Control)?.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault()?.Text?.Contains("未分配", StringComparison.Ordinal) == true).index;
        window.UpdateLayout();
        window.UpdateLayout();
        return (window, view, vm, students);
    }

    private static Border FindStudentRow(SeatingArrangementView view, string name)
        => view.GetVisualDescendants()
            .OfType<Border>()
            .First(b => b.DataContext is Student s && s.Name == name);

    /// <summary>把画布内某个座位中心换算成窗口坐标（按画布快照里的实际座位矩形）。</summary>
    private static Point SeatPointInWindow(SeatingArrangementView view, string seatId)
    {
        var canvas = view.FindControl<SeatFlow.Presentation.Avalonia.Controls.SeatingCanvas>("SeatCanvas")!;
        var seat = canvas.Snapshot!.Seats.First(s => s.Id == seatId);
        return SeatInCanvasToWindow(canvas, view, new Point(seat.X + (seat.Width / 2), seat.Y + (seat.Height / 2)));
    }

    /// <summary>板面坐标 → 窗口坐标（先按 ToBoard 的逆变换落到画布坐标，再换到窗口）。</summary>
    private static Point SeatInCanvasToWindow(SeatFlow.Presentation.Avalonia.Controls.SeatingCanvas canvas,
        Visual view, Point inBoard)
    {
        var (offsetX, offsetY) = ContentOrigin(canvas);
        var inCanvas = new Point(offsetX + (inBoard.X * canvas.Zoom), offsetY + (inBoard.Y * canvas.Zoom));
        var canvasOrigin = canvas.TranslatePoint(default, view) ?? default;
        return new Point(inCanvas.X + canvasOrigin.X, inCanvas.Y + canvasOrigin.Y);
    }

    /// <summary>板面原点在画布坐标系中的位置（与 SeatingCanvas.ToBoard 同一公式）。</summary>
    private static (double X, double Y) ContentOrigin(SeatFlow.Presentation.Avalonia.Controls.SeatingCanvas canvas)
    {
        var snapshot = canvas.Snapshot!;
        var zoom = canvas.Zoom;
        if (snapshot.BoardHeight <= 0 || snapshot.BoardWidth <= 0) return (0, 0);
        var contentWidth = snapshot.BoardWidth * zoom;
        var contentHeight = snapshot.BoardHeight * zoom;
        const double margin = 24;
        var x = contentWidth + (margin * 2) < canvas.Bounds.Width
            ? (canvas.Bounds.Width - contentWidth) / 2
            : margin;
        var y = contentHeight + (margin * 2) < canvas.Bounds.Height
            ? (canvas.Bounds.Height - contentHeight) / 2
            : margin;
        return (x + canvas.PanOffset.X, y + canvas.PanOffset.Y);
    }

    /// <summary>在画布上找一个不绘制任何座位的空白点（避开座位与覆盖物）。</summary>
    private static Point BlankPointInWindow(SeatingArrangementView view)
    {
        var canvas = view.FindControl<SeatFlow.Presentation.Avalonia.Controls.SeatingCanvas>("SeatCanvas")!;
        var snapshot = canvas.Snapshot!;
        var zoom = canvas.Zoom;
        var (offsetX, offsetY) = ContentOrigin(canvas);
        for (var y = 8.0; y < canvas.Bounds.Height - 8; y += 16)
        {
            for (var x = 8.0; x < canvas.Bounds.Width - 8; x += 16)
            {
                var board = new Point((x - offsetX) / zoom, (y - offsetY) / zoom);
                if (canvas.HitTestSeatAt(new Point(x, y)) is not null) continue;
                if (snapshot.Overlays is { } overlays && overlays.Any(o =>
                        board.X >= o.X && board.X <= o.X + o.Width
                        && board.Y >= o.Y && board.Y <= o.Y + o.Height))
                {
                    continue;
                }

                var canvasOrigin = canvas.TranslatePoint(default, view) ?? default;
                return new Point(x + canvasOrigin.X, y + canvasOrigin.Y);
            }
        }

        throw new InvalidOperationException("画布上找不到空白点");
    }

    [AvaloniaFact]
    public void 触摸拖动未分配学生到空座位_应落座()
    {
        var (window, view, vm, _) = CreatePage();
        var row = FindStudentRow(view, "未分配甲");
        var start = row.TranslatePoint(new Point(20, row.Bounds.Height / 2), view)!.Value;
        var target = SeatPointInWindow(view, "R3C0");

        var touch = window.TouchBegin(start);
        window.TouchMove(touch, new Point(start.X, start.Y + 12));
        window.TouchMove(touch, new Point((start.X + target.X) / 2, (start.Y + target.Y) / 2));
        window.TouchMove(touch, target);
        window.TouchEnd(touch, target);
        window.UpdateLayout();
        for (var i = 0; i < 20; i++)
        {
            window.UpdateLayout();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (!vm.UnassignedStudents.Any(s => s.Id == "stu-1")) break;
            System.Threading.Thread.Sleep(20);
        }

        view.TouchDragEventCountForTest.Should().BeGreaterThan(0, "触摸移动应被视图收到");
        vm.UnassignedStudents.Select(s => s.Id).Should().NotContain("stu-1",
            "拖到空座位(" + target + ")后该学生应离开未分配列表");
        vm.StatusMessage.Should().NotBeNullOrEmpty("落座后应更新状态栏");
        window.Close();
    }

    [AvaloniaFact]
    public void 触摸轻点未分配学生_不落座但保持选中()
    {
        var (window, view, vm, _) = CreatePage();
        var row = FindStudentRow(view, "未分配乙");
        var point = row.TranslatePoint(new Point(20, row.Bounds.Height / 2), view)!.Value;

        var touch = window.TouchBegin(point);
        window.TouchEnd(touch, point);
        window.UpdateLayout();

        vm.SelectedUnassignedStudent?.Id.Should().Be("stu-2", "轻点应选中该学生，供随后点座位放置");
        vm.UnassignedStudents.Select(s => s.Id).Should().Contain("stu-2");
        window.Close();
    }

    [AvaloniaFact]
    public void 触摸拖动未分配学生到画布空白处_取消拖拽且不落座()
    {
        var (window, view, vm, _) = CreatePage();
        var row = FindStudentRow(view, "未分配甲");
        var start = row.TranslatePoint(new Point(20, row.Bounds.Height / 2), view)!.Value;
        var blank = BlankPointInWindow(view);

        var touch = window.TouchBegin(start);
        window.TouchMove(touch, new Point(start.X + 10, start.Y - 10));
        window.TouchMove(touch, blank);
        window.TouchEnd(touch, blank);
        window.UpdateLayout();

        vm.UnassignedStudents.Select(s => s.Id).Should().Contain("stu-1", "空白处松开不落座");
        view.FindControl<SeatFlow.Presentation.Avalonia.Controls.SeatingCanvas>("SeatCanvas")!
            .DragGhostVisibleForTest.Should().BeFalse("拖拽结束后幽灵标签必须清除");
        window.Close();
    }
}

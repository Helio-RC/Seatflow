using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using SeatFlow.Presentation.Avalonia.Controls;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 自绘画布 <see cref="SeatingCanvas"/> 的几何命中与键盘虚拟焦点测试（M6）：
/// 命中为纯几何计算，键盘导航跳过禁用座位并提供可访问性补偿。
/// </summary>
public class SeatingCanvasTests
{
    private static SeatLayoutSnapshot CreateSnapshot() => new(
        [
            new SeatVisual("s1", 0, 0, 50, 30, SeatLabel: "R1C1"),
            new SeatVisual("s2", 100, 0, 50, 30, SeatLabel: "R1C2"),
            new SeatVisual("s3", 0, 60, 50, 30, SeatLabel: "R2C1"),
        ],
        200,
        120);

    private static (SeatingCanvas Canvas, Window Window) CreateCanvasWindow(SeatLayoutSnapshot? snapshot)
    {
        var canvas = new SeatingCanvas { Snapshot = snapshot };
        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.Show();
        window.UpdateLayout();
        canvas.Zoom = 1.0;
        canvas.PanOffset = default;
        return (canvas, window);
    }

    /// <summary>按画布当前的居中规则反算板面坐标对应的屏幕坐标（Zoom=1）。</summary>
    private static Point ToScreen(SeatingCanvas canvas, Point board)
    {
        var zoom = canvas.Zoom;
        var snapshot = canvas.Snapshot!;
        var contentWidth = snapshot.BoardWidth * zoom;
        var contentHeight = snapshot.BoardHeight * zoom;
        var offsetX = contentWidth + (SeatingCanvas.BoardMargin * 2) < canvas.Bounds.Width
            ? (canvas.Bounds.Width - contentWidth) / 2
            : SeatingCanvas.BoardMargin;
        var offsetY = contentHeight + (SeatingCanvas.BoardMargin * 2) < canvas.Bounds.Height
            ? (canvas.Bounds.Height - contentHeight) / 2
            : SeatingCanvas.BoardMargin;
        return new Point(offsetX + (board.X * zoom), offsetY + (board.Y * zoom));
    }

    [AvaloniaFact]
    public void 命中测试_板面坐标映射与未命中()
    {
        var (canvas, _) = CreateCanvasWindow(CreateSnapshot());

        canvas.HitTestSeat(ToScreen(canvas, new Point(10, 10)))!.Id.Should().Be("s1");
        canvas.HitTestSeat(ToScreen(canvas, new Point(120, 10)))!.Id.Should().Be("s2");
        canvas.HitTestSeat(ToScreen(canvas, new Point(10, 70)))!.Id.Should().Be("s3");

        // 座位之间的空白 / 板面外
        canvas.HitTestSeat(ToScreen(canvas, new Point(70, 10))).Should().BeNull();
        canvas.HitTestSeat(ToScreen(canvas, new Point(300, 110))).Should().BeNull();
    }

    [AvaloniaFact]
    public void 快照为空_命中返回_null_渲染不崩溃()
    {
        var (canvas, window) = CreateCanvasWindow(null);

        canvas.HitTestSeat(new Point(50, 50)).Should().BeNull();
        window.CaptureRenderedFrame().Should().NotBeNull();
    }

    [AvaloniaFact]
    public void 键盘_无选中时按方向键_选中首个可用座位()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        canvas.Focus();

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);

        canvas.SelectedSeatId.Should().Be("s1");
    }

    [AvaloniaFact]
    public void 键盘_方向键按几何邻接移动()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        canvas.Focus();
        canvas.SelectedSeatId = "s2";

        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);

        canvas.SelectedSeatId.Should().Be("s1");
    }

    [AvaloniaFact]
    public void 键盘_跳过禁用座位()
    {
        var snapshot = new SeatLayoutSnapshot(
        [
            new SeatVisual("disabled", 0, 0, 50, 30, IsDisabled: true),
            new SeatVisual("enabled", 100, 0, 50, 30),
        ], 200, 120);
        var (canvas, window) = CreateCanvasWindow(snapshot);
        canvas.Focus();

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);

        canvas.SelectedSeatId.Should().Be("enabled");
    }

    [AvaloniaFact]
    public void 键盘_Enter激活选中座位()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        canvas.Focus();
        canvas.SelectedSeatId = "s1";

        SeatEventArgs? activated = null;
        canvas.SeatActivated += (_, e) => activated = e;

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        activated.Should().NotBeNull();
        activated!.SeatId.Should().Be("s1");
        activated.Seat.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void 空座位列表_键盘操作无副作用()
    {
        var (canvas, window) = CreateCanvasWindow(SeatLayoutSnapshot.Empty);
        canvas.Focus();

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);

        canvas.SelectedSeatId.Should().BeNull();
    }
}

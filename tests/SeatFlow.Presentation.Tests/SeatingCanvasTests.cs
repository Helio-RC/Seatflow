using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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

    [AvaloniaFact]
    public void 关闭自动适配_快照更新后保持显式缩放()
    {
        // 工作台场景：AutoFit=false，由设置「座位图默认缩放」驱动
        var canvas = new SeatingCanvas { AutoFit = false, Zoom = 0.75 };
        var window = new Window { Width = 800, Height = 600, Content = canvas };
        window.Show();
        window.UpdateLayout();

        canvas.Snapshot = new SeatLayoutSnapshot([new SeatVisual("s1", 0, 0, 50, 30)], 3000, 2000);
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        canvas.Zoom.Should().BeApproximately(0.75, 0.001, "AutoFit=false 时不应被适配为其他缩放");
    }

    [AvaloniaFact]
    public async Task 开启自动适配_大板面适配视口()
    {
        // 预览场景：AutoFit 默认 true，大板面自动缩小以全览
        var canvas = new SeatingCanvas();
        var window = new Window { Width = 800, Height = 600, Content = canvas };
        window.Show();
        window.UpdateLayout();

        canvas.Snapshot = new SeatLayoutSnapshot([new SeatVisual("s1", 0, 0, 50, 30)], 3000, 2000);
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        // 适配通过 Dispatcher.Background 调度执行
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        canvas.Zoom.Should().BeLessThan(1.0, "大板面应自动缩小适配视口");
    }

    private static SeatLayoutSnapshot CreateDisabledSnapshot() => new(
        [
            new SeatVisual("disabled", 0, 0, 50, 30, IsDisabled: true, SeatLabel: "R1C1", Row: 1, Column: 1),
            new SeatVisual("enabled", 100, 0, 50, 30, Row: 1, Column: 2),
        ],
        200,
        120);

    [AvaloniaFact]
    public void 拾取模式_点击禁用座位_触发点击且不拖拽()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());
        canvas.IsSeatPicking = true;

        SeatEventArgs? clicked = null;
        var dropped = false;
        canvas.SeatClicked += (_, e) => clicked = e;
        canvas.SeatDropped += (_, _) => dropped = true;

        var point = ToScreen(canvas, new Point(10, 10));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        clicked.Should().NotBeNull();
        clicked!.SeatId.Should().Be("disabled");
        clicked.Seat!.Row.Should().Be(1);
        clicked.Seat!.Column.Should().Be(1);
        dropped.Should().BeFalse("拾取模式不应进入拖拽");
    }

    [AvaloniaFact]
    public void 拾取模式_按下后拖动超阈值_不触发点击也不拖拽()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());
        canvas.IsSeatPicking = true;

        SeatEventArgs? clicked = null;
        var dropped = false;
        canvas.SeatClicked += (_, e) => clicked = e;
        canvas.SeatDropped += (_, _) => dropped = true;

        var start = ToScreen(canvas, new Point(10, 10));
        var end = ToScreen(canvas, new Point(40, 10));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);

        clicked.Should().BeNull("拾取模式下超阈值拖动视为误触");
        dropped.Should().BeFalse("拾取模式不应进入拖拽");
    }

    [AvaloniaFact]
    public void 非拾取模式_拖动普通座位_触发拖拽放下()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());

        SeatDropEventArgs? dropped = null;
        SeatEventArgs? clicked = null;
        canvas.SeatDropped += (_, e) => dropped = e;
        canvas.SeatClicked += (_, e) => clicked = e;

        var start = ToScreen(canvas, new Point(110, 10));
        var end = ToScreen(canvas, new Point(160, 10));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);

        dropped.Should().NotBeNull("非拾取模式仍应进入拖拽");
        dropped!.SourceSeatId.Should().Be("enabled");
        clicked.Should().BeNull();
    }

    [AvaloniaFact]
    public void 非拾取模式_点击禁用座位_不触发点击()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());

        SeatEventArgs? clicked = null;
        canvas.SeatClicked += (_, e) => clicked = e;

        var point = ToScreen(canvas, new Point(10, 10));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        clicked.Should().BeNull("禁用座位仅在拾取模式下可点击");
    }

    [AvaloniaFact]
    public void 拾取模式_普通座位_点击不触发拖拽()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());
        canvas.IsSeatPicking = true;

        SeatEventArgs? clicked = null;
        var dropped = false;
        canvas.SeatClicked += (_, e) => clicked = e;
        canvas.SeatDropped += (_, _) => dropped = true;

        var start = ToScreen(canvas, new Point(110, 10));
        var end = ToScreen(canvas, new Point(112, 10));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);

        clicked.Should().NotBeNull();
        clicked!.SeatId.Should().Be("enabled");
        dropped.Should().BeFalse("拾取模式下普通座位也不进入拖拽");
    }

    // ── 触摸交互（触屏上 IsLeftButtonPressed 恒为 false，必须按 PointerType 放行） ──

    [AvaloniaFact]
    public void 触摸_点击座位_触发点击()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());

        SeatEventArgs? clicked = null;
        canvas.SeatClicked += (_, e) => clicked = e;

        var point = ToScreen(canvas, new Point(110, 10));
        var touch = window.TouchBegin(point);
        window.TouchEnd(touch, point);

        clicked.Should().NotBeNull("触摸点击座位必须可用");
        clicked!.SeatId.Should().Be("enabled");
    }

    [AvaloniaFact]
    public void 触摸_拖动座位_触发拖拽放下()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());

        SeatDropEventArgs? dropped = null;
        canvas.SeatDropped += (_, e) => dropped = e;

        var start = ToScreen(canvas, new Point(110, 10));
        var end = ToScreen(canvas, new Point(160, 10));
        var touch = window.TouchBegin(start);
        window.TouchMove(touch, end);
        window.TouchEnd(touch, end);

        dropped.Should().NotBeNull("触摸拖动座位必须能进入拖拽");
        dropped!.SourceSeatId.Should().Be("enabled");
    }

    [AvaloniaFact]
    public void 触摸_拖动中途捕获丢失_下一次点击仍可响应()
    {
        // 假死回归：指针被系统吞掉（没有 Released）后，画布必须能自行恢复，
        // 否则会永久停在拖动态，下一次点击毫无反应。
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());

        var clicks = 0;
        canvas.SeatClicked += (_, _) => clicks++;

        var start = ToScreen(canvas, new Point(110, 10));
        var end = ToScreen(canvas, new Point(160, 10));
        var touch = window.TouchBegin(start);
        window.TouchMove(touch, end);
        // 直接结束该指针（触发 CaptureLost / 丢失 Released 场景）
        window.TouchEnd(touch, end);

        var second = ToScreen(canvas, new Point(110, 10));
        var touch2 = window.TouchBegin(second);
        window.TouchEnd(touch2, second);

        clicks.Should().Be(1, "上一次手势被中断后，新的触摸点击必须正常响应");
        canvas.SelectedSeatId.Should().Be("enabled");
    }

    [AvaloniaFact]
    public void 触摸_空白处拖动_平移板面()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        var before = canvas.PanOffset;

        var start = ToScreen(canvas, new Point(70, 100));
        var end = new Point(start.X - 30, start.Y - 20);
        var touch = window.TouchBegin(start);
        window.TouchMove(touch, end);
        window.TouchEnd(touch, end);

        canvas.PanOffset.Should().NotBe(before, "触摸拖动空白处应平移板面");
    }

    [AvaloniaFact]
    public void 鼠标_点击座位_按下到抬起之间丢失捕获_仍应触发点击()
    {
        // 回归：未分配学生列表在 PointerPressed 里启动系统拖放（DoDragDropAsync），
        // 抬手时它会抢走 / 释放指针捕获；若捕获丢失就取消手势，点击会被吞掉，
        // 表现为「点学生再点座位毫无反应」。
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());

        var clicks = 0;
        canvas.SeatClicked += (_, _) => clicks++;

        var point = ToScreen(canvas, new Point(110, 10));
        window.MouseDown(point, MouseButton.Left);
        canvas.RaiseEvent(new PointerCaptureLostEventArgs(canvas, new Pointer(0, PointerType.Mouse, true)));
        window.MouseUp(point, MouseButton.Left);

        clicks.Should().Be(1, "捕获丢失发生在按下期间、随后正常抬起时，点击不应被吞");
    }

    [AvaloniaFact]
    public void 触摸_点击座位_按下到抬起之间丢失捕获_仍应触发点击()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());

        var clicks = 0;
        canvas.SeatClicked += (_, _) => clicks++;

        var point = ToScreen(canvas, new Point(110, 10));
        var touch = window.TouchBegin(point);
        canvas.RaiseEvent(new PointerCaptureLostEventArgs(
            canvas, new Pointer(1, PointerType.Touch, true)));
        window.TouchEnd(touch, point);

        clicks.Should().Be(1, "触摸点击在捕获丢失后仍应可用");
    }

    // ── 双指缩放（触摸设备没有滚轮，捏合是唯一缩放入口） ──

    [AvaloniaFact]
    public void 双指捏合张开_放大画布()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        var zoomBefore = canvas.Zoom;

        var center = ToScreen(canvas, new Point(100, 60));
        var fingerA = window.TouchBegin(new Point(center.X - 20, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 20, center.Y));
        window.TouchMove(fingerA, new Point(center.X - 60, center.Y));
        window.TouchMove(fingerB, new Point(center.X + 60, center.Y));
        window.TouchEnd(fingerA, new Point(center.X - 60, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 60, center.Y));

        canvas.Zoom.Should().BeGreaterThan(zoomBefore, "张开两指必须放大画布");
    }

    [AvaloniaFact]
    public void 双指捏合收拢_缩小画布()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        canvas.Zoom = 2.0;
        var zoomBefore = canvas.Zoom;

        var center = ToScreen(canvas, new Point(60, 40));
        var fingerA = window.TouchBegin(new Point(center.X - 80, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 80, center.Y));
        window.TouchMove(fingerA, new Point(center.X - 30, center.Y));
        window.TouchMove(fingerB, new Point(center.X + 30, center.Y));
        window.TouchEnd(fingerA, new Point(center.X - 30, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 30, center.Y));

        canvas.Zoom.Should().BeLessThan(zoomBefore, "收拢两指必须缩小画布");
    }

    [AvaloniaFact]
    public void 双指缩放_不触发座位点击也不拖拽()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());

        var clicks = 0;
        var drops = 0;
        canvas.SeatClicked += (_, _) => clicks++;
        canvas.SeatDropped += (_, _) => drops++;

        // 第一根手指落在座位上（单指会进入拖拽判定），第二根手指出现后必须转成缩放
        var seat = ToScreen(canvas, new Point(10, 10));
        var far = ToScreen(canvas, new Point(120, 10));
        var fingerA = window.TouchBegin(seat);
        var fingerB = window.TouchBegin(far);
        window.TouchMove(fingerA, new Point(seat.X - 30, seat.Y - 30));
        window.TouchEnd(fingerA, new Point(seat.X - 30, seat.Y - 30));
        window.TouchEnd(fingerB, far);

        clicks.Should().Be(0, "双指手势不应当作点击");
        drops.Should().Be(0, "双指手势不应当作拖动座位");
    }

    [AvaloniaFact]
    public void 双指缩放结束后_单指点击仍然可用()
    {
        var (canvas, window) = CreateCanvasWindow(CreateDisabledSnapshot());

        var clicks = 0;
        canvas.SeatClicked += (_, _) => clicks++;

        var center = ToScreen(canvas, new Point(100, 60));
        var fingerA = window.TouchBegin(new Point(center.X - 40, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 40, center.Y));
        window.TouchEnd(fingerA, new Point(center.X - 40, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 40, center.Y));

        clicks.Should().Be(0);

        // 双指手势后画布必须回到干净的待命状态：单指点击照常生效
        var seat = ToScreen(canvas, new Point(110, 10));
        var fingerC = window.TouchBegin(seat);
        window.TouchEnd(fingerC, seat);

        clicks.Should().Be(1, "双指手势结束后单指点击必须恢复");
    }

    // ── 双指缩放稳定性（回归：缩放时会场抽搐、甚至缩到看不见） ──
    // 根因：两指共用了一个位置字段，任一根手指移动都会改写另一根的位置，
    // 两指间距瞬间归零 → 缩放掉到最小值再弹回。

    [AvaloniaFact]
    public void 双指移动第一根手指_缩放按两指间距精确变化()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        var center = ToScreen(canvas, new Point(100, 60));

        var fingerA = window.TouchBegin(new Point(center.X - 20, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 20, center.Y));

        // 只动第一根手指：两指间距 40 → 80，缩放应精确翻倍
        window.TouchMove(fingerA, new Point(center.X - 60, center.Y));

        canvas.Zoom.Should().BeApproximately(2.0, 0.05, "间距翻倍时缩放应翻倍，不能塌到最小缩放");

        window.TouchEnd(fingerA, new Point(center.X - 60, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 20, center.Y));
    }

    [AvaloniaFact]
    public void 双指交替张开_缩放单调增大不抖动()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        var center = ToScreen(canvas, new Point(100, 60));

        var fingerA = window.TouchBegin(new Point(center.X - 20, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 20, center.Y));

        var zooms = new List<double> { canvas.Zoom };
        for (var step = 1; step <= 3; step++)
        {
            window.TouchMove(fingerA, new Point(center.X - 20 - (step * 20), center.Y));
            zooms.Add(canvas.Zoom);
            window.TouchMove(fingerB, new Point(center.X + 20 + (step * 20), center.Y));
            zooms.Add(canvas.Zoom);
        }

        window.TouchEnd(fingerA, new Point(center.X - 80, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 80, center.Y));

        for (var i = 1; i < zooms.Count; i++)
        {
            zooms[i].Should().BeGreaterThanOrEqualTo(zooms[i - 1] - 0.001, "张开过程中缩放不应回退（回退就是画面抽搐）");
            zooms[i].Should().BeGreaterThan(SeatingCanvas.MinZoom + 0.01, "任何一帧都不应缩到最小、把会场甩出视口");
        }

        canvas.Zoom.Should().BeGreaterThan(1.5);
    }

    [AvaloniaFact]
    public void 双指对称张开_锚点下方的座位保持不动()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());

        // 两指中心对准 s1 座位中心
        var center = ToScreen(canvas, new Point(25, 15));
        canvas.HitTestSeat(center)!.Id.Should().Be("s1");

        var fingerA = window.TouchBegin(new Point(center.X - 30, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 30, center.Y));
        window.TouchMove(fingerA, new Point(center.X - 60, center.Y));
        window.TouchMove(fingerB, new Point(center.X + 60, center.Y));

        canvas.HitTestSeat(center).Should().NotBeNull("缩放锚点下方的座位不应漂走");
        canvas.HitTestSeat(center)!.Id.Should().Be("s1");

        window.TouchEnd(fingerA, new Point(center.X - 60, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 60, center.Y));
    }

    [AvaloniaFact]
    public void 双指张开超过上限后再合拢_缩放立即回落()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        var center = ToScreen(canvas, new Point(100, 60));

        var fingerA = window.TouchBegin(new Point(center.X - 20, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 20, center.Y));

        window.TouchMove(fingerA, new Point(center.X - 200, center.Y));
        window.TouchMove(fingerB, new Point(center.X + 200, center.Y));
        canvas.Zoom.Should().Be(SeatingCanvas.MaxZoom, "缩放到上限时夹在 3.0");

        window.TouchMove(fingerA, new Point(center.X - 40, center.Y));
        window.TouchMove(fingerB, new Point(center.X + 40, center.Y));
        canvas.Zoom.Should().BeLessThan(SeatingCanvas.MaxZoom - 0.3, "合拢后必须立即回落，不能被上限卡住");

        window.TouchEnd(fingerA, new Point(center.X - 40, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 40, center.Y));
    }

    [AvaloniaFact]
    public void 双指缩放_第三根手指移动不影响缩放()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        var center = ToScreen(canvas, new Point(100, 60));

        var fingerA = window.TouchBegin(new Point(center.X - 40, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 40, center.Y));
        window.TouchMove(fingerA, new Point(center.X - 80, center.Y));

        var zoomAfterPinch = canvas.Zoom;

        var fingerC = window.TouchBegin(new Point(center.X + 120, center.Y + 60));
        window.TouchMove(fingerC, new Point(center.X + 260, center.Y + 160));

        canvas.Zoom.Should().Be(zoomAfterPinch, "第三根手指不参与双指缩放");

        window.TouchEnd(fingerC, new Point(center.X + 260, center.Y + 160));
        window.TouchEnd(fingerA, new Point(center.X - 80, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 40, center.Y));
    }

    [AvaloniaFact]
    public void 双指缩放中抬起一根手指_剩余手指移动不再改变缩放()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());
        var center = ToScreen(canvas, new Point(100, 60));

        var fingerA = window.TouchBegin(new Point(center.X - 20, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 20, center.Y));
        window.TouchMove(fingerA, new Point(center.X - 50, center.Y));

        var zoomAfterPinch = canvas.Zoom;
        var panAfterPinch = canvas.PanOffset;

        // 抬起一根手指：双指手势结束，剩余手指的移动不能再被当成捏合
        window.TouchEnd(fingerA, new Point(center.X - 50, center.Y));
        window.TouchMove(fingerB, new Point(center.X + 260, center.Y + 160));

        canvas.Zoom.Should().Be(zoomAfterPinch, "抬起一根手指后缩放必须冻结，否则画面会跳");
        canvas.PanOffset.Should().Be(panAfterPinch, "抬起一根手指后剩余手指不应再平移板面");

        window.TouchEnd(fingerB, new Point(center.X + 260, center.Y + 160));
    }

    // ── 缩放/平移边界（回归：缩放后会场跑到屏幕外、拖不回来） ──

    [AvaloniaFact]
    public void 平移超界_板面始终留在视口内()
    {
        var (canvas, _) = CreateCanvasWindow(CreateSnapshot());

        canvas.PanOffset = new Vector(100000, 100000);

        var offset = canvas.ContentOffsetForTest;
        var boardWidth = canvas.Snapshot!.BoardWidth * canvas.Zoom;
        var boardHeight = canvas.Snapshot.BoardHeight * canvas.Zoom;

        offset.X.Should().BeGreaterThanOrEqualTo(0);
        offset.Y.Should().BeGreaterThanOrEqualTo(0);
        (offset.X + boardWidth).Should().BeLessThanOrEqualTo(canvas.Bounds.Width + 0.001,
            "板面小于视口时整块板面都必须留在视口内");
        (offset.Y + boardHeight).Should().BeLessThanOrEqualTo(canvas.Bounds.Height + 0.001);
    }

    [AvaloniaFact]
    public void 放大后平移超界_板面仍有足够区域可见()
    {
        var (canvas, _) = CreateCanvasWindow(CreateSnapshot());
        canvas.Zoom = 2.5;

        var boardWidth = canvas.Snapshot!.BoardWidth * canvas.Zoom;
        var boardHeight = canvas.Snapshot.BoardHeight * canvas.Zoom;

        canvas.PanBy(new Vector(100000, 100000));
        var offset = canvas.ContentOffsetForTest;
        VisibleWidth(offset.X, boardWidth, canvas.Bounds.Width)
            .Should().BeGreaterThanOrEqualTo(Math.Min(boardWidth, canvas.Bounds.Width * 0.75) - 0.001,
                "向右拖到极限时仍要留下足够大的板面区域，用户才能把它拖回来");
        VisibleHeight(offset.Y, boardHeight, canvas.Bounds.Height)
            .Should().BeGreaterThanOrEqualTo(Math.Min(boardHeight, canvas.Bounds.Height * 0.75) - 0.001);

        canvas.PanBy(new Vector(-100000, -100000));
        offset = canvas.ContentOffsetForTest;
        VisibleWidth(offset.X, boardWidth, canvas.Bounds.Width)
            .Should().BeGreaterThanOrEqualTo(Math.Min(boardWidth, canvas.Bounds.Width * 0.75) - 0.001);
        VisibleHeight(offset.Y, boardHeight, canvas.Bounds.Height)
            .Should().BeGreaterThanOrEqualTo(Math.Min(boardHeight, canvas.Bounds.Height * 0.75) - 0.001);
    }

    private static double VisibleWidth(double offsetX, double boardWidth, double viewportWidth)
        => Math.Min(offsetX + boardWidth, viewportWidth) - Math.Max(offsetX, 0);

    private static double VisibleHeight(double offsetY, double boardHeight, double viewportHeight)
        => Math.Min(offsetY + boardHeight, viewportHeight) - Math.Max(offsetY, 0);

    [AvaloniaFact]
    public void 缩小到板面放得下_平移量被收敛回可见范围()
    {
        var (canvas, _) = CreateCanvasWindow(CreateSnapshot());
        canvas.Zoom = 3.0;
        canvas.PanBy(new Vector(-100000, -100000));

        canvas.Zoom = 0.2;

        var offset = canvas.ContentOffsetForTest;
        offset.X.Should().BeGreaterThanOrEqualTo(0);
        offset.Y.Should().BeGreaterThanOrEqualTo(0);
        (offset.X + (canvas.Snapshot!.BoardWidth * 0.2))
            .Should().BeLessThanOrEqualTo(canvas.Bounds.Width + 0.001);
        (offset.Y + (canvas.Snapshot.BoardHeight * 0.2))
            .Should().BeLessThanOrEqualTo(canvas.Bounds.Height + 0.001);
    }

    [AvaloniaFact]
    public void 重置缩放_同时清空平移并回到居中()
    {
        var (canvas, _) = CreateCanvasWindow(CreateSnapshot());
        canvas.Zoom = 2.5;
        canvas.PanBy(new Vector(-100000, -100000));
        canvas.PanOffset.Should().NotBe(default(Vector));

        canvas.ResetView(1.0);

        canvas.Zoom.Should().Be(1.0);
        canvas.PanOffset.Should().Be(default(Vector), "重置视图必须同时把平移量归零，否则会场仍可能停在视口外");
        canvas.ContentOffsetForTest.X.Should().BeApproximately((canvas.Bounds.Width - 200) / 2, 0.001);
        canvas.ContentOffsetForTest.Y.Should().BeApproximately((canvas.Bounds.Height - 120) / 2, 0.001);
    }

    [AvaloniaFact]
    public void 第一根手指已经拖动_第二根手指仍能开始双指缩放()
    {
        var (canvas, window) = CreateCanvasWindow(CreateSnapshot());

        var drops = 0;
        canvas.SeatDropped += (_, _) => drops++;

        // 第一根手指从 s1 上移动超过 4px 阈值，已经进入拖拽
        var seat = ToScreen(canvas, new Point(25, 15));
        var fingerA = window.TouchBegin(seat);
        window.TouchMove(fingerA, new Point(seat.X + 12, seat.Y));

        // 第二根手指落下：必须让捏合接管（不能因为「正在拖拽」而把双指缩放判为无效）
        var fingerB = window.TouchBegin(new Point(seat.X + 80, seat.Y));
        window.TouchMove(fingerA, new Point(seat.X - 40, seat.Y));
        window.TouchMove(fingerB, new Point(seat.X + 140, seat.Y));

        canvas.Zoom.Should().BeGreaterThan(1.1, "双指张开必须放大，不能被单指拖拽状态挡住");

        window.TouchEnd(fingerA, new Point(seat.X - 40, seat.Y));
        window.TouchEnd(fingerB, new Point(seat.X + 140, seat.Y));
        drops.Should().Be(0, "被捏合接管的拖拽不应产生落座");
    }

    [AvaloniaFact]
    public void 双指缩放_同步到双向绑定的缩放比例()
    {
        var canvas = new SeatingCanvas { Snapshot = CreateSnapshot() };
        var holder = new ZoomHolder();
        canvas.Bind(
            SeatingCanvas.ZoomProperty,
            new Binding(nameof(ZoomHolder.Zoom)) { Source = holder, Mode = BindingMode.TwoWay });

        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.Show();
        window.UpdateLayout();

        holder.Zoom.Should().Be(1.0, "初始缩放应来自绑定源");

        var center = ToScreen(canvas, new Point(100, 60));
        var fingerA = window.TouchBegin(new Point(center.X - 40, center.Y));
        var fingerB = window.TouchBegin(new Point(center.X + 40, center.Y));
        window.TouchMove(fingerA, new Point(center.X - 80, center.Y));
        window.TouchMove(fingerB, new Point(center.X + 80, center.Y));

        canvas.Zoom.Should().BeGreaterThan(1.2);
        holder.Zoom.Should().BeApproximately(canvas.Zoom, 0.001,
            "缩放必须回写到双向绑定源，否则右下角百分比显示会与实际缩放不一致");

        window.TouchEnd(fingerA, new Point(center.X - 80, center.Y));
        window.TouchEnd(fingerB, new Point(center.X + 80, center.Y));
    }

    [AvaloniaFact]
    public void 重置视图_不破坏缩放的双向绑定()
    {
        var canvas = new SeatingCanvas { Snapshot = CreateSnapshot() };
        var holder = new ZoomHolder();
        canvas.Bind(
            SeatingCanvas.ZoomProperty,
            new Binding(nameof(ZoomHolder.Zoom)) { Source = holder, Mode = BindingMode.TwoWay });

        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.Show();
        window.UpdateLayout();

        holder.Zoom = 1.5;
        window.UpdateLayout();
        canvas.Zoom.Should().BeApproximately(1.5, 0.001, "绑定源的缩放必须传到画布");

        // 重置视图内部写入 Zoom：必须回写到绑定源，且不能把绑定拆掉
        canvas.ResetView(1.0);
        window.UpdateLayout();
        holder.Zoom.Should().BeApproximately(1.0, 0.001, "重置缩放应回写到绑定源");
        canvas.PanOffset.Should().Be(default(Vector), "重置视图必须清空平移");

        holder.Zoom = 2.0;
        window.UpdateLayout();
        canvas.Zoom.Should().BeApproximately(2.0, 0.001, "重置后绑定仍须生效");

        window.Close();
    }

    private sealed class ZoomHolder : INotifyPropertyChanged
    {
        private double _zoom = 1.0;

        public double Zoom
        {
            get => _zoom;
            set
            {
                if (Math.Abs(_zoom - value) < 1e-9) return;
                _zoom = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Zoom)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

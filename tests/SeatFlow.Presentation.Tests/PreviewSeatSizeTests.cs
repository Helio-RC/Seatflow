using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Services;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 预览座位尺寸测试（用户实测：快照预览座位重叠、放大也无法辨认姓名）：
/// Grid 预览尺寸必须小于真实坐标步进，保证座位不互相重叠。
/// </summary>
public class PreviewSeatSizeTests
{
    [Fact]
    public void Grid预览尺寸_按最小步进_不重叠()
    {
        var meta = new GridLayoutMetadata
        {
            SeatsPerDesk = 2,
            IntraDeskSpacing = 12,
            InterDeskSpacing = 40,
            VerticalSpacing = 56,
        };

        var (w, h) = PreviewSeatSize.ForGrid(meta);

        w.Should().BeLessThanOrEqualTo(12, "同桌步进 12，座位不得宽于步进");
        h.Should().BeLessThanOrEqualTo(56, "行步进 56，座位不得高于步进");
        w.Should().BeGreaterThan(1);
        h.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Grid预览尺寸_单座位桌_使用桌间距步进()
    {
        var meta = new GridLayoutMetadata
        {
            SeatsPerDesk = 1,
            IntraDeskSpacing = 12,
            InterDeskSpacing = 40,
            VerticalSpacing = 56,
        };

        var (w, _) = PreviewSeatSize.ForGrid(meta);

        w.Should().BeGreaterThan(12, "每桌一座时横向步进为桌间距 40");
        w.Should().BeLessThanOrEqualTo(40);
    }
}

using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using FluentAssertions;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>
/// 人员导入模板资源回归测试：模板以 AvaloniaResource 编译在 Presentation.Avalonia 程序集，
/// 曾因 URI 使用宿主程序集名（avares://SeatFlow/...）导致 AssetLoader 解析失败、报「模板缺失」。
/// </summary>
public class TemplateAssetTests
{
    [AvaloniaFact]
    public void 内置人员模板资源应存在()
    {
        AssetLoader.Exists(MemberManagementViewModel.BuildTemplateUri("zh_cn")).Should().BeTrue();
        AssetLoader.Exists(MemberManagementViewModel.BuildTemplateUri("en_us")).Should().BeTrue();
    }
}

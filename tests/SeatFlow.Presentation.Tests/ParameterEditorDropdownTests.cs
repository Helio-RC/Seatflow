using System.Text.Json;
using FluentAssertions;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.ViewModels;

namespace SeatFlow.Presentation.Tests;

/// <summary>声明式参数的下拉（Dropdown）渲染与稳定值回填。</summary>
public class ParameterEditorDropdownTests
{
    private static StrategyParameterDefinition SortByDefinition() => new()
    {
        Name = "SortBy",
        FieldType = StrategyFieldType.Dropdown,
        Label = new() { ["zh-CN"] = "排序规则" },
        DefaultValue = "Name",
        DropdownValues = ["Input", "Name", "Height"],
        DropdownLabels = new()
        {
            ["Input"] = new() { ["zh-CN"] = "名单顺序", ["en-US"] = "Roster order" },
            ["Name"] = new() { ["zh-CN"] = "姓名", ["en-US"] = "Name" },
            ["Height"] = new() { ["zh-CN"] = "身高", ["en-US"] = "Height" }
        }
    };

    [Fact]
    public void 下拉参数_选项本地化并按稳定值回填()
    {
        var vm = new ParameterEditorViewModel();

        vm.LoadParameters([SortByDefinition()], null);

        var param = vm.Parameters.Single();
        param.IsDropdown.Should().BeTrue();
        param.DropdownOptions.Should().HaveCount(3);
        param.SelectedOption!.Value.Should().Be("Name");

        param.SelectedOption = param.DropdownOptions.First(o => o.Value == "Input");
        param.Value.Should().Be("Input");
        vm.CollectValues()["SortBy"].Should().Be("Input");
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void 下拉参数_从持久化Json字符串回填()
    {
        using var doc = JsonDocument.Parse("\"Height\"");
        var vm = new ParameterEditorViewModel();

        vm.LoadParameters([SortByDefinition()], new() { ["SortBy"] = doc.RootElement.Clone() });

        vm.Parameters.Single().SelectedOption!.Value.Should().Be("Height");
    }

    [Fact]
    public void 下拉参数_未知值不回填()
    {
        var vm = new ParameterEditorViewModel();

        vm.LoadParameters([SortByDefinition()], new() { ["SortBy"] = "Unknown" });

        vm.Parameters.Single().SelectedOption.Should().BeNull();
    }
}

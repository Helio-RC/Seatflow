using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class FreeformManagementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;

    public string Title { get; } = "自由点管理";

    [ObservableProperty]
    private ObservableCollection<VenueItem> _savedLayouts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLayout))]
    private VenueItem? _selectedLayout;

    [ObservableProperty]
    private string _layoutName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FreeformPoint> _points = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPoints))]
    private bool _isEmpty = true;

    public bool HasSelectedLayout => SelectedLayout != null;
    public bool HasPoints => !IsEmpty;

    [ObservableProperty]
    private string _statusMessage = "就绪，请导入自由点数据或选择已有布局";

    public FreeformManagementViewModel (IApplicationFacade facade , IFileService fileService , IDialogService dialog)
    {
        _facade = facade;
        _fileService = fileService;
        _ = LoadSavedLayouts();
    }

    [RelayCommand]
    private async Task LoadSavedLayouts ()
    {
        await SafeExecuteAsync(async () =>
        {
            var ids = (await _facade.ListVenueIdsAsync()).ToList();
            var items = new List<VenueItem>();
            foreach (var id in ids)
            {
                var layout = await _facade.LoadVenueAsync(id);
                if (layout?.LayoutType == LayoutType.Freeform)
                    items.Add(new VenueItem(id , layout.Name));
            }
            SavedLayouts = new ObservableCollection<VenueItem>(items);
            StatusMessage = $"已加载 {items.Count} 个自由点布局";
        });
    }

    [RelayCommand]
    private async Task SelectLayout (VenueItem item)
    {
        await SafeExecuteAsync(async () =>
        {
            var layout = await _facade.LoadVenueAsync(item.Id);
            if (layout == null) return;

            LayoutName = layout.Name;
            var pts = layout.Seats.OfType<FreeformSeat>()
                .Select(s => new FreeformPoint(s.X , s.Y , s.Id))
                .ToList();
            Points = new ObservableCollection<FreeformPoint>(pts);
            IsEmpty = Points.Count == 0;
            StatusMessage = $"已加载布局「{layout.Name}」，共 {pts.Count} 个座位";
        });
    }

    [RelayCommand]
    private async Task ExportTemplate ()
    {
        var file = await _fileService.SaveFileAsync(
            "保存 CSV 模板" ,
            [new("CSV 文件") { Patterns = ["*.csv"] }] ,
            "自由点导入模板.csv");
        if (file == null) return;

        await SafeExecuteAsync(async () =>
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync("X,Y");
            await writer.WriteLineAsync("100,100");
            await writer.WriteLineAsync("200,100");
            await writer.WriteLineAsync("300,100");
            await writer.WriteLineAsync("100,200");
            await writer.WriteLineAsync("200,200");
            await writer.WriteLineAsync("300,200");
            StatusMessage = "模板已保存";
        } , "保存模板失败");
    }

    [RelayCommand]
    private async Task ImportCsv ()
    {
        var file = await _fileService.OpenFileAsync(
            "导入 CSV 坐标" ,
            [new("CSV 文件") { Patterns = ["*.csv"] }]);
        if (file == null) return;

        await SafeExecuteAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var pts = new List<FreeformPoint>();
            var lineNum = 0;
            while (await reader.ReadLineAsync() is { } line)
            {
                lineNum++;
                if (lineNum == 1) continue; // skip header
                var parts = line.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0].Trim() , NumberStyles.Any , CultureInfo.InvariantCulture , out var x) &&
                    double.TryParse(parts[1].Trim() , NumberStyles.Any , CultureInfo.InvariantCulture , out var y))
                {
                    pts.Add(new FreeformPoint(x , y));
                }
            }
            Points = new ObservableCollection<FreeformPoint>(pts);
            IsEmpty = Points.Count == 0;
            LayoutName = file.Name.Replace(".csv" , "");
            StatusMessage = $"已导入 {pts.Count} 个坐标点";
        } , "导入 CSV 失败");
    }

    [RelayCommand]
    private async Task ImportJson ()
    {
        var file = await _fileService.OpenFileAsync(
            "导入 JSON 布局" ,
            [new("JSON 文件") { Patterns = ["*.json"] }]);
        if (file == null) return;

        await SafeExecuteAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            var layout = await System.Text.Json.JsonSerializer.DeserializeAsync<ClassroomLayoutDefinition>(stream);
            if (layout == null) return;

            var pts = layout.Seats.OfType<FreeformSeat>()
                .Select(s => new FreeformPoint(s.X , s.Y , s.Id))
                .ToList();
            Points = new ObservableCollection<FreeformPoint>(pts);
            IsEmpty = Points.Count == 0;
            LayoutName = layout.Name;
            StatusMessage = $"已导入 {pts.Count} 个坐标点";
        } , "导入 JSON 失败");
    }

    [RelayCommand]
    private async Task SaveLayout ()
    {
        if (string.IsNullOrWhiteSpace(LayoutName))
        {
            await Dialog.ShowWarningAsync("保存失败" , "请输入布局名称");
            return;
        }

        var errors = ValidatePoints();
        if (errors.Count > 0)
        {
            await Dialog.ShowErrorAsync("数据校验未通过" ,
                string.Join('\n' , errors.Take(10)));
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var id = SelectedLayout?.Id ?? Guid.NewGuid().ToString("N")[..8];
            var layout = new ClassroomLayoutDefinition
            {
                Id = id ,
                Name = LayoutName ,
                LayoutType = LayoutType.Freeform ,
                Metadata = new FreeformLayoutMetadata()
            };
            foreach (var pt in Points)
                layout.Seats.Add(new FreeformSeat { Id = pt.Id , X = pt.X , Y = pt.Y });

            await _facade.SaveVenueAsync(id , layout);
            await LoadSavedLayouts();
            StatusMessage = $"布局「{LayoutName}」已保存，共 {Points.Count} 个座位";
        } , "保存布局失败");
    }

    [RelayCommand]
    private async Task DeleteLayout ()
    {
        if (SelectedLayout == null) return;
        var item = SelectedLayout;
        var confirmed = await Dialog.ShowConfirmAsync("确认删除" , $"确定要删除布局「{item.Name}」吗？");
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.DeleteVenueAsync(item.Id);
            SelectedLayout = null;
            Points.Clear();
            IsEmpty = true;
            LayoutName = string.Empty;
            await LoadSavedLayouts();
            StatusMessage = $"布局「{item.Name}」已删除";
        } , "删除布局失败");
    }

    [RelayCommand]
    private void AddPoint ()
    {
        Points.Add(new FreeformPoint(0 , 0));
        IsEmpty = false;
        StatusMessage = $"已添加点，当前共 {Points.Count} 个点";
    }

    [RelayCommand]
    private void DeletePoint (FreeformPoint point)
    {
        Points.Remove(point);
        IsEmpty = Points.Count == 0;
        StatusMessage = $"当前共 {Points.Count} 个点";
    }

    [RelayCommand]
    private void ClearPoints ()
    {
        Points.Clear();
        IsEmpty = true;
        StatusMessage = "已清空所有点";
    }

    private List<string> ValidatePoints ()
    {
        var errors = new List<string>();
        var seen = new HashSet<(double , double)>();

        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            var n = i + 1;

            if (double.IsNaN(p.X) || double.IsInfinity(p.X))
                errors.Add($"第 {n} 行：X 坐标无效");
            if (double.IsNaN(p.Y) || double.IsInfinity(p.Y))
                errors.Add($"第 {n} 行：Y 坐标无效");
            if (p.Y < 0)
                errors.Add($"第 {n} 行：Y 坐标为负值 ({p.Y:F1})，请确认坐标是否正确");

            var key = (p.X , p.Y);
            if (seen.Contains(key))
                errors.Add($"第 {n} 行：坐标 ({p.X:F1}, {p.Y:F1}) 与前面的点重复");
            seen.Add(key);
        }

        return errors;
    }
}

public class FreeformPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public FreeformPoint () { }

    public FreeformPoint (double x , double y , string? id = null)
    {
        X = x;
        Y = y;
        Id = id ?? Guid.NewGuid().ToString();
    }
}

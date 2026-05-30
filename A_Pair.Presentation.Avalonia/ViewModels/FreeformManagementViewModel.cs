using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Infrastructure.Layouts;
using A_Pair.Presentation.Avalonia.Lang;
using A_Pair.Presentation.Avalonia.Services;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class FreeformManagementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;
    private readonly ILogger<FreeformManagementViewModel> _logger;

    public string Title { get; } = Resources.Freeform_Title;

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

    partial void OnSelectedLayoutChanged (VenueItem? value)
    {
        if (value != null)
            _ = SelectLayout(value);
    }

    [ObservableProperty]
    private string _statusMessage = Resources.Freeform_ReadyHint;

    private int _dialogLock;
    private static readonly string[] GroupColors =
        ["#4A90D9" , "#E74C3C" , "#2ECC71" , "#F39C12" , "#9B59B6" , "#1ABC9C" , "#E67E22" , "#3498DB"];

    public static string GetGroupColor (int? groupId)
        => groupId is >= 0 and < 8 ? GroupColors[groupId.Value] : "#4A90D9";

    
    public string ElementCountDisplay => string.Format(Resources.Freeform_ElementCountFmt, Points.Count);

    public FreeformManagementViewModel (IApplicationFacade facade , IFileService fileService , IDialogService dialog , ILogger<FreeformManagementViewModel>? logger = null)
    {
        _facade = facade;
        _fileService = fileService;
        _logger = logger ?? NullLogger<FreeformManagementViewModel>.Instance;
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
            StatusMessage = string.Format(Resources.Freeform_LayoutsLoadedFmt, items.Count);
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
            var pts = new List<FreeformPoint>();

            // 加载座位
            foreach (var s in layout.Seats.OfType<FreeformSeat>())
            {
                int? groupId = null;
                if (!string.IsNullOrEmpty(s.LogicalGroup) && s.LogicalGroup.StartsWith("G")
                    && int.TryParse(s.LogicalGroup[1..] , out var gid))
                {
                    groupId = gid;
                }
                pts.Add(new FreeformPoint(s.X , s.Y , s.Id)
                {
                    ElementType = (int)FreeformElementType.Seat ,
                    GroupId = groupId ,
                    Row = s.Row ,
                    Column = s.Column
                });
            }

            // 加载障碍物（讲台/门）
            foreach (var obs in layout.Obstacles)
            {
                var et = obs.Type == "Podium" ? (int)FreeformElementType.Podium
                       : obs.Type == "Door" ? (int)FreeformElementType.Door
                       : (int)FreeformElementType.Seat;
                pts.Add(new FreeformPoint(obs.X , obs.Y)
                {
                    ElementType = et ,
                    Width = obs.Width ,
                    Height = obs.Height
                });
            }

            Points = new ObservableCollection<FreeformPoint>(pts);
            RefreshIndices();
            IsEmpty = Points.Count == 0;
            StatusMessage = string.Format(Resources.Freeform_LayoutLoadedFmt, layout.Name, pts.Count);
        });
    }

    [RelayCommand]
    private async Task ExportTemplate ()
    {
        if (Interlocked.CompareExchange(ref _dialogLock, 1, 0) != 0) return;
        try
        {
            IStorageFile? tmplFile;
            try { tmplFile = await _fileService.SaveFileAsync(
                Resources.Freeform_SaveTemplate ,
                [new(Resources.Data_CSVFile) { Patterns = ["*.csv"] }] ,
                Resources.Freeform_CSVTemplate); }
            catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常"); return; }
            if (tmplFile == null) return;
            var file = tmplFile;

            await SafeExecuteAsync(async () =>
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync("X,Y,Type,GroupId,Row,Column");
            await writer.WriteLineAsync("100,100,Seat,1,1,1");
            await writer.WriteLineAsync("200,100,Seat,1,1,2");
            await writer.WriteLineAsync("300,100,Seat,2,2,1");
            await writer.WriteLineAsync("100,200,Seat,2,2,2");
            await writer.WriteLineAsync("200,200,Seat,3,3,1");
            await writer.WriteLineAsync("300,200,Seat,3,3,2");
            await writer.WriteLineAsync("200,50,Podium,,,");
            await writer.WriteLineAsync("400,150,Door,,,");
            StatusMessage = Resources.Data_TemplateSaved;
        } , Resources.Data_TemplateSaveFailed);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常"); }
        finally { await Task.Delay(150); Interlocked.Exchange(ref _dialogLock, 0); }
    }

    [RelayCommand]
    private async Task ImportCsv ()
    {
        if (Interlocked.CompareExchange(ref _dialogLock, 1, 0) != 0) return;
        try
        {
        IStorageFile? csvFile;
        try { csvFile = await _fileService.OpenFileAsync(
            Resources.Freeform_ImportCSV ,
            [new(Resources.Data_CSVFile) { Patterns = ["*.csv"] }]); }
        catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常"); return; }
        if (csvFile == null) return;
        var file = csvFile;

        var cleanImport = false;
        if (Points.Count > 0)
        {
            var choice = await Dialog.ShowMultiOptionAsync(Resources.Freeform_ImportTitle ,
                string.Format(Resources.Freeform_ImportMsgFmt, Points.Count) ,
                Resources.Freeform_UnloadAndImport , Resources.Freeform_Overwrite , "取消");
            if (choice == null || choice == 2) return;
            cleanImport = choice == 0;
        }

        if (cleanImport)
        {
            SelectedLayout = null;
            LayoutName = string.Empty;
        }

        await SafeExecuteAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var pts = new List<FreeformPoint>();
            var lineNum = 0;
            while (await reader.ReadLineAsync() is { } line)
            {
                lineNum++;
                if (lineNum == 1) continue;
                var parts = line.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0].Trim() , NumberStyles.Any , CultureInfo.InvariantCulture , out var x) &&
                    double.TryParse(parts[1].Trim() , NumberStyles.Any , CultureInfo.InvariantCulture , out var y))
                {
                    var pt = new FreeformPoint(x , y);
                    // 解析可选列：Type,GroupId,Row,Column
                    if (parts.Length >= 3)
                        pt.ElementType = parts[2].Trim() switch
                        {
                            "Podium" => (int)FreeformElementType.Podium,
                            "Door" => (int)FreeformElementType.Door,
                            _ => (int)FreeformElementType.Seat
                        };
                    if (parts.Length >= 4 && int.TryParse(parts[3].Trim() , out var gid))
                        pt.GroupId = gid;
                    if (parts.Length >= 5 && int.TryParse(parts[4].Trim() , out var row))
                        pt.Row = row;
                    if (parts.Length >= 6 && int.TryParse(parts[5].Trim() , out var col))
                        pt.Column = col;
                    pts.Add(pt);
                }
            }
            Points = new ObservableCollection<FreeformPoint>(pts);
            RefreshIndices();
            IsEmpty = Points.Count == 0;
            LayoutName = file.Name.Replace(".csv" , "");
            StatusMessage = string.Format(Resources.Freeform_ImportedPtsFmt, pts.Count);
        } , Resources.Freeform_ImportFailed);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常"); }
        finally { await Task.Delay(150); Interlocked.Exchange(ref _dialogLock, 0); }
    }

    [RelayCommand]
    private async Task ImportJson ()
    {
        if (Interlocked.CompareExchange(ref _dialogLock, 1, 0) != 0) return;
        try
        {
        IStorageFile? jsonFile;
        try { jsonFile = await _fileService.OpenFileAsync(
            Resources.Freeform_ImportJSON ,
            [new(Resources.Data_JSONFile) { Patterns = ["*.json"] }]); }
        catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常"); return; }
        if (jsonFile == null) return;
        var file = jsonFile;

        var cleanImport = false;
        if (Points.Count > 0)
        {
            var choice = await Dialog.ShowMultiOptionAsync(Resources.Freeform_ImportTitle ,
                string.Format(Resources.Freeform_ImportMsgFmt, Points.Count) ,
                Resources.Freeform_UnloadAndImport , Resources.Freeform_Overwrite , "取消");
            if (choice == null || choice == 2) return;
            cleanImport = choice == 0;
        }

        if (cleanImport)
        {
            SelectedLayout = null;
            LayoutName = string.Empty;
        }

        await SafeExecuteAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            var layout = await System.Text.Json.JsonSerializer.DeserializeAsync<ClassroomLayoutDefinition>(stream);
            if (layout == null) return;

            var pts = new List<FreeformPoint>();
            foreach (var s in layout.Seats.OfType<FreeformSeat>())
            {
                int? groupId = null;
                if (!string.IsNullOrEmpty(s.LogicalGroup) && s.LogicalGroup.StartsWith("G")
                    && int.TryParse(s.LogicalGroup[1..] , out var gid))
                    groupId = gid;
                pts.Add(new FreeformPoint(s.X , s.Y , s.Id)
                {
                    ElementType = (int)FreeformElementType.Seat ,
                    GroupId = groupId ,
                    Row = s.Row ,
                    Column = s.Column
                });
            }
            foreach (var obs in layout.Obstacles)
            {
                var et = obs.Type == "Podium" ? (int)FreeformElementType.Podium
                       : obs.Type == "Door" ? (int)FreeformElementType.Door
                       : (int)FreeformElementType.Seat;
                pts.Add(new FreeformPoint(obs.X , obs.Y)
                {
                    ElementType = et ,
                    Width = obs.Width ,
                    Height = obs.Height
                });
            }

            Points = new ObservableCollection<FreeformPoint>(pts);
            RefreshIndices();
            IsEmpty = Points.Count == 0;
            LayoutName = layout.Name;
            StatusMessage = string.Format(Resources.Freeform_ImportedFmt, pts.Count);
        } , Resources.Freeform_ImportFailed);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "文件对话框取消或异常"); }
        finally { await Task.Delay(150); Interlocked.Exchange(ref _dialogLock, 0); }
    }

    [RelayCommand]
    private async Task SaveLayout ()
    {
        if (string.IsNullOrWhiteSpace(LayoutName))
        {
            await Dialog.ShowWarningAsync(Resources.Data_SaveFailed , Resources.Freeform_EnterLayoutName);
            return;
        }

        var errors = ValidatePoints();
        if (errors.Count > 0)
        {
            await Dialog.ShowErrorAsync(Resources.Data_ValidationFailed ,
                string.Join('\n' , errors.Take(10)));
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var id = SelectedLayout?.Id ?? Guid.NewGuid().ToString("N")[..8];

            var seatPoints = Points
                .Where(p => p.ElementType == (int)FreeformElementType.Seat)
                .Select(p => (p.X , p.Y , p.Row , p.Column , p.GroupId))
                .ToList();

            var obstaclePoints = Points
                .Where(p => (p.ElementType == (int)FreeformElementType.Podium) || p.ElementType == (int)FreeformElementType.Door)
                .Select(p => (p.X , p.Y , p.Width > 0 ? p.Width : 60 , p.Height > 0 ? p.Height : 40 ,
                    p.ElementType == (int)FreeformElementType.Podium ? "Podium" : "Door"))
                .ToList();

            var layout = FreeformLayoutBuilder.BuildFreeform(
                seatPoints ,
                obstaclePoints.Count > 0 ? obstaclePoints : null);
            layout.Id = id;
            layout.Name = LayoutName;

            await _facade.SaveVenueAsync(id , layout);
            await LoadSavedLayouts();
            SelectedLayout = SavedLayouts.FirstOrDefault(v => v.Id == id);
            StatusMessage = string.Format(Resources.Freeform_SavedFmt, LayoutName, Points.Count);
        } , Resources.Freeform_SaveLayoutFailed);
    }

    [RelayCommand]
    private async Task DeleteLayout ()
    {
        if (SelectedLayout == null) return;
        var item = SelectedLayout;
        var confirmed = await Dialog.ShowConfirmAsync(Resources.Freeform_DeleteConfirm , string.Format(Resources.Freeform_DeleteConfirmMsg, item.Name));
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _facade.DeleteVenueAsync(item.Id);
            SelectedLayout = null;
            Points.Clear();
            IsEmpty = true;
            LayoutName = string.Empty;
            await LoadSavedLayouts();
            StatusMessage = string.Format(Resources.Freeform_DeletedFmt, item.Name);
        } , Resources.Freeform_DeleteFailed);
    }

    [RelayCommand]
    private void AddPoint ()
    {
        Points.Add(new FreeformPoint(0 , 0));
        RefreshIndices();
        IsEmpty = false;
        StatusMessage = string.Format(Resources.Freeform_PointAddedFmt, Points.Count);
    }

    [RelayCommand]
    private void DeletePoint (FreeformPoint point)
    {
        Points.Remove(point);
        RefreshIndices();
        IsEmpty = Points.Count == 0;
        StatusMessage = string.Format(Resources.Freeform_PointCountFmt, Points.Count);
    }

    [RelayCommand]
    private void ClearPoints ()
    {
        Points.Clear();
        IsEmpty = true;
        StatusMessage = Resources.Freeform_PointsCleared;
    }

    [RelayCommand]
    private void Unload ()
    {
        Points.Clear();
        IsEmpty = true;
        LayoutName = string.Empty;
        SelectedLayout = null;
        StatusMessage = Resources.Freeform_UnloadedHint;
    }

    private void RefreshIndices ()
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i].DisplayIndex = i + 1;
        // 强制刷新 UI（FreeformPoint 非 ObservableObject）
        var copy = Points.ToList();
        Points = new ObservableCollection<FreeformPoint>(copy);
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
                errors.Add(string.Format(Resources.Freeform_RowXInvalidFmt, n));
            if (double.IsNaN(p.Y) || double.IsInfinity(p.Y))
                errors.Add(string.Format(Resources.Freeform_RowYInvalidFmt, n));
            if (p.Y < 0)
                errors.Add(string.Format(Resources.Freeform_RowYNegativeFmt, n, p.Y));

            if (p.ElementType == (int)FreeformElementType.Seat)
            {
                var key = (p.X , p.Y);
                if (seen.Contains(key))
                    errors.Add(string.Format(Resources.Freeform_DuplicatePointFmt, n, p.X, p.Y));
                seen.Add(key);
            }
        }

        return errors;
    }
}

public enum FreeformElementType
{
    Seat,
    Podium,
    Door
}

public class FreeformPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>元素类型: 0=座位, 1=讲台, 2=门</summary>
    public int ElementType { get; set; }

    public int? GroupId { get; set; }
    public int? Row { get; set; }
    public int? Column { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int DisplayIndex { get; set; }
    public string GroupColor { get; set; } = "#4A90D9";

    public string TooltipDisplay => ElementType switch
    {
        0 => string.Format(Resources.Freeform_SeatFmt, DisplayIndex),
        1 => string.Format(Resources.Freeform_PodiumFmt, DisplayIndex),
        2 => string.Format(Resources.Freeform_DoorFmt, DisplayIndex),
        _ => $"#{DisplayIndex}"
    };

    public FreeformPoint () { }

    public FreeformPoint (double x , double y , string? id = null)
    {
        X = x;
        Y = y;
        Id = id ?? Guid.NewGuid().ToString();
        GroupColor = FreeformManagementViewModel.GetGroupColor(null);
    }
}

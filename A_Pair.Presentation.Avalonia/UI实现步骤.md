# A_Pair UI 实现步骤

本文档是 UI 开发的详细实施指南，按依赖顺序列出每个步骤的具体操作。每步完成后均可编译运行验证。

---

## 前置：关键 API 速查

开发过程中只需查阅以下后端类型：

| 类型 | 所在文件 | 关键成员 |
|------|---------|---------|
| `IApplicationFacade` | `Application/Interfaces/IApplicationFacade.cs` | `LoadStudentsAsync`, `GenerateSeatingAsync`, `ExportSeatingPlanAsync`, `ExportStudentsAsync`, `ExecuteCommandAsync`, `UndoAsync`, `RedoAsync`, `GetSnapshotsAsync`, `RollbackToSnapshotAsync`, `SaveVenueAsync`, `LoadVenueAsync`, `ListVenueIdsAsync`, `LoadAppSettingsAsync`, `SaveAppSettingsAsync` |
| `SeatingWorkspace` | `Core/Workspace/SeatingWorkspace.cs` | `Students` (IReadOnlyList), `GetEmptySeats()`, `FindSeats(predicate)`, `TryAssignSeat(seatId, studentId, out error)`, `BuildSeatingPlan()`, `ApplySnapshotAssignments(dict)` |
| `SeatingRequest` | `Application/Interfaces/IApplicationFacade.cs` | `LayoutId`, `LayoutType`, `LayoutParameters`, `StrategyIds`, `UseDefaultStrategies`, `StudentDataSource`, `Description` |
| `Student` | `Core/Models/Student.cs` | `Id`, `Name`, `Height`, `Gender`, `NeedsFrontRow`, `FrontRowPreferenceScore` |
| `Seat` (abstract) | `Core/Models/Seat.cs` | `Id`, `Type`, `LogicalGroup`, `IsAvailable`, `IsFixed`, `OccupantId`, `Extensions` |
| `GridSeat` | `Core/Models/Seat.cs` | `Row`, `Column` |
| `PolarSeat` | `Core/Models/Seat.cs` | `Radius`, `AngleDegrees` |
| `FreeformSeat` | `Core/Models/FreeformSeat.cs` | `X`, `Y` |
| `ClassroomLayoutDefinition` | `Core/Models/ClassroomLayoutDefinition.cs` | `Id`, `Name`, `LayoutType`, `Seats`, `Obstacles`, `Metadata` |
| `SeatingSnapshot` | `Core/Models/SeatingSnapshot.cs` | `Id`, `CreatedAt`, `Description`, `LayoutId`, `SeatAssignments` |
| `ISeatingStrategy` | `Core/Strategies/ISeatingStrategy.cs` | `Id`, `Name`, `Priority`, `IsEnabled`, `ExecuteAsync`, `ValidateConfiguration` |
| `AssignSeatCommand` | `Application/Commands/AssignSeatCommand.cs` | `new(seatId, studentId)` — 实现 `IUndoableCommand` |
| `ExportFormat` | `Core/Models/ExportOptions.cs` | `Excel`, `Csv`, `Pdf`, `Json` |
| `ExportOptions` | `Core/Models/ExportOptions.cs` | `Format`, `Anonymize`, `IncludeMetadata` |
| `SeatGeometryHelper` | `Core/DomainServices/SeatGeometryHelper.cs` | `GetPosition(Seat, LayoutMetadata)` → `(double X, double Y)` |
| `PluginManager` | `Application/Plugins/PluginManager.cs` | `LoadPlugins()` → `IEnumerable<LoadedPluginInfo>`, `GetManifest(id)` |
| `LoadedPluginInfo` | `Application/Plugins/PluginManager.cs` | `Manifest`, `Strategy`, `PluginPath` |
| `AppSettings` | `Core/Models/AppSettings.cs` | `WindowState`, `LastOpenedFilePath`, `LastVenueId`, `RecentFiles` |

---

## 第一步：接通表示层与后端

### 1.1 添加项目引用

**文件**: `A_Pair.Presentation.Avalonia/A_Pair.Presentation.Avalonia.csproj`

在 `<ItemGroup>` 中添加：

```xml
<ProjectReference Include="..\A_Pair.Application\A_Pair.Application.csproj" />
```

添加后 `dotnet build` 应成功。这一行引用会传递引入 Core、Infrastructure、Contracts。

### 1.2 配置 DI 容器

**文件**: `A_Pair.Presentation.Avalonia/Program.cs`

重写 `Main` 方法，在启动 Avalonia 前配置 DI：

```csharp
using A_Pair.Application.Services;
using A_Pair.Presentation.Avalonia.Services;
using A_Pair.Presentation.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

[STAThread]
public static void Main(string[] args)
{
    var services = new ServiceCollection();
    
    // 注册后端（一行搞定所有 Core/App/Infra 服务）
    services.AddA_PairApplication("AppData", "Plugins");
    
    // 注册 UI 服务
    services.AddSingleton<INavigationService, NavigationService>();
    
    // 注册 ViewModels
    services.AddSingleton<MainShellViewModel>();
    services.AddTransient<DataManagementViewModel>();
    services.AddTransient<VenueConfigurationViewModel>();
    services.AddTransient<StrategyConfigurationViewModel>();
    services.AddTransient<SeatingArrangementViewModel>();
    services.AddTransient<SnapshotHistoryViewModel>();
    services.AddTransient<PluginManagementViewModel>();
    
    var provider = services.BuildServiceProvider();
    
    // 将 provider 传给 App 类，以便在 OnFrameworkInitializationCompleted 中使用
    App.ServiceProvider = provider;
    
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
```

### 1.3 修改 App.axaml.cs 使用 DI

**文件**: `A_Pair.Presentation.Avalonia/App.axaml.cs`

```csharp
public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainShell = ServiceProvider?.GetRequiredService<MainShellViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainShell
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
```

### 1.4 验证

`dotnet build`  → `dotnet run --project A_Pair.Presentation.Avalonia` 应启动窗口。

---

## 第二步：导航服务与主窗口壳

### 2.1 创建导航服务

**新建文件**: `A_Pair.Presentation.Avalonia/Services/INavigationService.cs`

```csharp
using A_Pair.Presentation.Avalonia.ViewModels;

namespace A_Pair.Presentation.Avalonia.Services;

public enum PageKey
{
    DataManagement,
    VenueConfiguration,
    StrategyConfiguration,
    SeatingArrangement,
    SnapshotHistory,
    PluginManagement
}

public interface INavigationService
{
    ViewModelBase CurrentViewModel { get; }
    PageKey CurrentPage { get; }
    event Action? CurrentViewModelChanged;
    void NavigateTo(PageKey page);
}
```

**新建文件**: `A_Pair.Presentation.Avalonia/Services/NavigationService.cs`

```csharp
using A_Pair.Presentation.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace A_Pair.Presentation.Avalonia.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public ViewModelBase CurrentViewModel { get; private set; } = default!;
    public PageKey CurrentPage { get; private set; }
    public event Action? CurrentViewModelChanged;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        // 默认导航到数据管理
        NavigateTo(PageKey.DataManagement);
    }

    public void NavigateTo(PageKey page)
    {
        CurrentPage = page;
        CurrentViewModel = page switch
        {
            PageKey.DataManagement => _serviceProvider.GetRequiredService<DataManagementViewModel>(),
            PageKey.VenueConfiguration => _serviceProvider.GetRequiredService<VenueConfigurationViewModel>(),
            PageKey.StrategyConfiguration => _serviceProvider.GetRequiredService<StrategyConfigurationViewModel>(),
            PageKey.SeatingArrangement => _serviceProvider.GetRequiredService<SeatingArrangementViewModel>(),
            PageKey.SnapshotHistory => _serviceProvider.GetRequiredService<SnapshotHistoryViewModel>(),
            PageKey.PluginManagement => _serviceProvider.GetRequiredService<PluginManagementViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };
        CurrentViewModelChanged?.Invoke();
    }
}
```

### 2.2 创建 MainShellViewModel

**重写文件**: `A_Pair.Presentation.Avalonia/ViewModels/MainShellViewModel.cs`

```csharp
using A_Pair.Presentation.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private ViewModelBase _currentViewModel = default!;

    [ObservableProperty]
    private PageKey _currentPage;

    public MainShellViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += () =>
        {
            CurrentViewModel = _navigation.CurrentViewModel;
            CurrentPage = _navigation.CurrentPage;
        };
        CurrentViewModel = _navigation.CurrentViewModel;
        CurrentPage = _navigation.CurrentPage;
    }

    [RelayCommand]
    private void Navigate(string pageName)
    {
        if (Enum.TryParse<PageKey>(pageName, out var key))
            _navigation.NavigateTo(key);
    }
}
```

### 2.3 重写 MainWindow.axaml（主窗口布局）

**文件**: `A_Pair.Presentation.Avalonia/Views/MainWindow.axaml`

主窗口采用左右分栏：左侧边栏导航按钮 + 右侧内容区。

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:A_Pair.Presentation.Avalonia.ViewModels"
        x:Class="A_Pair.Presentation.Avalonia.Views.MainWindow"
        x:DataType="vm:MainShellViewModel"
        Title="A_Pair — 座位安排系统" Width="1200" Height="800"
        MinWidth="900" MinHeight="600">

    <DockPanel>
        <!-- 侧边栏 -->
        <Border DockPanel.Dock="Left" Width="200" Background="#F0F0F0">
            <StackPanel Margin="10" Spacing="6">
                <TextBlock Text="A_Pair" FontSize="18" FontWeight="Bold" Margin="0,0,0,16"/>
                
                <Button Content="数据管理" 
                        Command="{Binding NavigateCommand}" CommandParameter="DataManagement"
                        Classes="{Binding CurrentPage, Converter={x:Static ...}}" />
                <Button Content="会场配置"
                        Command="{Binding NavigateCommand}" CommandParameter="VenueConfiguration"/>
                <Button Content="策略配置"
                        Command="{Binding NavigateCommand}" CommandParameter="StrategyConfiguration"/>
                <Button Content="座位安排"
                        Command="{Binding NavigateCommand}" CommandParameter="SeatingArrangement"/>
                <Button Content="历史快照"
                        Command="{Binding NavigateCommand}" CommandParameter="SnapshotHistory"/>
                <Button Content="插件管理"
                        Command="{Binding NavigateCommand}" CommandParameter="PluginManagement"/>
            </StackPanel>
        </Border>

        <!-- 内容区域：使用 ViewLocator 根据 CurrentViewModel 自动匹配视图 -->
        <ContentControl Content="{Binding CurrentViewModel}"
                        HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>
    </DockPanel>
</Window>
```

> 注：侧边栏高亮当前页可用 `Classes` 绑定配合样式实现，或直接在 ViewModel 中暴露 `IsXxxActive` 布尔属性。简单做法是先用无高亮的按钮，后续再优化。

### 2.4 删除旧的 MainWindowViewModel

保留但清空 `MainWindowViewModel.cs`（或删除，取决于是否保留引用），因为主窗口 DataContext 已改为 `MainShellViewModel`。

**文件**: `A_Pair.Presentation.Avalonia/ViewModels/MainWindowViewModel.cs` — 可设为空类占位，不做他用。

---

## 第三步：数据管理页

### 3.1 ViewModel

**新建/重写文件**: `A_Pair.Presentation.Avalonia/ViewModels/DataManagementViewModel.cs`

```csharp
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Enums;
using A_Pair.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class DataManagementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;

    public ObservableCollection<Student> Students { get; } = [];

    [ObservableProperty]
    private Student? _selectedStudent;

    [ObservableProperty]
    private string _dataSourcePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private int _totalStudents;

    [ObservableProperty]
    private int _frontRowCount;

    [ObservableProperty]
    private double _averageHeight;

    public DataManagementViewModel(IApplicationFacade facade)
    {
        _facade = facade;
    }

    [RelayCommand]
    private async Task ImportFromCsv(CancellationToken ct)
    {
        var path = await OpenFileDialogAsync("CSV 文件|*.csv");
        if (path == null) return;
        await ImportAsync(path, ct);
    }

    [RelayCommand]
    private async Task ImportFromXlsx(CancellationToken ct)
    {
        var path = await OpenFileDialogAsync("Excel 文件|*.xlsx");
        if (path == null) return;
        await ImportAsync(path, ct);
    }

    [RelayCommand]
    private async Task ImportFromJson(CancellationToken ct)
    {
        var path = await OpenFileDialogAsync("JSON 文件|*.json");
        if (path == null) return;
        await ImportAsync(path, ct);
    }

    private async Task ImportAsync(string path, CancellationToken ct)
    {
        try
        {
            StatusMessage = "正在加载...";
            var students = await _facade.LoadStudentsAsync(path, ct);
            Students.Clear();
            foreach (var s in students) Students.Add(s);
            DataSourcePath = path;
            UpdateStatistics();
            StatusMessage = $"已加载 {students.Count} 名学生";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportToCsv(CancellationToken ct) => await ExportAsync(ExportFormat.Csv, ct);
    
    [RelayCommand]
    private async Task ExportToXlsx(CancellationToken ct) => await ExportAsync(ExportFormat.Excel, ct);
    
    [RelayCommand]
    private async Task ExportToJson(CancellationToken ct) => await ExportAsync(ExportFormat.Json, ct);

    private async Task ExportAsync(ExportFormat format, CancellationToken ct)
    {
        var path = await SaveFileDialogAsync(format switch
        {
            ExportFormat.Csv => "CSV 文件|*.csv",
            ExportFormat.Excel => "Excel 文件|*.xlsx",
            ExportFormat.Json => "JSON 文件|*.json",
            _ => "所有文件|*.*"
        });
        if (path == null) return;
        try
        {
            await _facade.ExportStudentsAsync(path, Students, format, ct);
            StatusMessage = $"已导出到 {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Students.Clear();
        DataSourcePath = string.Empty;
        UpdateStatistics();
        StatusMessage = "已清空";
    }

    private void UpdateStatistics()
    {
        TotalStudents = Students.Count;
        FrontRowCount = Students.Count(s => s.NeedsFrontRow);
        AverageHeight = Students.Any(s => s.Height.HasValue)
            ? Students.Where(s => s.Height.HasValue).Average(s => s.Height!.Value)
            : 0;
    }

    // 文件对话框辅助 — Avalonia 使用 IStorageProvider
    private async Task<string?> OpenFileDialogAsync(string filter)
    {
        if (TopLevel.GetTopLevel(/* 需要从 View 获取 */) is not { } topLevel) return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            AllowMultiple = false
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> SaveFileDialogAsync(string filter)
    {
        if (TopLevel.GetTopLevel(/* ... */) is not { } topLevel) return null;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            SuggestedFileName = "students"
        });
        return file?.Path.LocalPath;
    }
}
```

> 文件对话框需要从 View 层获取 `TopLevel`。推荐做法是创建一个 `IDialogService` 封装文件对话框逻辑（参见下方的 3.3 补充设计）。

### 3.2 View

**新建/重写文件**: `A_Pair.Presentation.Avalonia/Views/DataManagementView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:A_Pair.Presentation.Avalonia.ViewModels"
             x:Class="A_Pair.Presentation.Avalonia.Views.DataManagementView"
             x:DataType="vm:DataManagementViewModel">
    
    <DockPanel Margin="16">
        <!-- 顶部工具栏 -->
        <StackPanel DockPanel.Dock="Top" Spacing="8" Margin="0,0,0,12">
            <TextBlock Text="数据管理" FontSize="20" FontWeight="Bold"/>
            <WrapPanel Spacing="6">
                <Button Content="导入 CSV" Command="{Binding ImportFromCsvCommand}"/>
                <Button Content="导入 Excel" Command="{Binding ImportFromXlsxCommand}"/>
                <Button Content="导入 JSON" Command="{Binding ImportFromJsonCommand}"/>
                <Separator Width="1" Height="24" Background="Gray"/>
                <Button Content="导出 CSV" Command="{Binding ExportToCsvCommand}"/>
                <Button Content="导出 Excel" Command="{Binding ExportToXlsxCommand}"/>
                <Button Content="导出 JSON" Command="{Binding ExportToJsonCommand}"/>
                <Separator Width="1" Height="24" Background="Gray"/>
                <Button Content="清空" Command="{Binding ClearCommand}"/>
            </WrapPanel>
        </StackPanel>

        <!-- 统计信息 -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="20" Margin="0,0,0,8">
            <TextBlock Text="{Binding TotalStudents, StringFormat='总人数: {0}'}"/>
            <TextBlock Text="{Binding FrontRowCount, StringFormat='需要前排: {0}'}"/>
            <TextBlock Text="{Binding AverageHeight, StringFormat='平均身高: {0:F1} cm'}"/>
        </StackPanel>

        <!-- 学生列表 -->
        <DataGrid ItemsSource="{Binding Students}" SelectedItem="{Binding SelectedStudent}"
                  AutoGenerateColumns="False" IsReadOnly="False">
            <DataGrid.Columns>
                <DataGridTextColumn Header="ID" Binding="{Binding Id}" IsReadOnly="True"/>
                <DataGridTextColumn Header="姓名" Binding="{Binding Name}"/>
                <DataGridTextColumn Header="身高(cm)" Binding="{Binding Height}"/>
                <DataGridTextColumn Header="性别" Binding="{Binding Gender}"/>
                <DataGridCheckBoxColumn Header="需要前排" Binding="{Binding NeedsFrontRow}"/>
            </DataGrid.Columns>
        </DataGrid>

        <!-- 底部状态栏 -->
        <TextBlock DockPanel.Dock="Bottom" Text="{Binding StatusMessage}" 
                   Margin="0,8,0,0" FontStyle="Italic"/>
    </DockPanel>
</UserControl>
```

**文件**: `A_Pair.Presentation.Avalonia/Views/DataManagementView.axaml.cs`（代码后置，只调用 InitializeComponent）

```csharp
using Avalonia.Controls;

namespace A_Pair.Presentation.Avalonia.Views;

public partial class DataManagementView : UserControl
{
    public DataManagementView()
    {
        InitializeComponent();
    }
}
```

### 3.3 文件对话框服务（因 Avalonia 需要 TopLevel）

**新建文件**: `A_Pair.Presentation.Avalonia/Services/IDialogService.cs`

```csharp
namespace A_Pair.Presentation.Avalonia.Services;

public interface IDialogService
{
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FilePickerFilter>? filters = null);
    Task<string?> SaveFileAsync(string title, string? defaultName = null, IReadOnlyList<FilePickerFilter>? filters = null);
}

public record FilePickerFilter(string Name, IReadOnlyList<string> Patterns);
```

需要在 MainWindow 加载后将 TopLevel 注入到 DialogService 中。简单做法是直接在 ViewModel 中注入一个依赖 `TopLevel` 的服务包装。

> 为简化第一步实现，可以先在 View 的 code-behind 中处理文件对话框，通过事件通知 ViewModel，后续再提取为 DialogService。

---

## 第四步：会场配置页

### 4.1 ViewModel

**新建/重写文件**: `A_Pair.Presentation.Avalonia/ViewModels/VenueConfigurationViewModel.cs`

```csharp
using System.Collections.ObjectModel;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Infrastructure.Layouts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class VenueConfigurationViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;

    [ObservableProperty]
    private LayoutType _selectedLayoutType = LayoutType.Grid;

    // 网格参数
    [ObservableProperty] private int _rows = 5;
    [ObservableProperty] private int _columns = 6;
    [ObservableProperty] private double _gridHSpacing = 60.0;
    [ObservableProperty] private double _gridVSpacing = 60.0;

    // 极坐标参数
    [ObservableProperty] private int _rings = 3;
    [ObservableProperty] private int _seatsPerRing = 10;
    [ObservableProperty] private double _radiusStep = 80.0;

    // 通用
    [ObservableProperty] private string _venueName = "新会场";
    [ObservableProperty] private string _venueId = string.Empty;
    [ObservableProperty] private string _statusMessage = "就绪";

    // 预览座位列表
    public ObservableCollection<Seat> PreviewSeats { get; } = [];

    public VenueConfigurationViewModel(IApplicationFacade facade)
    {
        _facade = facade;
        // 添加 LayoutType 变更处理 — 使用 partial OnXxxChanged
    }

    // 当 LayoutType 改变时自动刷新预览
    partial void OnSelectedLayoutTypeChanged(LayoutType value) => GeneratePreview();
    partial void OnRowsChanged(int value) { if (SelectedLayoutType == LayoutType.Grid) GeneratePreview(); }
    partial void OnColumnsChanged(int value) { if (SelectedLayoutType == LayoutType.Grid) GeneratePreview(); }
    partial void OnRingsChanged(int value) { if (SelectedLayoutType == LayoutType.Polar) GeneratePreview(); }
    partial void OnSeatsPerRingChanged(int value) { if (SelectedLayoutType == LayoutType.Polar) GeneratePreview(); }
    partial void OnRadiusStepChanged(double value) { if (SelectedLayoutType == LayoutType.Polar) GeneratePreview(); }

    [RelayCommand]
    private void GeneratePreview()
    {
        ClassroomLayoutDefinition layout = SelectedLayoutType switch
        {
            LayoutType.Grid => GridLayoutBuilder.BuildGrid(Rows, Columns),
            LayoutType.Polar => PolarLayoutBuilder.BuildPolar(RadiusStep, Rings, SeatsPerRing),
            LayoutType.Freeform => new ClassroomLayoutDefinition { LayoutType = LayoutType.Freeform },
            _ => throw new ArgumentOutOfRangeException()
        };

        layout.Name = VenueName;
        PreviewSeats.Clear();
        foreach (var seat in layout.Seats) PreviewSeats.Add(seat);
        StatusMessage = $"已生成 {PreviewSeats.Count} 个座位";
    }

    [RelayCommand]
    private async Task SaveVenue(CancellationToken ct)
    {
        var layout = new ClassroomLayoutDefinition
        {
            Id = string.IsNullOrEmpty(VenueId) ? Guid.NewGuid().ToString() : VenueId,
            Name = VenueName,
            LayoutType = SelectedLayoutType
        };
        // 将 PreviewSeats 加入 layout.Seats
        foreach (var seat in PreviewSeats) layout.Seats.Add(seat);

        await _facade.SaveVenueAsync(layout.Id, layout, ct);
        VenueId = layout.Id;
        StatusMessage = $"已保存会场: {VenueName} ({layout.Id})";
    }

    [RelayCommand]
    private async Task LoadVenue(string venueId, CancellationToken ct)
    {
        var layout = await _facade.LoadVenueAsync(venueId, ct);
        if (layout == null) { StatusMessage = "未找到会场"; return; }
        VenueId = layout.Id;
        VenueName = layout.Name;
        SelectedLayoutType = layout.LayoutType;
        PreviewSeats.Clear();
        foreach (var seat in layout.Seats) PreviewSeats.Add(seat);
        StatusMessage = $"已加载会场: {layout.Name}";
    }
}
```

### 4.2 View

**新建文件**: `A_Pair.Presentation.Avalonia/Views/VenueConfigurationView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:A_Pair.Presentation.Avalonia.ViewModels"
             x:Class="A_Pair.Presentation.Avalonia.Views.VenueConfigurationView"
             x:DataType="vm:VenueConfigurationViewModel">
    
    <Grid ColumnDefinitions="300,*" Margin="16">
        <!-- 左侧参数面板 -->
        <ScrollViewer Grid.Column="0">
            <StackPanel Spacing="10">
                <TextBlock Text="会场配置" FontSize="20" FontWeight="Bold"/>
                
                <!-- 基本信息 -->
                <TextBlock Text="会场名称"/>
                <TextBox Text="{Binding VenueName}"/>
                <TextBlock Text="会场 ID"/>
                <TextBox Text="{Binding VenueId}" IsReadOnly="True"/>

                <!-- 布局类型 -->
                <TextBlock Text="布局类型" Margin="0,8,0,0"/>
                <ComboBox SelectedIndex="{Binding SelectedLayoutType}">
                    <ComboBoxItem Content="网格 (Grid)"/>
                    <ComboBoxItem Content="环形 (Polar)"/>
                    <ComboBoxItem Content="自由点 (Freeform)"/>
                </ComboBox>

                <!-- 网格参数 -->
                <Border IsVisible="{Binding SelectedLayoutType, Converter={x:Static ???}}" Padding="8" Background="#FAFAFA">
                    <StackPanel Spacing="6">
                        <TextBlock Text="网格参数" FontWeight="Bold"/>
                        <TextBlock Text="行数"/>
                        <NumericUpDown Value="{Binding Rows}" Minimum="1" Maximum="50"/>
                        <TextBlock Text="列数"/>
                        <NumericUpDown Value="{Binding Columns}" Minimum="1" Maximum="50"/>
                        <TextBlock Text="水平间距"/>
                        <NumericUpDown Value="{Binding GridHSpacing}" Minimum="20" Maximum="200"/>
                        <TextBlock Text="垂直间距"/>
                        <NumericUpDown Value="{Binding GridVSpacing}" Minimum="20" Maximum="200"/>
                    </StackPanel>
                </Border>

                <!-- 极坐标参数 -->
                <Border IsVisible="{Binding ...}" Padding="8" Background="#FAFAFA">
                    <!-- 类似网格参数，展示 Rings / SeatsPerRing / RadiusStep -->
                </Border>

                <Button Content="生成预览" Command="{Binding GeneratePreviewCommand}" Margin="0,8,0,0"/>
                <Button Content="保存会场" Command="{Binding SaveVenueCommand}"/>
                <Button Content="加载已有会场" Command="{Binding LoadVenueCommand}" CommandParameter="..."/>
            </StackPanel>
        </ScrollViewer>

        <!-- 右侧预览画布 -->
        <Border Grid.Column="1" Background="White" Margin="8,0,0,0" BorderBrush="LightGray" BorderThickness="1">
            <ItemsControl ItemsSource="{Binding PreviewSeats}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><Canvas/></ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Width="40" Height="40" Background="LightBlue" BorderBrush="Gray" BorderThickness="1"
                                CornerRadius="4"
                                Canvas.Left="..." Canvas.Top="...">
                            <TextBlock Text="{Binding Id}" FontSize="8" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Border>
        
        <TextBlock Grid.Column="0" Grid.Row="1" Text="{Binding StatusMessage}" FontStyle="Italic"/>
    </Grid>
</UserControl>
```

> 布局类型切换可见性最简单的方式：使用三个 Border，分别绑定 `IsVisible` 到一个计算属性（如 `IsGridLayout`、`IsPolarLayout`），或者使用 `Binding` + `IValueConverter`。暂可用 `ContentControl` + `DataTemplateSelector`，但为了快速实现，用多个 Border + IsVisible 绑定最直接。

---

## 第五步：策略配置页

### 5.1 策略包装 ViewModel

**新建文件**: `A_Pair.Presentation.Avalonia/ViewModels/StrategyItemViewModel.cs`

```csharp
using A_Pair.Core.Strategies;
using CommunityToolkit.Mvvm.ComponentModel;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class StrategyItemViewModel : ViewModelBase
{
    public ISeatingStrategy Strategy { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _priority;

    public string Name => Strategy.Name;
    public string Id => Strategy.Id;

    public StrategyItemViewModel(ISeatingStrategy strategy)
    {
        Strategy = strategy;
        _isEnabled = strategy.IsEnabled;
        _priority = strategy.Priority;
    }

    partial void OnIsEnabledChanged(bool value) => Strategy.IsEnabled = value;
    partial void OnPriorityChanged(int value) => Strategy.Priority = value;
}
```

### 5.2 ViewModel

**新建/重写文件**: `A_Pair.Presentation.Avalonia/ViewModels/StrategyConfigurationViewModel.cs`

```csharp
using System.Collections.ObjectModel;
using A_Pair.Core.Strategies;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class StrategyConfigurationViewModel : ViewModelBase
{
    public ObservableCollection<StrategyItemViewModel> Strategies { get; } = [];

    [ObservableProperty]
    private StrategyItemViewModel? _selectedStrategy;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public StrategyConfigurationViewModel(IEnumerable<ISeatingStrategy> strategies)
    {
        foreach (var s in strategies.OrderBy(s => s.Priority))
            Strategies.Add(new StrategyItemViewModel(s));
    }

    [RelayCommand]
    private void MoveUp(StrategyItemViewModel item)
    {
        var idx = Strategies.IndexOf(item);
        if (idx <= 0) return;
        Strategies.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveDown(StrategyItemViewModel item)
    {
        var idx = Strategies.IndexOf(item);
        if (idx >= Strategies.Count - 1) return;
        Strategies.Move(idx, idx + 1);
    }
}
```

### 5.3 View

列表 + 操作按钮：

```xml
<UserControl xmlns="..." x:Class="..." x:DataType="vm:StrategyConfigurationViewModel">
    <DockPanel Margin="16">
        <TextBlock DockPanel.Dock="Top" Text="策略配置" FontSize="20" FontWeight="Bold" Margin="0,0,0,12"/>
        
        <Grid ColumnDefinitions="*,Auto" RowDefinitions="Auto,*">
            <ListBox ItemsSource="{Binding Strategies}" SelectedItem="{Binding SelectedStrategy}"
                     Grid.Row="1" Grid.Column="0">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="Auto,*,Auto,Auto" Margin="4">
                            <CheckBox IsChecked="{Binding IsEnabled}" Grid.Column="0" Margin="0,0,8,0"/>
                            <TextBlock Text="{Binding Name}" Grid.Column="1" VerticalAlignment="Center"/>
                            <TextBlock Text="{Binding Priority, StringFormat='优先级: {0}'}" Grid.Column="2" Margin="8,0"/>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <StackPanel Grid.Column="1" Grid.Row="1" Margin="8,0,0,0" Spacing="6">
                <Button Content="▲ 上移" Command="{Binding MoveUpCommand}" 
                        CommandParameter="{Binding SelectedStrategy}"/>
                <Button Content="▼ 下移" Command="{Binding MoveDownCommand}"
                        CommandParameter="{Binding SelectedStrategy}"/>
            </StackPanel>
        </Grid>
        
        <TextBlock DockPanel.Dock="Bottom" Text="{Binding StatusMessage}" FontStyle="Italic"/>
    </DockPanel>
</UserControl>
```

---

## 第六步：座位安排页（核心页面）

这是最复杂的页面，需要座位可视化渲染、拖拽交互和撤销/重做。

### 6.1 SeatViewModel

**新建文件**: `A_Pair.Presentation.Avalonia/ViewModels/SeatViewModel.cs`

```csharp
using A_Pair.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SeatViewModel : ViewModelBase
{
    public string Id { get; }
    public SeatType Type { get; }

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private string _occupantName = string.Empty;
    [ObservableProperty] private string _occupantId = string.Empty;
    [ObservableProperty] private bool _isFixed;
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isHighlighted;

    // 座位外观
    public double Width => Type == SeatType.Polar ? 44 : 60;
    public double Height => Type == SeatType.Polar ? 44 : 40;
    public double CornerRadius => Type == SeatType.Polar ? 22 : 4;

    public SeatViewModel(Seat seat, LayoutMetadata metadata)
    {
        Id = seat.Id;
        Type = seat.Type;
        IsFixed = seat.IsFixed;
        IsAvailable = seat.IsAvailable;
        OccupantId = seat.OccupantId ?? string.Empty;

        var pos = SeatGeometryHelper.GetPosition(seat, metadata);
        X = pos.X;
        Y = pos.Y;
    }
}
```

### 6.2 ViewModel

**新建/重写文件**: `A_Pair.Presentation.Avalonia/ViewModels/SeatingArrangementViewModel.cs`

```csharp
using System.Collections.ObjectModel;
using A_Pair.Application.Commands;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using A_Pair.Core.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SeatingArrangementViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private SeatingWorkspace? _workspace;
    private ClassroomLayoutDefinition? _currentLayout;

    public ObservableCollection<SeatViewModel> Seats { get; } = [];
    public ObservableCollection<Student> UnassignedStudents { get; } = [];

    [ObservableProperty] private SeatViewModel? _selectedSeat;
    [ObservableProperty] private Student? _selectedUnassignedStudent;
    [ObservableProperty] private string _venueId = string.Empty;
    [ObservableProperty] private string _studentSource = string.Empty;
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private int _assignedCount;
    [ObservableProperty] private int _totalSeats;
    [ObservableProperty] private double _zoomLevel = 1.0;
    [ObservableProperty] private double _panX;
    [ObservableProperty] private double _panY;

    public SeatingArrangementViewModel(IApplicationFacade facade)
    {
        _facade = facade;
    }

    [RelayCommand]
    private async Task GenerateSeating(CancellationToken ct)
    {
        var request = new SeatingRequest
        {
            LayoutId = string.IsNullOrEmpty(VenueId) ? null : VenueId,
            StudentDataSource = string.IsNullOrEmpty(StudentSource) ? null : StudentSource,
            UseDefaultStrategies = true
        };

        StatusMessage = "正在生成座位安排...";
        _workspace = await _facade.GenerateSeatingAsync(request, null, ct);

        // 加载布局元数据用于坐标计算
        if (!string.IsNullOrEmpty(VenueId))
            _currentLayout = await _facade.LoadVenueAsync(VenueId, ct);
        else
            _currentLayout = new ClassroomLayoutDefinition { LayoutType = request.LayoutType };

        RefreshViews();
        StatusMessage = "座位生成完成";
    }

    private void RefreshViews()
    {
        if (_workspace == null) return;

        Seats.Clear();
        UnassignedStudents.Clear();

        var metadata = _currentLayout?.Metadata ?? new GridLayoutMetadata();
        foreach (var seat in _workspace.FindSeats(_ => true))
        {
            var svm = new SeatViewModel(seat, metadata);
            if (!string.IsNullOrEmpty(seat.OccupantId))
            {
                var student = _workspace.Students.FirstOrDefault(s => s.Id == seat.OccupantId);
                svm.OccupantName = student?.Name ?? seat.OccupantId;
                svm.OccupantId = seat.OccupantId!;
            }
            Seats.Add(svm);
        }

        var assignedIds = new HashSet<string>(
            _workspace.FindSeats(s => !string.IsNullOrEmpty(s.OccupantId))
                     .Select(s => s.OccupantId!));
        foreach (var student in _workspace.Students)
        {
            if (!assignedIds.Contains(student.Id))
                UnassignedStudents.Add(student);
        }

        TotalSeats = Seats.Count;
        AssignedCount = Seats.Count(s => !string.IsNullOrEmpty(s.OccupantId));
    }

    [RelayCommand]
    private async Task AssignStudentToSeat(/* 参数来自拖拽 */ CancellationToken ct)
    {
        // 实现见 6.3 拖拽小节
    }

    [RelayCommand]
    private async Task Undo(CancellationToken ct)
    {
        if (await _facade.UndoAsync(ct))
            RefreshViews();
    }

    [RelayCommand]
    private async Task Redo(CancellationToken ct)
    {
        if (await _facade.RedoAsync(ct))
            RefreshViews();
    }

    [RelayCommand]
    private async Task ExportToExcel(CancellationToken ct)
    {
        if (_workspace == null) return;
        // 使用文件对话框选择路径
        var options = new ExportOptions { Format = ExportFormat.Excel };
        // await _facade.ExportSeatingPlanAsync(_workspace, path, options, ct);
    }
}
```

### 6.3 座位画布控件

**新建文件**: `A_Pair.Presentation.Avalonia/Controls/SeatCanvas.cs`

```csharp
using Avalonia.Controls;
using Avalonia.Input;

namespace A_Pair.Presentation.Avalonia.Controls;

public class SeatCanvas : Canvas
{
    // 处理缩放（MouseWheel）+ 平移（右键拖拽）+ 座位点击/拖拽
    private double _zoom = 1.0;

    // 详见后面"自定义控件"小节
}
```

### 6.4 View

```xml
<UserControl xmlns="..." x:Class="..." x:DataType="vm:SeatingArrangementViewModel">
    <DockPanel Margin="16">
        <!-- 顶部工具栏 -->
        <StackPanel DockPanel.Dock="Top" Spacing="8" Margin="0,0,0,12">
            <TextBlock Text="座位安排" FontSize="20" FontWeight="Bold"/>
            <WrapPanel Spacing="6">
                <TextBlock Text="会场 ID" VerticalAlignment="Center"/>
                <TextBox Text="{Binding VenueId}" Width="150"/>
                <TextBlock Text="学生数据" VerticalAlignment="Center"/>
                <TextBox Text="{Binding StudentSource}" Width="200"/>
                <Button Content="生成座位" Command="{Binding GenerateSeatingCommand}"/>
                <Button Content="撤销" Command="{Binding UndoCommand}"/>
                <Button Content="重做" Command="{Binding RedoCommand}"/>
                <Button Content="导出 Excel" Command="{Binding ExportToExcelCommand}"/>
            </WrapPanel>
            <WrapPanel Spacing="16">
                <TextBlock Text="{Binding AssignedCount, StringFormat='已分配: {0}'}"/>
                <TextBlock Text="{Binding TotalSeats, StringFormat='共 {0} 座'}"/>
            </WrapPanel>
        </StackPanel>

        <!-- 主体：左侧未分配列表 + 右侧画布 -->
        <Grid ColumnDefinitions="250,*">
            <!-- 未分配学生列表 -->
            <Border Grid.Column="0" BorderBrush="Gray" BorderThickness="1" Margin="0,0,8,0">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top" Text="未分配学生" FontWeight="Bold" Margin="4"/>
                    <ListBox ItemsSource="{Binding UnassignedStudents}" 
                             SelectedItem="{Binding SelectedUnassignedStudent}">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Name}"/>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </DockPanel>
            </Border>

            <!-- 座位画布 -->
            <ScrollViewer Grid.Column="1">
                <ItemsControl ItemsSource="{Binding Seats}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <Canvas/>
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Width="{Binding Width}" Height="{Binding Height}"
                                    CornerRadius="{Binding CornerRadius}"
                                    Background="LightBlue" BorderBrush="Gray" BorderThickness="1"
                                    Canvas.Left="{Binding X}" Canvas.Top="{Binding Y}">
                                <TextBlock Text="{Binding OccupantName}" FontSize="10"
                                           HorizontalAlignment="Center" VerticalAlignment="Center"
                                           TextTrimming="CharacterEllipsis"/>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>

        <TextBlock DockPanel.Dock="Bottom" Text="{Binding StatusMessage}" FontStyle="Italic"/>
    </DockPanel>
</UserControl>
```

---

## 第七步：历史快照页

### 7.1 ViewModel

**新建/重写**: `A_Pair.Presentation.Avalonia/ViewModels/SnapshotHistoryViewModel.cs`

```csharp
using System.Collections.ObjectModel;
using A_Pair.Application.Interfaces;
using A_Pair.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class SnapshotHistoryViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;

    public ObservableCollection<SeatingSnapshot> Snapshots { get; } = [];

    [ObservableProperty] private SeatingSnapshot? _selectedSnapshot;
    [ObservableProperty] private string _venueId = string.Empty;
    [ObservableProperty] private string _statusMessage = "就绪";

    public SnapshotHistoryViewModel(IApplicationFacade facade)
    {
        _facade = facade;
    }

    [RelayCommand]
    private async Task LoadSnapshots(CancellationToken ct)
    {
        StatusMessage = "加载快照中...";
        var list = await _facade.GetSnapshotsAsync(VenueId, ct);
        Snapshots.Clear();
        foreach (var s in list) Snapshots.Add(s);
        StatusMessage = $"已加载 {Snapshots.Count} 个快照";
    }

    [RelayCommand]
    private async Task ApplySnapshot(SeatingSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            await _facade.RollbackToSnapshotAsync(snapshot.Id, ct);
            StatusMessage = $"已回滚到快照: {snapshot.Description}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"回滚失败: {ex.Message}";
        }
    }
}
```

### 7.2 View

```xml
<UserControl xmlns="..." x:Class="..." x:DataType="vm:SnapshotHistoryViewModel">
    <DockPanel Margin="16">
        <StackPanel DockPanel.Dock="Top" Spacing="8" Margin="0,0,0,12">
            <TextBlock Text="历史快照" FontSize="20" FontWeight="Bold"/>
            <WrapPanel Spacing="6">
                <TextBlock Text="会场 ID" VerticalAlignment="Center"/>
                <TextBox Text="{Binding VenueId}" Width="150"/>
                <Button Content="加载快照" Command="{Binding LoadSnapshotsCommand}"/>
            </WrapPanel>
        </StackPanel>

        <Grid ColumnDefinitions="*,Auto">
            <ListBox ItemsSource="{Binding Snapshots}" SelectedItem="{Binding SelectedSnapshot}"
                     Grid.Column="0">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="Auto,*,Auto" Margin="4">
                            <TextBlock Text="{Binding CreatedAt, StringFormat='{0:yyyy-MM-dd HH:mm}'}" 
                                       Grid.Column="0" Width="140"/>
                            <TextBlock Text="{Binding Description}" Grid.Column="1" Margin="8,0"/>
                            <TextBlock Text="{Binding SeatAssignments.Count, StringFormat='{0} 座位'}" 
                                       Grid.Column="2"/>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <StackPanel Grid.Column="1" Margin="8,0,0,0" Spacing="6">
                <Button Content="应用此快照" 
                        Command="{Binding ApplySnapshotCommand}"
                        CommandParameter="{Binding SelectedSnapshot}"/>
            </StackPanel>
        </Grid>

        <TextBlock DockPanel.Dock="Bottom" Text="{Binding StatusMessage}" FontStyle="Italic"/>
    </DockPanel>
</UserControl>
```

---

## 第八步：插件管理页

### 8.1 ViewModel

**新建/重写**: `A_Pair.Presentation.Avalonia/ViewModels/PluginManagementViewModel.cs`

```csharp
using System.Collections.ObjectModel;
using A_Pair.Application.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class PluginManagementViewModel : ViewModelBase
{
    private readonly PluginManager _pluginManager;

    public ObservableCollection<LoadedPluginInfo> Plugins { get; } = [];

    [ObservableProperty] private LoadedPluginInfo? _selectedPlugin;
    [ObservableProperty] private string _statusMessage = "就绪";

    public PluginManagementViewModel(PluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    [RelayCommand]
    private void Refresh()
    {
        var plugins = _pluginManager.LoadPlugins();
        Plugins.Clear();
        foreach (var p in plugins) Plugins.Add(p);
        StatusMessage = $"已加载 {Plugins.Count} 个插件";
    }

    [RelayCommand]
    private void EnablePlugin(LoadedPluginInfo plugin)
    {
        plugin.Strategy.IsEnabled = true;
    }

    [RelayCommand]
    private void DisablePlugin(LoadedPluginInfo plugin)
    {
        plugin.Strategy.IsEnabled = false;
    }
}
```

### 8.2 View

```xml
<UserControl xmlns="..." x:Class="..." x:DataType="vm:PluginManagementViewModel">
    <DockPanel Margin="16">
        <StackPanel DockPanel.Dock="Top" Spacing="8" Margin="0,0,0,12">
            <TextBlock Text="插件管理" FontSize="20" FontWeight="Bold"/>
            <Button Content="刷新" Command="{Binding RefreshCommand}" HorizontalAlignment="Left"/>
        </StackPanel>

        <ListBox ItemsSource="{Binding Plugins}" SelectedItem="{Binding SelectedPlugin}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid ColumnDefinitions="Auto,*,Auto,Auto" Margin="4" ColumnSpacing="8">
                        <CheckBox IsChecked="{Binding Strategy.IsEnabled}" Grid.Column="0"/>
                        <StackPanel Grid.Column="1">
                            <TextBlock Text="{Binding Manifest.Name}" FontWeight="Bold"/>
                            <TextBlock Text="{Binding Manifest.Description}" FontSize="11" Foreground="Gray"/>
                        </StackPanel>
                        <TextBlock Text="{Binding Manifest.Version}" Grid.Column="2"/>
                        <TextBlock Text="{Binding Manifest.Type}" Grid.Column="3"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <TextBlock DockPanel.Dock="Bottom" Text="{Binding StatusMessage}" FontStyle="Italic"/>
    </DockPanel>
</UserControl>
```

---

## 第九步：完成 View 的 .axaml 文件

当前 Views 目录下 5 个视图仅有 `.axaml.cs` 没有 `.axaml`。需要创建 `.axaml` 并删除或清空原有的占位 `.axaml.cs`：

| 文件 | 操作 |
|------|------|
| `Views/DataManagementView.axaml` | 新建（内容见第三步） |
| `Views/VenueConfigurationView.axaml` | 新建（内容见第四步） |
| `Views/StrategyConfigurationView.axaml` | 新建（内容见第五步） |
| `Views/SeatingArrangementView.axaml` | 新建（内容见第六步） |
| `Views/SnapshotHistoryView.axaml` | 新建（内容见第七步） |
| `Views/PluginManagementView.axaml` | 新建（内容见第八步） |

所有 `.axaml.cs` 代码后置只需 `InitializeComponent()`，无其他逻辑。

---

## 第十步：清理占位文件

1. 删除或清空 `PlaceholderViewModels.cs`
2. 删除 `MainShellViewModel.cs` 的旧内容（已在第二步重写）
3. 删除 `MainWindowViewModel.cs` 或保留为空（主窗口已改用 MainShellViewModel）

---

## 实现顺序建议

按依赖关系，建议实现顺序如下：

```
1. 项目引用 + DI      (30 分钟)  → 可编译，能启动空窗口
2. 导航 + 主窗口壳     (1 小时)    → 带侧边栏的窗口可切换页面（显示空白页）
3. 数据管理页         (2 小时)    → 可导入/导出学生数据
4. 会场配置页         (2 小时)    → 可设计布局、预览座位
5. 策略配置页         (1.5 小时)  → 可启用/禁用/排序策略
6. 座位安排页         (4 小时)    → 核心：生成座位、可视化、拖拽
7. 历史快照页         (1 小时)    → 浏览和回滚快照
8. 插件管理页         (1 小时)    → 查看和管理插件
9. 清理 & 打磨        (2 小时)    → 样式统一、错误处理、国际化占位
```

总计约 **15-20 小时**的编码工作量。

---

## 关键技术要点

### CommunityToolkit.Mvvm 规范

- ViewModel 继承 `ViewModelBase`（已继承 `ObservableObject`）
- 属性用 `[ObservableProperty]` 标注（field 用 `_camelCase`），生成器自动创建 `PascalCase` 属性
- 命令用 `[RelayCommand]` 标注，方法名以动词结尾
- 不要混用 ReactiveUI

### 文件对话框

Avalonia 使用 `IStorageProvider` 而非传统 `OpenFileDialog`。需要在 View 层获取 `TopLevel`：

```csharp
var topLevel = TopLevel.GetTopLevel(this); // 在 View code-behind 中
var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
```

最佳实践：创建 `IDialogService` 在 View 层实现，注入到 ViewModel 中。也可以用 `CommunityToolkit.Mvvm` 的 messenger 模式。

### 座位坐标计算

使用 `SeatGeometryHelper.GetPosition(seat, metadata)` 获取座位的物理坐标 `(X, Y)`。需要先通过 `IVenueRepository` 或从布局的 `Metadata` 属性获取正确的 `LayoutMetadata`。

### 错误处理

所有 IApplicationFacade 方法调用都应 try-catch，将错误信息显示在 `StatusMessage` 中。避免未捕获异常导致应用崩溃。

### ViewLocator

当前 `ViewLocator.cs` 通过反射将 `XXXViewModel` 映射到 `XXXView`。确认命名空间一致：
- ViewModels: `A_Pair.Presentation.Avalonia.ViewModels`
- Views: `A_Pair.Presentation.Avalonia.Views`

映射逻辑：`replace("ViewModel", "View")` — 要求 View 文件名与 ViewModel 严格对应。

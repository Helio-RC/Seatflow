using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace A_Pair.Presentation.Avalonia.ViewModels
{
    // Lightweight presentation models to avoid tight coupling to domain models in the UI layer.
    public class PresentationStudent : ObservableObject
    {
        private string _id = string.Empty;
        public string Id { get => _id; set => SetProperty(ref _id , value); }

        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name , value); }

        private string? _gender;
        public string? Gender { get => _gender; set => SetProperty(ref _gender , value); }

        private float? _height;
        public float? Height { get => _height; set => SetProperty(ref _height , value); }

        private bool _needsFrontRow;
        public bool NeedsFrontRow { get => _needsFrontRow; set => SetProperty(ref _needsFrontRow , value); }

        private string? _deskGroupId;
        public string? DeskGroupId { get => _deskGroupId; set => SetProperty(ref _deskGroupId , value); }
    }

    public class StrategyListItem : ObservableObject
    {
        public bool IsEnabled { get; set; }
        public int Priority { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        // For configuration host - could be a View/ViewModel in real app
        public object? ConfigurationView { get; set; }
    }

    public class SnapshotListItem : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int AssignedCount { get; set; }
    }

    public class PluginListItem : ObservableObject
    {
        public bool IsEnabled { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string? Description { get; set; }
        public string? ScriptFile { get; set; }
    }

    public class DataManagementViewModel : ViewModelBase
    {
        public ObservableCollection<PresentationStudent> Students { get; } = new();
        public PresentationStudent? SelectedStudent { get; set; }
        public ObservableCollection<string> GenderOptions { get; } = new() { "男" , "女" , "其他" };

        public string SourcePath { get; set; } = string.Empty;
        public string SummaryText => $"总人数 {Students.Count}";

        public IRelayCommand ImportCommand { get; }
        public IRelayCommand ExportCommand { get; }
        public IRelayCommand ValidateCommand { get; }
        public IRelayCommand ClearCommand { get; }
        public IRelayCommand BrowseSourceCommand { get; }

        public DataManagementViewModel ()
        {
            ImportCommand = new RelayCommand(() => { /* placeholder */ });
            ExportCommand = new RelayCommand(() => { /* placeholder */ });
            ValidateCommand = new RelayCommand(() => { /* placeholder */ });
            ClearCommand = new RelayCommand(() => Students.Clear());
            BrowseSourceCommand = new RelayCommand(() => { /* placeholder */ });

            // sample data for designer/runtime
            Students.Add(new PresentationStudent { Id = "S001" , Name = "张三" , Gender = "男" , Height = 175 });
            Students.Add(new PresentationStudent { Id = "S002" , Name = "李四" , Gender = "女" , Height = 162 , NeedsFrontRow = true });
        }
    }

    public class VenueConfigurationViewModel : ViewModelBase
    {
        public ObservableCollection<string> LayoutTypes { get; } = new() { "Grid" , "Polar" , "Freeform" };
        public string SelectedLayoutType { get; set; } = "Grid";
        public int Rows { get; set; } = 6;
        public int Columns { get; set; } = 8;
        public int SeatSpacing { get; set; } = 60;

        public IRelayCommand SaveVenueCommand { get; }
        public IRelayCommand TestLayoutCommand { get; }

        // preview canvas attached by view
        private A_Pair.Presentation.Avalonia.Controls.SeatCanvas? _previewCanvas;
        public void AttachPreviewCanvas (A_Pair.Presentation.Avalonia.Controls.SeatCanvas canvas)
        {
            _previewCanvas = canvas;
        }

        public VenueConfigurationViewModel ()
        {
            SaveVenueCommand = new RelayCommand(() => { /* placeholder save */ });
            TestLayoutCommand = new RelayCommand(() =>
            {
                // generate simple grid preview and render to attached preview canvas
                var seats = new System.Collections.Generic.List<A_Pair.Presentation.Avalonia.Controls.PresentationSeat>();
                for (int r = 1; r <= Rows; r++)
                {
                    for (int c = 1; c <= Columns; c++)
                    {
                        seats.Add(new A_Pair.Presentation.Avalonia.Controls.PresentationSeat
                        {
                            Id = $"R{r}C{c}" ,
                            Row = r ,
                            Column = c
                        });
                    }
                }

                _previewCanvas?.RenderPresentationSeats(seats);
            });
        }
    }

    public class StrategyConfigurationViewModel : ViewModelBase
    {
        public ObservableCollection<StrategyListItem> Strategies { get; } = new();
        public StrategyListItem? SelectedStrategy { get; set; }

        public IRelayCommand MoveUpCommand { get; }
        public IRelayCommand MoveDownCommand { get; }
        public IRelayCommand ConfigureCommand { get; }
        public IRelayCommand RemoveCommand { get; }

        public StrategyConfigurationViewModel ()
        {
            MoveUpCommand = new RelayCommand(() =>
            {
                if (SelectedStrategy == null) return;
                var idx = Strategies.IndexOf(SelectedStrategy);
                if (idx > 0)
                {
                    Strategies.Move(idx , idx - 1);
                    RecalculatePriorities();
                }
            });
            MoveDownCommand = new RelayCommand(() =>
            {
                if (SelectedStrategy == null) return;
                var idx = Strategies.IndexOf(SelectedStrategy);
                if (idx >= 0 && idx < Strategies.Count - 1)
                {
                    Strategies.Move(idx , idx + 1);
                    RecalculatePriorities();
                }
            });
            ConfigureCommand = new RelayCommand(() => { /* placeholder */ });
            RemoveCommand = new RelayCommand(() => { if (SelectedStrategy != null) Strategies.Remove(SelectedStrategy); RecalculatePriorities(); });

            // sample
            Strategies.Add(new StrategyListItem { IsEnabled = true , Priority = 10 , Name = "FixedSeatStrategy" , Status = "已配置" });
            Strategies.Add(new StrategyListItem { IsEnabled = true , Priority = 30 , Name = "FrontRowRotationStrategy" , Status = "已配置" });
        }

        private void RecalculatePriorities ()
        {
            for (int i = 0; i < Strategies.Count; i++)
            {
                Strategies[i].Priority = (i + 1) * 10;
            }
        }
    }

    public class SeatingArrangementViewModel : ViewModelBase
    {
        private readonly A_Pair.Application.Interfaces.IApplicationFacade _facade;
        private bool _canvasHooked = false;
        private A_Pair.Presentation.Avalonia.Controls.SeatCanvas? _attachedCanvas;

        public A_Pair.Application.Interfaces.IApplicationFacade Facade => _facade;

        public ObservableCollection<PresentationStudent> UnassignedStudents { get; } = new();
        public PresentationStudent? SelectedUnassignedStudent { get; set; }
        public string SelectedSeatInfo { get; set; } = string.Empty;

        public IAsyncRelayCommand GenerateSeatingCommand { get; }
        public IRelayCommand StartRotationCommand { get; }
        public IRelayCommand ClearAssignmentsCommand { get; }
        public IRelayCommand ExportViewCommand { get; }
        public IRelayCommand ZoomInCommand { get; }
        public IRelayCommand ZoomOutCommand { get; }
        public IRelayCommand ShowStudentHistoryCommand { get; }

        public SeatingArrangementViewModel (A_Pair.Application.Interfaces.IApplicationFacade facade)
        {
            _facade = facade;

            GenerateSeatingCommand = new AsyncRelayCommand(GenerateSeatingAsync);
            StartRotationCommand = new RelayCommand(() => { /* placeholder */ });
            ClearAssignmentsCommand = new RelayCommand(() => { /* placeholder */ });
            ExportViewCommand = new RelayCommand(() => { /* placeholder */ });
            ZoomInCommand = new RelayCommand(() => { /* placeholder */ });
            ZoomOutCommand = new RelayCommand(() => { /* placeholder */ });
            ShowStudentHistoryCommand = new RelayCommand(() => { /* placeholder */ });

            // sample unassigned
            UnassignedStudents.Add(new PresentationStudent { Id = "S010" , Name = "赵六" });
            UnassignedStudents.Add(new PresentationStudent { Id = "S011" , Name = "孙七" });
        }

        private async Task GenerateSeatingAsync ()
        {
            // use facade to generate seating if available
            try
            {
                var plan = await _facade.GenerateSeatingAsync(new A_Pair.Application.Interfaces.SeatingRequest());
                if (_attachedCanvas != null && plan != null)
                {
                    _attachedCanvas.RenderSeats(plan.FindSeats(s => true));
                }
            }
            catch
            {
                await Task.CompletedTask;
            }
        }

        public void AttachCanvas (A_Pair.Presentation.Avalonia.Controls.SeatCanvas canvas)
        {
            _attachedCanvas = canvas;
            // hook zoom commands to attached canvas
            ZoomInCommand = new RelayCommand(() => _attachedCanvas?.ZoomIn());
            ZoomOutCommand = new RelayCommand(() => _attachedCanvas?.ZoomOut());
        }

        public async System.Threading.Tasks.Task RefreshSeatsAsync (A_Pair.Presentation.Avalonia.Controls.SeatCanvas canvas)
        {
            if (canvas == null) return;

            var ws = await _facade.GetCurrentWorkspaceAsync();
            if (ws != null)
            {
                canvas.RenderSeats(ws.FindSeats(s => true));
            }

            if (!_canvasHooked)
            {
                _canvasHooked = true;
                canvas.OnSeatAssigned += async (targetSeatId , sourceSeatId) =>
                {
                    if (string.IsNullOrEmpty(sourceSeatId) || string.IsNullOrEmpty(targetSeatId)) return;
                    var workspace = await _facade.GetCurrentWorkspaceAsync();
                    if (workspace == null) return;

                    var source = workspace.FindSeats(s => s.Id == sourceSeatId).FirstOrDefault();
                    var studentId = source?.OccupantId;
                    if (string.IsNullOrEmpty(studentId)) return;

                    var cmd = new A_Pair.Application.Commands.AssignSeatCommand(targetSeatId , studentId);
                    var ok = await _facade.ExecuteCommandAsync(cmd);
                    if (ok)
                    {
                        // refresh view
                        var ws2 = await _facade.GetCurrentWorkspaceAsync();
                        if (ws2 != null) canvas.RenderSeats(ws2.FindSeats(s => true));
                    }
                };
            }
        }
    }

    public class SnapshotHistoryViewModel : ViewModelBase
    {
        public ObservableCollection<SnapshotListItem> Snapshots { get; } = new();
        public SnapshotListItem? SelectedSnapshot { get; set; }
        public string SelectedSnapshotSummary { get; set; } = string.Empty;

        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand CompareCommand { get; }
        public IRelayCommand ApplySnapshotCommand { get; }

        public SnapshotHistoryViewModel ()
        {
            RefreshCommand = new RelayCommand(() => { /* placeholder refresh */ });
            CompareCommand = new RelayCommand(() => { /* placeholder compare */ });
            ApplySnapshotCommand = new RelayCommand(() => { /* placeholder apply */ });

            Snapshots.Add(new SnapshotListItem { Id = "1" , CreatedAt = "2026-04-15 10:23" , Description = "月考座位表" , AssignedCount = 42 });
        }
    }

    public class PluginManagementViewModel : ViewModelBase
    {
        public ObservableCollection<PluginListItem> Plugins { get; } = new();
        public PluginListItem? SelectedPlugin { get; set; }
        public string? PluginError { get; set; }

        public IRelayCommand InstallPluginCommand { get; }
        public IRelayCommand ScanCommand { get; }
        public IRelayCommand EnableAllCommand { get; }
        public IRelayCommand DisableAllCommand { get; }
        public IRelayCommand EditScriptCommand { get; }
        public IRelayCommand ReloadPluginCommand { get; }

        public PluginManagementViewModel ()
        {
            InstallPluginCommand = new RelayCommand(() => { /* placeholder */ });
            ScanCommand = new RelayCommand(() => { /* placeholder */ });
            EnableAllCommand = new RelayCommand(() => { foreach (var p in Plugins) p.IsEnabled = true; });
            DisableAllCommand = new RelayCommand(() => { foreach (var p in Plugins) p.IsEnabled = false; });
            EditScriptCommand = new RelayCommand(() => { /* placeholder */ });
            ReloadPluginCommand = new RelayCommand(() => { /* placeholder */ });

            Plugins.Add(new PluginListItem { IsEnabled = true , Name = "视力优先策略" , Version = "1.0.0" , Type = "Lua脚本" , Priority = 25 , Description = "根据学生视力需求优先安排前排座位" });
        }
    }
}
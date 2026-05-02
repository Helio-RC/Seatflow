# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Chat & Coding Language

你需要使用中文来进行对话和思考、编写注释。

## Build & Test

```bash
dotnet build                    # Build all 9 projects (uses .slnx, requires .NET 10 SDK)
dotnet test                     # Run all tests (xUnit v3, Microsoft.Testing.Platform)
dotnet test --filter "FullyQualifiedName~TestName"  # Run a single test
dotnet run --project A_Pair.Presentation.Avalonia   # Launch the desktop app
```

**Test stack**: xUnit v3 + FluentAssertions + NSubstitute. Tests are in 3 projects: `*.Core.Tests`, `*.Application.Tests`, `*.Infrastructure.Tests`. Each test project has a `Usings.cs` with shared global usings (`System`, `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks`).

**No `Directory.Build.props` or `Directory.Packages.props`** — package versions are managed directly in each `.csproj`.

## Architecture

A_Pair is a .NET 10 cross-platform desktop seating arrangement system using Avalonia UI 12 (MVVM) + CommunityToolkit.Mvvm 8.4. The solution file is `A_Pair.slnx` (the new XML-based format).

**Layers (bottom-up)**:
- **Core** — Domain entities (`Student`, `Seat`, `ClassroomLayoutDefinition`, `SeatingWorkspace`), strategy interfaces (`ISeatingStrategy`), domain services (`ObstacleProcessor`, `SeatGeometryHelper`), and data provider interfaces (`IStudentProvider`, `IVenueRepository`, etc.)
- **Contracts** — Cross-layer interface for plugins (`IPluginSeatingStrategy`)
- **Infrastructure** — File I/O (`CsvStudentProvider`, `XlsxStudentProvider`, `JsonStudentProvider`), exporters (`ExcelSeatingExporter`, `CsvSeatingExporter`, `PdfSeatingExporter`), layout builders (`GridLayoutBuilder`, `PolarLayoutBuilder`, `FreeformLayoutBuilder`), repositories, serialization
- **Application** — `IApplicationFacade` (UI's single entry point), `StrategyExecutionPipeline`, command pattern (`IUndoableCommand` / `CommandHistory`), plugin manager (`PluginManager`, `PluginLoadContext`), script adapters (Lua/C#), DI registration
- **Plugins.Sdk** — Lightweight assembly for external plugin authors
- **Presentation.Avalonia** — Avalonia 12 desktop app, MVVM with CommunityToolkit.Mvvm

**DI**: `ServiceCollectionExtensions.AddA_PairApplication(snapshotBasePath, pluginsPath)` in Application layer registers all services (strategies, exporters, providers, repositories, plugin manager). UI calls it in `Program.cs`, then adds its own services (navigation, ViewModels, Views).

**Navigation**: `INavigationService` + `MainShellViewModel` manages 8 pages via `PageKey` enum (`DataManagement`, `VenueConfiguration`, `StrategyConfiguration`, `SeatingArrangement`, `SnapshotHistory`, `PluginManagement`, `Settings`, `About`). `ViewLocator` auto-resolves `XXXViewModel` → `XXXView` by convention: replaces `"ViewModel"` with `"View"` in the type name via reflection.

**Project config**: `AvaloniaUseCompiledBindingsByDefault` is `true` in the Avalonia csproj — all bindings are compiled unless explicitly opted out.

## Key Patterns

### CommunityToolkit.Mvvm Source Generators
- `[ObservableProperty]` on a private field generates a public property with `On<PropertyName>Changed` partial method hooks
- `[RelayCommand]` on a method generates an `ICommand` property
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` triggers change notification for dependent properties
- All ViewModels inherit `ViewModelBase` (extends `ObservableObject`)

### ViewModelBase.SafeExecuteAsync
```csharp
protected async Task<bool> SafeExecuteAsync(Func<Task> action, string errorTitle = "操作失败")
```
Wraps async operations in try-catch and shows error dialogs automatically. Use this in ViewModels for user-facing operations.

### Adding a New Page
1. Add a new value to `PageKey` enum in `INavigationService.cs`
2. Create `ViewModels/NewThingViewModel.cs` (inherit `ViewModelBase`)
3. Create `Views/NewThingView.axaml` + `.axaml.cs` (set `x:DataType="vm:NewThingViewModel"`)
4. Register both in `Program.cs`: `services.AddSingleton<NewThingViewModel>()`
5. Add navigation button in `MainWindow.axaml` sidebar

### Axaml Bindings
- Always use `x:DataType` on the root element for compiled bindings
- Icons: `<fic:FluentIcon Icon="{x:Static ficEnum:Icon.{Name}}" FontSize="18"/>` (see `Fluent_Icons.md`)
- Converters: `BoolConverters.cs` (Negate, TrueWhenNull, etc.) and `ValueConverters.cs`

### Sidebar
- Width: 140px expanded / 64px collapsed (controlled by `MainShellViewModel.SidebarWidth`)
- Auto-collapses when window width < 750px
- `MainShellViewModel.ToggleSidebar()` command for manual toggle

## Documents
- `Goal.md` — Project goals & architecture design
- `Phases.md` — Implementation phases & detailed planning
- `A_Pair.Presentation.Avalonia/Develop handoff.md` — Developer handoff guide
- `A_Pair.Presentation.Avalonia/How_to_Design_UI.md` — UI implementation guide (step by step, in Chinese)
- `A_Pair.Presentation.Avalonia/Design_Spec.md` — FluentUI design spec (colors, typography, spacing, icons)
- `A_Pair.Presentation.Avalonia/Fluent_Icons.md` — All FluentUI icon names in use

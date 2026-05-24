# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Chat & Coding Language

你需要使用中文来进行对话和思考、编写注释。

## Development Environment

在执行需要 GUI 的操作前，先确认当前是否处于无头环境（检查 `DISPLAY` / `WAYLAND_DISPLAY` 等环境变量），以及是否安装了 .NET 10 SDK。avdt（Avalonia DevTools）仅在桌面环境下可用。

##  Build & Test

```bash
dotnet build                    # Build all 9 projects (uses .slnx, requires .NET 10 SDK)
dotnet test                     # Run all tests (xUnit v3, Microsoft.Testing.Platform)
dotnet test --filter "FullyQualifiedName~TestName"  # Run a single test
dotnet run --project A_Pair.Presentation.Avalonia   # Launch the desktop app
```

**Test stack**: xUnit v3 + FluentAssertions + NSubstitute. Tests are in 3 projects: `*.Core.Tests`, `*.Application.Tests`, `*.Infrastructure.Tests`. Each has `<ImplicitUsings>enable</ImplicitUsings>` (provides `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks`). Project-specific global usings are in `Usings.cs` (or `Using.cs` in Application.Tests).

**No `Directory.Build.props` or `Directory.Packages.props`** — package versions are managed directly in each `.csproj`.

**dotnet tools**: `avaloniaui.developertools` (avdt) is installed in repo-root `dotnet-tools.json`. 使用前先确认当前环境是否有桌面显示支持，若无头则跳过 avdt。构建时无需此工具。

## Architecture

A_Pair is a .NET 10 cross-platform desktop seating arrangement system using Avalonia UI 12 (MVVM) + CommunityToolkit.Mvvm 8.4. The solution file is `A_Pair.slnx` (the new XML-based format).

**Layers (bottom-up)**:
- **Core** — Domain entities (`Student`, `Seat`, `ClassroomLayoutDefinition`, `SeatingWorkspace`, `SeatingPlan`), strategy interfaces (`ISeatingStrategy`) + four built-in implementations, domain services (`ObstacleProcessor`, `SeatGeometryHelper`, `StrategyManifestProvider`), and data provider interfaces (`IStudentProvider`, `IVenueRepository`, etc.)
- **Contracts** — Cross-layer interface for plugins (`IPluginSeatingStrategy`)
- **Infrastructure** — File I/O (`CsvStudentProvider`, `XlsxStudentProvider`, `JsonStudentProvider`, and the composite `CompositeStudentProvider` registered as the primary `IStudentProvider`), exporters (`ExcelSeatingExporter`, `CsvSeatingExporter`, `PdfSeatingExporter`, `ImageSeatingExporter`), layout builders (`GridLayoutBuilder`, `PolarLayoutBuilder`, `FreeformLayoutBuilder`), repositories (`JsonVenueRepository`, `JsonAppSettingsRepository`, `StrategyConfigFileRepository`, `SeatingSnapshotRepository`, `JsonStudentDatasetRepository`), writers (`JsonStudentWriter`, `CsvStudentWriter`, `XlsxStudentWriter`), serialization
- **Application** — `IApplicationFacade` (UI's single entry point), `StrategyExecutionPipeline`, command pattern (`IUndoableCommand` / `CommandHistory`), plugin manager (`PluginManager`, `PluginLoadContext`), script adapters (Lua/C#), DI registration
- **Plugins.Sdk** — Lightweight assembly for external plugin authors
- **Presentation.Avalonia** — Avalonia 12 desktop app, MVVM with CommunityToolkit.Mvvm

**DI**: `ServiceCollectionExtensions.AddA_PairApplication(snapshotBasePath, pluginsPath)` in Application layer registers all services (strategies, exporters, providers, repositories, plugin manager). In `Program.cs`, the UI layer calls this then adds its own singletons: `INavigationService`, `IFileService`, `IDialogService`, `MainWindow`, `MainShellViewModel`, and all page ViewModels.

**Navigation**: `INavigationService` + `MainShellViewModel` manages 10 pages via `PageKey` enum (`Home`, `DataManagement`, `VenueConfiguration`, `FreeformManagement`, `StrategyConfiguration`, `SeatingArrangement`, `SnapshotHistory`, `PluginManagement`, `Settings`, `About`). `ViewLocator` auto-resolves `XXXViewModel` → `XXXView` by convention: replaces `"ViewModel"` with `"View"` in the type name via reflection.

**Project dependency chain**: `Presentation.Avalonia` → `Application` → (`Core`, `Contracts`, `Infrastructure`). `Plugins.Sdk` is referenced only by external plugins. `Application` orchestrates; `Infrastructure` implements providers/exporters/layouts/repos; `Core` owns entities, strategy interfaces, and the workspace.

**Project config**: `AvaloniaUseCompiledBindingsByDefault` is `true` in the Avalonia csproj — all bindings are compiled unless explicitly opted out.

**App startup sequence** (`App.axaml.cs` `OnFrameworkInitializationCompleted`):
1. Resolve `MainShellViewModel` and `MainWindow` from DI, wire DataContext
2. Call `IFileService.SetTopLevel()` and `IDialogService.SetTopLevel()` with MainWindow
3. Initialize `ViewModelBase.Dialog` (static) and `ViewModelBase` logger
4. Start `WatchdogService` (prevents UI freeze from blocking exit) with a 3s DispatcherTimer ping
5. Attach `ChineseInputNormalizer` behavior (全角数字/符号 → 半角)
6. Restore saved settings (theme, window position/size) via `RestoreSettingsAsync()`

## Key Patterns

### CommunityToolkit.Mvvm Source Generators
- `[ObservableProperty]` on a private field generates a public property with `On<PropertyName>Changed` partial method hooks
- `[RelayCommand]` on a method generates an `ICommand` property
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` triggers change notification for dependent properties
- All ViewModels inherit `ViewModelBase` (extends `ObservableObject`)

### ViewModelBase.SafeExecuteAsync

Two overloads:

```csharp
// Simple: try-catch, auto error dialog
protected async Task<bool> SafeExecuteAsync(Func<Task> action, string errorTitle = "操作失败")

// With timeout: auto-cancels via CancellationTokenSource, shows timeout dialog
protected async Task<bool> SafeExecuteAsync(Func<CancellationToken, Task> action, TimeSpan timeout, string errorTitle = "操作失败")
```

The timeout overload aborts the operation when exceeded — prefer it for long-running exports or imports. Keep the timeout well under the WatchdogService threshold (45s).

`ViewModelBase` uses a static `IDialogService` — `App.axaml.cs` must call `ViewModelBase.InitializeDialogService(dialog)` at startup before any ViewModel uses `SafeExecuteAsync`. **If you add a new window or test ViewModels in isolation, Dialog must be initialized first.**

### ViewModelBase.CanLeaveAsync
```csharp
public virtual Task<bool> CanLeaveAsync()
```
Called by `NavigationService` before navigating away. Override to prompt user about unsaved changes.

### Theme & Custom Resources
- **Theme dictionaries**: App.axaml defines `ResourceDictionary.ThemeDictionaries` with Light/Dark variants for sidebar colors, semantic colors (Success/Warning/Error/Info), surface colors, and shadows.
- **Brushes**: SolidColorBrush resources reference theme colors via `DynamicResource`. Always use these brush resources (e.g., `{StaticResource SuccessBrush}`) — never hardcode hex colors.
- **Style includes**: `Typography.axaml`, `Spacing.axaml`, `Colors.axaml` are included after FluentTheme.
- **Font**: Global `Window` style sets `FontFamily` to `Inter,Microsoft YaHei UI,PingFang SC,Noto Sans CJK SC,WenQuanYi Micro Hei,sans-serif` for CJK support.
- **BoxShadows**: `CardShadowNone`, `CardShadowLarge`, `CardShadowSmall` are defined as `BoxShadows` resources.

### UI Services (`A_Pair.Presentation.Avalonia/Services/`)
- **INavigationService** — Page switching with `PageKey` enum. `NavigateTo()` is synchronous, `NavigateToAsync()` runs `CanLeaveAsync()` first.
- **IDialogService** — Shows error/info dialogs. Requires `SetTopLevel(TopLevel)` before use.
- **IFileService** — File open/save pickers. Also requires `SetTopLevel()`.
- **WatchdogService** — Detects UI thread hangs via a background poll loop. Default timeout is 45 seconds; on expiry it dumps thread/process diagnostics to `err_<timestamp>.log` and force-exits the app. UI thread must call `Ping()` regularly (a `DispatcherTimer` does this in `App.axaml.cs`).

### Utility Windows
- `InputWindow` — modal dialog for single-line text input (returns the entered string)
- `DialogWindow` — general-purpose modal content host with title bar and close button

### Behaviors (`A_Pair.Presentation.Avalonia/Behaviors/`)
- `CanvasZoomPan` — Pan and zoom for Canvas-based previews
- `ZoomOnScroll` — Ctrl+Scroll to zoom
- `ChineseInputNormalizer` — Converts full-width numbers/symbols to half-width on text input

### Adding a New Page
1. Add a new value to `PageKey` enum in `INavigationService.cs`
2. Create `ViewModels/NewThingViewModel.cs` (inherit `ViewModelBase`)
3. Create `Views/NewThingView.axaml` + `.axaml.cs` (set `x:DataType="vm:NewThingViewModel"`)
4. Register both in `Program.cs`: `services.AddSingleton<NewThingViewModel>()`
5. Add navigation button in `MainWindow.axaml` sidebar

### Axaml Bindings
- Always use `x:DataType` on the root element for compiled bindings
- Icons: `<fic:FluentIcon Icon="{x:Static ficEnum:Icon.{Name}}" FontSize="18"/>` (see `A_Pair.Presentation.Avalonia/docs/Fluent_Icons.md`)
- Converters: `BoolConverters.cs` (Negate, TrueWhenNull, etc.) and `ValueConverters.cs`

### Sidebar
- Width: 140px expanded / 64px collapsed (controlled by `MainShellViewModel.SidebarWidth`)
- Auto-collapses when window width < 750px
- `MainShellViewModel.ToggleSidebar()` command for manual toggle

## File Versions & Migration

All persisted JSON files carry a `version` field. Current versions are defined in `A_Pair.Infrastructure/Migration/file_versions.json` (embedded resource, compiled into the assembly).

| File type | Version | Location |
|---|---|---|
| Venue | `1.1` | `{data}/Venues/*.venue.json` |
| Roster | `1.0` | `{data}/Rosters/*.roster.json` |
| Snapshot | `1.0` | `{data}/Assignments/{venueId}/{date}/*.json` |
| VenueInfo | `1.0` | `{data}/Assignments/{venueId}/_venue.json` |
| AppSettings | `1.0` | `{data}/AppSettings.json` |
| StrategyConfig | `1.0` | `{data}/StrategyConfig/*.config.json` |

On load, `FileMigrationService` checks the file version and runs registered `IFileMigrator` implementations to migrate old formats forward. Migration is **forward-only** — no version rollback.

To add a migration for a breaking format change:
1. Implement `IFileMigrator` (`FileType`, `FromVersion`, `ToVersion`, `Migrate(JsonNode)`)
2. Register in `ServiceCollectionExtensions.cs`
3. Bump the version in `file_versions.json`
4. Update the model's default `Version` property

Existing migrators:
- `VenueFileMigrator_1_0_to_1_1`: Reorders Grid layout seats from column-major to row-major

## Documents
- `ARCHITECTURE.md` — Project goals & architecture design
- `Phases.md` — Implementation phases & detailed planning
- `A_Pair.Presentation.Avalonia/docs/Design_Spec.md` — FluentUI design spec (colors, typography, spacing, icons)
- `A_Pair.Presentation.Avalonia/docs/Fluent_Icons.md` — All FluentUI icon names in use

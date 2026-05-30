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

**Navigation**: `INavigationService` + `MainShellViewModel` manages 10 pages via `PageKey` enum (`Home`, `MemberManagement`, `VenueConfiguration`, `FreeformManagement`, `StrategyConfiguration`, `SeatingArrangement`, `SnapshotHistory`, `PluginManagement`, `Settings`, `About`). `ViewLocator` auto-resolves `XXXViewModel` → `XXXView` by convention: replaces `"ViewModel"` with `"View"` in the type name via reflection.

**Project dependency chain**: `Presentation.Avalonia` → `Application` → (`Core`, `Contracts`, `Infrastructure`). `Plugins.Sdk` is referenced only by external plugins. `Application` orchestrates; `Infrastructure` implements providers/exporters/layouts/repos; `Core` owns entities, strategy interfaces, and the workspace.

**Project config**: `AvaloniaUseCompiledBindingsByDefault` is `true` in the Avalonia csproj — all bindings are compiled unless explicitly opted out.

**App startup sequence**:
1. `App.Initialize()` — `ApplyLanguageFromSettings()` sets `CurrentUICulture` + `Resources.Culture`, then `AvaloniaXamlLoader.Load(this)` (language MUST be set before XAML loading so `{x:Static}` resolves correctly)
2. `OnFrameworkInitializationCompleted` — Resolve `MainShellViewModel`/`MainWindow` from DI, wire DataContext
3. Call `IFileService.SetTopLevel()` and `IDialogService.SetTopLevel()` with MainWindow
4. Initialize `ViewModelBase.Dialog` (static) and `ViewModelBase` logger
5. Start `WatchdogService` with a 3s DispatcherTimer ping
6. Attach `ChineseInputNormalizer` behavior (全角数字/符号 → 半角)
7. `RestoreSettingsAsync()` — restore theme, window position/size (language already applied in step 1)

## Key Patterns

### CommunityToolkit.Mvvm Source Generators
- `[ObservableProperty]` on a private field generates a public property with `On<PropertyName>Changed` partial method hooks
- `[RelayCommand]` on a method generates an `ICommand` property
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` triggers change notification for dependent properties
- All ViewModels inherit `ViewModelBase` (extends `ObservableObject`)

### ViewModelBase.SafeExecuteAsync

Two overloads:

```csharp
// Simple: try-catch, auto error dialog. errorTitle defaults to localized Resources.Common_OperationFailed
protected async Task<bool> SafeExecuteAsync(Func<Task> action, string? errorTitle = null)

// With timeout: auto-cancels via CancellationTokenSource, shows timeout dialog
protected async Task<bool> SafeExecuteAsync(Func<CancellationToken, Task> action, TimeSpan timeout, string? errorTitle = null)
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

### i18n / Localization (`Lang/`)

Uses standard .NET `.resx` resource files in `A_Pair.Presentation.Avalonia/Lang/`:
- `Resources.resx` — neutral language (zh-CN), ~570 keys
- `Resources.en-US.resx` — English satellite
- `Resources.Designer.cs` — hand-maintained typed accessor class (Visual Studio's `PublicResXFileCodeGenerator` doesn't work with `dotnet build`)

**Adding a new language**: create `Resources.xx-XX.resx` with translations, no code changes needed.

**Usage in XAML** (attribute syntax only — element content won't resolve):
```xml
<TextBlock Text="{x:Static lang:Resources.Settings_Title}" />
<Button Content="{x:Static lang:Resources.Common_OK}" />
```
Namespace: `xmlns:lang="using:A_Pair.Presentation.Avalonia.Lang"`

**Usage in C#**:
```csharp
StatusMessage = Resources.Settings_Saved;
StatusMessage = string.Format(Resources.Snapshot_VenuesLoadedFmt, count);
```
**Important**: In classes inheriting from `Window` (DialogWindow, InputWindow), `Resources` resolves to `Window.Resources` (IResourceDictionary). Use fully-qualified `Lang.Resources.xxx` in those files.

**Key naming**: `{Page}_{Element}` with PascalCase, e.g. `Settings_Title`, `Nav_Home`, `Common_OK`. Format strings use `{0}` placeholders.

**Language switching**: `App.ApplyLanguageFromSettings()` (called in `Initialize()` before XAML loading). Sets `CultureInfo.CurrentUICulture` and `Resources.Culture`.

### About Page Data (`Data/about.json`)

Multi-language JSON with top-level culture keys:
```json
{ "zh-CN": { "description": "...", "dependencies": [...] },
  "en-US": { "description": "...", "dependencies": [...] } }
```
`AboutViewModel.LoadAboutData()` selects by `CultureInfo.CurrentUICulture.Name`, falls back to `"zh-CN"`.

### Dialog Windows

- `DialogWindow` — Confirm/Error/Warning/Info/MultiOption dialogs. Buttons use `Content="{x:Static}"` attribute syntax. Code-behind only controls visibility and MultiOption custom text. **Never** use `{x:Static}` as element content inside `<Button>...</Button>`.
- `InputWindow` — Text input dialog. Same button pattern.

## File Versions & Migration

All persisted JSON files carry a `version` field. Current versions are defined in `A_Pair.Infrastructure/Migration/file_versions.json` (embedded resource, compiled into the assembly). `FileVersionInfo.GetCurrentVersion(fileType)` reads the latest version at runtime.

| File type | Version | Location | Wrapper class |
|---|---|---|---|
| Venue | `1.1` | `{data}/Venues/*.venue.json` | `VenueFile` |
| Roster | `1.0` | `{data}/Rosters/*.roster.json` | `RosterFile` |
| Snapshot | `1.0` | `{data}/Assignments/{venueId}/{date}/*.json` | `SeatingSnapshot` |
| VenueInfo | `1.0` | `{data}/Assignments/{venueId}/_venue.json` | `VenueSnapshotInfo` |
| AppSettings | `1.0` | `{data}/AppSettings.json` | `AppSettings` |
| StrategyConfig | `1.0` | `{data}/StrategyConfig/*.config.json` | `StrategyConfig` |

### Migration pipeline

On load, each repository reads the file as `JsonNode`, calls `FileMigrationService.Migrate(fileType, node, fileVersion, targetVersion)`, then deserializes. Migration is **forward-only** — no version rollback. The service finds registered `IFileMigrator` implementations and chains them by matching `FromVersion`/`ToVersion`.

### Adding a migration

1. Add a nested class in `Migration/Migrators/{FileType}Migrators.cs` (one file per file type):
   ```csharp
   public static class VenueMigrators
   {
       public sealed class Step_1_0_to_1_1 : IFileMigrator
       {
           public string FileType => "venue";
           public string FromVersion => "1.0";
           public string ToVersion => "1.1";
           public JsonNode Migrate(JsonNode root) { ... }
       }
   }
   ```
2. Register in `ServiceCollectionExtensions.cs`: `services.AddSingleton<IFileMigrator, VenueMigrators.Step_1_0_to_1_1>()`
3. Bump the version in `file_versions.json`
4. Update the model's default `Version` property in Core
5. Add tests in `Infrastructure.Tests/Migration/{FileType}MigratorsTests.cs`

### Existing migrators

- `VenueMigrators.Step_1_0_to_1_1` — Reorders Grid layout seats from column-major to row-major (sorted by `Row` then `Column`)

### JSON field conventions

- Serialization uses `JsonNamingPolicy.CamelCase` — all fields are lowercase in JSON (`row`, `column`, `layoutTypeString`, `logicalGroup`)
- `ClassroomLayoutDefinition.LayoutType` is serialized as **both** a number (`layoutType`: 0=Grid, 1=Polar, 2=Freeform) and a string (`layoutTypeString`: "Grid"/"Polar"/"Freeform"). Migrators should read `layoutTypeString` for clarity.
- `Seat` polymorphic serialization uses `SeatJsonConverter` which writes a `Type` discriminator (capital T, string: "Grid"/"Polar"/"Freeform") alongside each seat object. The `type` field (lowercase, camelCase of `SeatType` enum) is a separate integer.
- `SeatingSnapshot.Version` and `VenueSnapshotInfo.Version` were added in this migration round — old snapshots without the field default to `"1.0"`.
- `VenueFile.ContentHash` and `RosterFile.ContentHash` — SHA256 hashes computed on save (ContentHash null → serialize → hash → set → re-serialize). Student dataset hash excludes `importedAt`/`originalFileName` (unstable timestamps).

### Grid seat ordering

`GridLayoutBuilder.BuildGrid` creates seats in **row-major** order (outer loop: rows, inner loop: columns). This ensures `RandomFillStrategy` fills seats row-by-row (left-to-right, top-to-bottom). For irregular grids with `ColumnRowCounts`, `maxRows = ColumnRowCounts.Max()` and each column checks `r <= rowsForCol`.

### Snapshot venue layout embedding

Snapshots store the full `ClassroomLayoutDefinition` (JSON-serialized via `SeatJsonConverter`) in `Metadata["venueLayout"]` at creation time. The snapshot preview (`BuildPreviewAsync`) reads this embedded layout first; old snapshots without it fall back to loading the venue file. This ensures snapshots are self-contained — editing or deleting the venue file doesn't break existing snapshot previews.

### Snapshot integrity detection

`BuildPreviewAsync` compares `Metadata["venueHash"]` with the current venue file's `ContentHash`:
- **Venue deleted** → red warning bar "会场已删除，无法预览", rollback button disabled
- **Venue changed** → yellow warning bar "会场布局已更改，回滚可能失败"
- **Data changed** (student IDs missing from current datasets) → yellow bar "数据已更改", affected seats highlighted in yellow

`RollbackAsync` checks venue integrity before rolling back:
- Venue deleted → dialog → restore venue from snapshot's `venueLayout`
- Venue changed → dialog → import snapshot's venue as new venue

### Snapshot rotation

`AppSettings.MaxSnapshotsPerVenue` (default 30, 0=unlimited). After saving a snapshot, `RotateSnapshotsAsync` deletes the oldest snapshots if the count exceeds the limit. Sidebar status bar shows `"{n}/{max}"` via `SnapshotQuotaDisplay`.

### Venue editing & seat ID preservation

`VenueConfigurationViewModel` preserves seat IDs across edits: on load, records `(Row, Column) → Id` and `(Ring, Angle) → Id` maps; on save, newly-built seats match old seats by position and reuse their IDs. This prevents snapshot `SeatAssignments` from breaking after venue edits.

### Student dataset rename

`RenameStudentDatasetAsync` renames in-place (updates `RosterFile.Description` only, preserves ID). Previously it deleted the old file and created a new one with a new ID.

## Documents
- `docs/INDEX.md` — Documentation map & cross-reference (read first before modifying docs)
- `ARCHITECTURE.md` — Project goals & architecture design
- `Phases.md` — Implementation phases & detailed planning
- `CONTRIBUTING.md` — Dev environment, conventions, version migration flow
- `A_Pair.Presentation.Avalonia/docs/Design_Spec.md` — FluentUI design spec (colors, typography, spacing, icons)
- `A_Pair.Presentation.Avalonia/docs/Fluent_Icons.md` — All FluentUI icon names in use

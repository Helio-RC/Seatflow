# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test

```bash
dotnet build                    # Build all 9 projects (uses .slnx, requires .NET 10 SDK)
dotnet test                     # Run all tests (xUnit v3, Microsoft.Testing.Platform)
dotnet test --filter "FullyQualifiedName~TestName"  # Run a single test
dotnet run --project A_Pair.Presentation.Avalonia   # Launch the desktop app
```

**Test stack**: xUnit v3 + FluentAssertions + NSubstitute. Tests are in 3 projects: `*.Core.Tests`, `*.Application.Tests`, `*.Infrastructure.Tests`.

**No `Directory.Build.props` or `Directory.Packages.props`** — package versions are managed directly in each `.csproj`.

## Architecture

A_Pair is a .NET 10 cross-platform desktop seating arrangement system using Avalonia UI (MVVM) + CommunityToolkit.Mvvm.

**Layers (bottom-up)**:
- **Core** — Domain entities (`Student`, `Seat`, `ClassroomLayoutDefinition`, `SeatingWorkspace`), strategy interfaces (`ISeatingStrategy`), domain services
- **Contracts** — Cross-layer interfaces (`IPluginSeatingStrategy`)
- **Infrastructure** — File I/O, exporters (Excel/CSV/PDF), layout builders (Grid/Polar/Freeform), repositories
- **Application** — `IApplicationFacade` (UI's single entry point), strategy pipeline, command pattern (undo/redo), plugin manager
- **Presentation.Avalonia** — Avalonia 12 desktop app, MVVM with CommunityToolkit.Mvvm

**DI**: `ServiceCollectionExtensions.AddA_PairApplication()` in Application layer registers all services. UI calls it in `Program.cs`, then adds its own services (navigation, ViewModels, Views).

**Navigation**: `INavigationService` + `MainShellViewModel` manages 8 pages via `PageKey` enum. `ViewLocator` auto-resolves `XXXViewModel` → `XXXView` by convention.

**Project config**: `AvaloniaUseCompiledBindingsByDefault` is `true` in the Avalonia csproj — all bindings are compiled unless explicitly opted out.

**Key UI patterns**:
- ViewModels use `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm, inherit `ViewModelBase` (extends `ObservableObject`)
- Views use `x:DataType` for compiled bindings
- Sidebar: 200px expanded / 64px collapsed, FluentIcons.Avalonia v2.1.325 for icons
- Icons use `{x:Static ficEnum:Icon.{Name}}` syntax (see `A_Pair.Presentation.Avalonia/Fluent_Icons.md`)

**Documents**:
- `Goal.md` — Project goals & architecture design
- `Phases.md` — Implementation phases & detailed planning
- `A_Pair.Presentation.Avalonia/Develop handoff.md` — Developer handoff guide
- `A_Pair.Presentation.Avalonia/How_to_Design_UI.md` — UI implementation guide (step by step)
- `A_Pair.Presentation.Avalonia/Design_Spec.md` — FluentUI design spec (colors, typography, spacing, icons)
- `A_Pair.Presentation.Avalonia/Fluent_Icons.md` — All FluentUI icon names in use
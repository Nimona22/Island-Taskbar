# FILE_STRUCTURE.md

**Project:** IslandTaskbar  
**Document status:** Draft v0.1  
**Last updated:** 2026-11-19  
**Depends on:** `PRD.md`, `SCOPE.md`, `ARCHITECTURE.md`, `DATABASE_SCHEMA.md`, `ENV.md`, `CONVENTIONS.md`, `DECISIONS.md`, `AGENT.md`

> This document defines the planned repository and solution structure. It is a planning reference, not authorization to generate project or source files. No files are created by reading this document.
>
> Follow `AGENT.md` §4 workflow before creating any file or folder. Where this document and another project document conflict, stop and report the conflict per `AGENT.md` §16.

---

## 0. AI Agent Rules (Read First)

### The agent MUST NOT:

- Create files or folders outside the structure defined here without approval.
- Rename `IslandTaskbar` in namespaces, solution names, or project identifiers (`BRAND.md` §2; `CONVENTIONS.md` §B.1).
- Add a database, backend service, test framework, MVVM library, DI container, logging library, or any package not already approved.
- Create separate projects for each feature or layer — the initial solution is a single main project plus a future test project.
- Invent an application-data folder name or hard-code filesystem paths in project files (OD-006).
- Create spike or prototype folders without approval (OD-007).
- Treat this document as authorization to scaffold the project — the product owner must assign that task.

### The agent MUST:

- Follow the namespace mapping in Section E when naming any new file or class.
- Keep Win32 interop declarations centralized in `Adapters/Implementations/` and Win32 configuration in `win32.json` (`CONVENTIONS.md` §D.2; `ARCHITECTURE.md` §3.3).
- Store only design tokens, shared brushes, and typography in `Styles/` — not application logic.
- Keep test code in `IslandTaskbar.Tests/` and never in the main project.

---

## A. Repository Overview

The repository contains documentation, the .NET solution, and development configuration at its root. There is one source project for the application and one placeholder location for tests. Documentation lives alongside the solution, not inside it.

```
IslandTaskbar/                 ← repository root
├── docs/                      ← project documents (Markdown)
├── src/                       ← all source code
│   └── IslandTaskbar/         ← the single application project
├── tests/                     ← test projects (empty until OD-003 is resolved)
├── .gitignore
├── global.json                ← pins .NET SDK version (ENV.md §6)
└── IslandTaskbar.sln          ← Visual Studio solution file
```

**Why a single application project?**  
The Release 1 scope is a focused native desktop application. Splitting into multiple projects (Core, Adapters, UI, etc.) before the application is proven adds build complexity and forces premature interface decisions. The adapter pattern and namespace boundaries provide the required separation within one project. Additional projects are added only when a genuine need is demonstrated, such as a companion console recovery helper (if validated by SPIKE-002) or a test project.

---

## B. Proposed Solution Tree

> File names marked `†` are present from day one. Files marked `‡` are created when the feature they support begins. The tree shows Release 1 scope plus the scaffolding that must exist before any feature work starts. Release 2 and Release 3 files are described in Section H; they do not appear here as empty placeholders.

```
IslandTaskbar/
│
├── docs/
│   ├── AGENT.md †
│   ├── ARCHITECTURE.md †
│   ├── BRAND.md †
│   ├── CHANGELOG.md †
│   ├── CONVENTIONS.md †
│   ├── DATABASE_SCHEMA.md †
│   ├── DECISIONS.md †
│   ├── DESIGN_SYSTEM.md †
│   ├── ENV.md †
│   ├── FILE_STRUCTURE.md †        ← this document
│   ├── PRD.md †
│   ├── SCOPE.md †
│   ├── TASKS.md †
│   ├── TESTING.md †
│   └── USER_STORIES.md †
│
├── src/
│   └── IslandTaskbar/
│       │
│       ├── IslandTaskbar.csproj †  ← project file; pins WindowsAppSDK 2.7.0
│       ├── App.xaml †              ← application resources and startup
│       ├── App.xaml.cs †           ← application lifecycle entry point
│       ├── app.manifest †          ← dpiAwareness and execution level declarations
│       │
│       ├── Presentation/
│       │   ├── Windows/
│       │   │   ├── IslandWindow.xaml †         ← the single island/notch window
│       │   │   └── IslandWindow.xaml.cs †
│       │   │
│       │   ├── Controls/
│       │   │   ├── NotchControl.xaml ‡         ← collapsed notch surface
│       │   │   ├── NotchControl.xaml.cs ‡
│       │   │   ├── IslandSurface.xaml ‡        ← expanded island surface
│       │   │   ├── IslandSurface.xaml.cs ‡
│       │   │   ├── PinnedAppItem.xaml ‡        ← pinned application entry
│       │   │   ├── PinnedAppItem.xaml.cs ‡
│       │   │   ├── RunningWindowItem.xaml ‡    ← running window entry
│       │   │   ├── RunningWindowItem.xaml.cs ‡
│       │   │   └── ClockControl.xaml ‡         ← date and time display
│       │   │       ClockControl.xaml.cs ‡
│       │   │
│       │   └── Converters/
│       │       └── BoolToVisibilityConverter.cs ‡
│       │
│       ├── ViewModels/
│       │   ├── IslandViewModel.cs †            ← island expand/collapse state; pin and window lists
│       │   ├── NotchViewModel.cs †             ← notch visible state and product mark
│       │   └── SettingsViewModel.cs ‡          ← settings page state
│       │
│       ├── Services/
│       │   ├── TaskbarLifecycleService.cs †    ← orchestrates hide/restore and app lifetime
│       │   ├── ApplicationLauncherService.cs ‡ ← resolves and launches pinned or selected apps
│       │   ├── WindowManagerService.cs ‡       ← enumerates, filters, and activates windows
│       │   ├── PinManagerService.cs ‡          ← adds, removes, reorders, persists pins
│       │   ├── SettingsService.cs †            ← loads, validates, migrates, and saves settings
│       │   └── DisplayManagerService.cs ‡      ← monitor layout, DPI, and display-change events
│       │
│       ├── Adapters/
│       │   ├── Interfaces/
│       │   │   ├── ITaskbarVisibilityController.cs †   ← Release 1
│       │   │   ├── IWindowSystem.cs †                  ← Release 1
│       │   │   ├── IApplicationLauncher.cs †           ← Release 1
│       │   │   └── IDisplayService.cs †                ← Release 1
│       │   │
│       │   └── Implementations/
│       │       ├── Win32TaskbarVisibilityController.cs †  ← SPIKE-001 result
│       │       ├── Win32WindowSystem.cs ‡
│       │       ├── Win32ApplicationLauncher.cs ‡
│       │       └── Win32DisplayService.cs ‡
│       │
│       ├── Controllers/
│       │   └── IslandWindowController.cs †     ← D-009; OD-011 open — see Section C
│       │
│       ├── Models/
│       │   ├── PinnedApp.cs †
│       │   ├── RunningWindow.cs †
│       │   ├── IslandState.cs †                ← enum: Collapsed / Expanded
│       │   ├── Settings.cs †                   ← typed representation of settings.json
│       │   └── PinsData.cs †                   ← typed representation of pins.json
│       │
│       ├── Persistence/
│       │   ├── SettingsFileService.cs †         ← read/write settings.json atomically
│       │   ├── PinsFileService.cs ‡             ← read/write pins.json atomically
│       │   └── JsonSchemaVersion.cs †           ← shared schema-version constants and migration helpers
│       │
│       ├── Logging/
│       │   └── IAppLogger.cs †                 ← logging abstraction; implementation deferred (OD-022)
│       │
│       ├── Recovery/
│       │   └── RecoveryCoordinator.cs ‡        ← only if SPIKE-002 validates a helper is needed
│       │
│       ├── Styles/
│       │   ├── Tokens.xaml †                   ← DESIGN_SYSTEM.md color, spacing, radius tokens
│       │   ├── Typography.xaml †               ← type tokens; Segoe UI Variable references
│       │   └── Controls.xaml ‡                 ← shared control styles once controls exist
│       │
│       ├── Assets/
│       │   ├── Icons/
│       │   │   └── AppIcon.ico †               ← placeholder; replaceable (BRAND.md §9)
│       │   └── Images/
│       │       └── NotchPlaceholder.png †       ← clearly labeled placeholder (AGENT.md §8)
│       │
│       └── win32.json †                        ← CsWin32 method and type requests
│
├── tests/
│   └── IslandTaskbar.Tests/                    ← created when OD-003 is resolved
│       └── (placeholder — see Section G)
│
├── .gitignore †
├── global.json †                               ← { "sdk": { "version": "10.0.404", "rollForward": "latestPatch" } }
└── IslandTaskbar.sln †
```

---

## C. Folder Responsibilities

### `docs/`

Holds all project Markdown documents. These are the authoritative source of requirements, architecture, decisions, and conventions. No source code or build output belongs here.

Documents that do not yet exist (`TASKS.md`, `CHANGELOG.md`, `COMPONENTS.md`, `API.md`) are created only when assigned. Do not create empty placeholder files for them.

---

### `src/IslandTaskbar/`

The single application project. All production source code lives here. The structure mirrors the layer model in `ARCHITECTURE.md` §3.

---

### `App.xaml` / `App.xaml.cs`

The WinUI 3 application entry point. `App.xaml` merges the resource dictionaries from `Styles/`. `App.xaml.cs` owns application startup and shutdown, single-instance enforcement (`ARCHITECTURE.md` §12), and top-level exception handling. It does not contain product logic; that lives in `TaskbarLifecycleService`.

---

### `Presentation/Windows/`

Top-level WinUI 3 window(s). Release 1 uses a single `IslandWindow` for the notch and island surface. Additional windows (a settings flyout, a secondary panel) are added here if future decisions require them. Keep each window's code-behind thin: event routing and focus management only, no product logic (`CONVENTIONS.md` §C.4).

---

### `Presentation/Controls/`

Reusable WinUI 3 user controls and templated controls. Each control has a `.xaml` and a `.xaml.cs` pair. Code-behind is limited to view-specific behavior. Business logic belongs in view models or services. Do not place Win32 calls here.

---

### `Presentation/Converters/`

XAML value converters: types that implement `IValueConverter` or `IMultiValueConverter`. Keep each converter focused on a single transformation. Do not use converters for logic that belongs in a view model.

---

### `ViewModels/`

View model classes that hold UI state and expose commands. They call application services and implement `INotifyPropertyChanged` (or the approved equivalent once OD-002 is resolved). They must not call adapter interfaces directly and must not contain Win32 calls. Do not add new view model files for features not yet in scope.

---

### `Services/`

Application service classes that own product behavior and coordinate adapter interfaces. They implement the application-layer responsibilities listed in `ARCHITECTURE.md` §3.2. Services depend on adapter interfaces, not implementations. Services do not contain XAML, Win32 calls, or view model state.

---

### `Adapters/Interfaces/`

Narrow C# interfaces for every Windows integration point. One interface per integration responsibility, as defined in `ARCHITECTURE.md` §3.3. Only the Release 1 interfaces are present to begin with. Release 2 and Release 3 adapter interfaces are added when their release begins. No placeholder or empty interface is added speculatively.

---

### `Adapters/Implementations/`

Concrete implementations of the adapter interfaces. Each implementation contains the Win32 or WinRT calls for its responsibility. `win32.json` (CsWin32) is the source of P/Invoke declarations; no handwritten `[DllImport]` declarations appear elsewhere in the project (`CONVENTIONS.md` §D.2). Only Release 1 implementations are present to begin with.

---

### `Controllers/`

The island window controller (D-009). Its exact form, interface, and layer placement are open (OD-011). Until OD-011 is resolved, this folder exists as the agreed-upon location but `IslandWindowController.cs` must not contain Win32 windowing logic (topmost, anchoring, hit-test, display-change). Create a minimal stub only if the build requires a placeholder; mark it provisional and list it in the completion report.

The controller sits between the presentation layer and the Windows integration adapters. It is not a view model and not a service. When OD-011 is resolved, the architecture update will specify the interface and layer dependencies precisely.

---

### `Models/`

Simple data types: records, classes, and enumerations that represent the application's core domain objects. Models have no dependencies on services, adapters, or WinUI. They are shared across layers. Examples: `PinnedApp`, `RunningWindow`, `Settings`, `IslandState`. Do not place Windows API types here.

---

### `Persistence/`

The file I/O layer for local settings. Services in this folder read and write the JSON files defined in `DATABASE_SCHEMA.md` §4. All writes are atomic (write to a temp file, then replace). Files validate loaded values and fall back to safe defaults (`CONVENTIONS.md` §F; `AGENT.md` §10). Persistence services do not interact with the UI or with adapters. They depend only on `Models/`.

The application-data folder path is resolved at runtime from the Windows environment (e.g., `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)`). The folder name inside that path is not yet decided (OD-006). Do not hard-code a folder name; use a placeholder constant that is easy to replace when OD-006 is resolved.

---

### `Logging/`

The logging abstraction only. `IAppLogger.cs` defines the interface. No logging implementation is added here until OD-022 is resolved and a framework is approved (`CONVENTIONS.md` §H.4). Write all log calls through this interface so the implementation can be supplied later.

---

### `Recovery/`

Created only if SPIKE-002 (ARCHITECTURE.md §11) proves that an out-of-process recovery helper is necessary. If a recovery helper is needed, it is a separate executable project, not a folder inside the main application. `RecoveryCoordinator.cs` manages communication between the main application and the helper. If SPIKE-002 concludes no helper is needed, this folder stays empty or is removed before Release 1 acceptance.

---

### `Styles/`

XAML resource dictionaries that define the visual token system from `DESIGN_SYSTEM.md`. Token names in these files must match the documented token names exactly (`CONVENTIONS.md` §C.2, §C.7). No hardcoded hex values. No application logic. These dictionaries are merged in `App.xaml`.

| File | Contents |
|---|---|
| `Tokens.xaml` | Color brushes, opacity, border tokens, elevation tokens |
| `Typography.xaml` | Type size, weight, and font family tokens |
| `Controls.xaml` | Shared control styles and templates (created when controls exist) |

---

### `Assets/`

Static assets: icons, images, and later fonts. All brand assets are clearly labeled as placeholders until `BRAND.md` §10 decisions are finalized. Do not embed a temporary logo into unrelated components (`AGENT.md` §8). Keep assets replaceable.

---

### `win32.json`

The CsWin32 configuration file. Lists the Win32 functions and types the project needs generated. All Win32 P/Invoke declarations come from this file and the generated source (`CONVENTIONS.md` §D.2). Do not add methods for future releases speculatively.

---

### `tests/IslandTaskbar.Tests/`

Placeholder location for the test project. No test project is created until OD-003 is resolved and a framework is approved. When created, the test project references the main application project but does not modify it. See Section G.

---

## D. Dependency Boundaries

The dependency rule follows `ARCHITECTURE.md` §3 strictly. Lower-numbered layers may not depend on higher-numbered ones.

```
1. Presentation (Windows/, Controls/, Converters/)
        │  binds to
2. ViewModels/
        │  calls
3. Services/
        │  calls via interface
4. Adapters/Interfaces/   ◄── implemented by ── Adapters/Implementations/
        │
5. Windows OS (Win32 / WinRT)
```

Additional rules:

- `Models/` has no dependencies. Every layer may depend on `Models/`.
- `Persistence/` depends on `Models/` only. Services call persistence services; adapters do not.
- `Logging/` has no dependencies. Every layer may use `IAppLogger`.
- `Controllers/` (OD-011 pending): its layer position will be defined when OD-011 is resolved. Until then, treat it as between `Presentation/` and `Adapters/` with no direct Win32 calls.
- `Recovery/` (if needed): depends on `Adapters/Interfaces/` and `Models/`. It does not depend on `Services/` or `Presentation/`.

**Forbidden dependencies:**

| From | May NOT depend on |
|---|---|
| `Presentation/` | `Adapters/` directly, `Persistence/`, Win32 |
| `ViewModels/` | `Adapters/Implementations/`, `Persistence/`, Win32 |
| `Services/` | `Adapters/Implementations/` (use interfaces only), Win32, `Presentation/`, `ViewModels/` |
| `Adapters/Interfaces/` | Anything in this project (pure interfaces) |
| `Adapters/Implementations/` | `Presentation/`, `ViewModels/`, `Services/` |
| `Models/` | Anything in this project |
| `Persistence/` | `Adapters/`, `Services/`, `Presentation/`, `ViewModels/` |

---

## E. Naming Conventions

These rules extend and apply `CONVENTIONS.md` §B–§C to file and folder names.

### E.1 Folder Names

Folders use `PascalCase` to match the namespace segment they represent. The top-level `src/` and `tests/` folders are lowercase by convention.

| Folder | Namespace segment |
|---|---|
| `Presentation/Windows/` | `IslandTaskbar.Presentation.Windows` |
| `Presentation/Controls/` | `IslandTaskbar.Presentation.Controls` |
| `Presentation/Converters/` | `IslandTaskbar.Presentation.Converters` |
| `ViewModels/` | `IslandTaskbar.ViewModels` |
| `Services/` | `IslandTaskbar.Services` |
| `Adapters/Interfaces/` | `IslandTaskbar.Adapters.Interfaces` |
| `Adapters/Implementations/` | `IslandTaskbar.Adapters.Implementations` |
| `Controllers/` | `IslandTaskbar.Controllers` |
| `Models/` | `IslandTaskbar.Models` |
| `Persistence/` | `IslandTaskbar.Persistence` |
| `Logging/` | `IslandTaskbar.Logging` |
| `Recovery/` | `IslandTaskbar.Recovery` |

### E.2 C# File Names

One type per file. The file name matches the type name exactly (including casing):

| Type | File name |
|---|---|
| Class `PinManagerService` | `PinManagerService.cs` |
| Interface `IWindowSystem` | `IWindowSystem.cs` |
| Enum `IslandState` | `IslandState.cs` |
| Record `PinnedApp` | `PinnedApp.cs` |

### E.3 XAML File Names

XAML files use the same name as their code-behind type. Each XAML file has a paired `.cs` code-behind:

| Control / Window | XAML | Code-behind |
|---|---|---|
| Island window | `IslandWindow.xaml` | `IslandWindow.xaml.cs` |
| Notch control | `NotchControl.xaml` | `NotchControl.xaml.cs` |
| Pinned app item | `PinnedAppItem.xaml` | `PinnedAppItem.xaml.cs` |

Resource dictionary files use `PascalCase` and describe their content:

| Contents | File |
|---|---|
| Design-system color and surface tokens | `Tokens.xaml` |
| Typography tokens | `Typography.xaml` |
| Shared control styles | `Controls.xaml` |

### E.4 Adapter Implementation Names

Adapter implementations are prefixed with the interop technology to make their nature explicit:

| Interface | Implementation |
|---|---|
| `ITaskbarVisibilityController` | `Win32TaskbarVisibilityController` |
| `IWindowSystem` | `Win32WindowSystem` |
| `IApplicationLauncher` | `Win32ApplicationLauncher` |
| `IDisplayService` | `Win32DisplayService` |

### E.5 Test File Names

Test files mirror the type under test with a `Tests` suffix. Fake adapter implementations use a `Fake` prefix:

| Type under test | Test file | Fake |
|---|---|---|
| `PinManagerService` | `PinManagerServiceTests.cs` | `FakeTaskbarVisibilityController.cs` |
| `SettingsService` | `SettingsServiceTests.cs` | `FakeWindowSystem.cs` |

---

## F. Configuration and Local Data

### F.1 Code Locations

| Responsibility | File | Notes |
|---|---|---|
| Settings schema types | `Models/Settings.cs` | Typed C# representation of `settings.json` |
| Pins schema types | `Models/PinsData.cs` | Typed C# representation of `pins.json` |
| Settings read/write | `Persistence/SettingsFileService.cs` | Atomic writes; validation; safe defaults |
| Pins read/write | `Persistence/PinsFileService.cs` | Atomic writes; validation; safe defaults |
| Schema version constants | `Persistence/JsonSchemaVersion.cs` | Integer constants and migration entry points |
| Settings product logic | `Services/SettingsService.cs` | Coordinates SettingsFileService; exposes settings to view models |
| Pin product logic | `Services/PinManagerService.cs` | Coordinates PinsFileService; manages ordering |

### F.2 Filesystem Path

The runtime path to the data directory is resolved from the Windows environment in `SettingsFileService` and `PinsFileService`. The folder name within `LocalApplicationData` is a named constant declared in `Persistence/JsonSchemaVersion.cs` or a dedicated constants file. That constant is marked provisional and easy to replace when OD-006 is resolved.

```csharp
// PROVISIONAL — OD-006: folder name not yet approved. Replace when decision is recorded.
private const string AppDataFolderName = "IslandTaskbar";
```

Do not hard-code an absolute path. Do not store data beside the installed binaries (`DATABASE_SCHEMA.md` §5; `CONVENTIONS.md` §F.1).

### F.3 JSON File Locations at Runtime

The expected runtime layout (not committed to the repository):

```
%LOCALAPPDATA%\<AppDataFolderName>\   ← folder name governed by OD-006
    settings.json
    pins.json
    recovery.json                      ← only if SPIKE-002 requires it
```

---

## G. Testing and Technical Spikes

### G.1 Test Project Location

```
tests/
└── IslandTaskbar.Tests/
    ├── IslandTaskbar.Tests.csproj     ← references main project; no test framework until OD-003
    ├── Services/
    │   ├── PinManagerServiceTests.cs
    │   └── SettingsServiceTests.cs
    ├── Persistence/
    │   └── SettingsFileServiceTests.cs
    ├── ViewModels/
    │   └── IslandViewModelTests.cs
    └── Fakes/
        ├── FakeTaskbarVisibilityController.cs
        ├── FakeWindowSystem.cs
        ├── FakeApplicationLauncher.cs
        └── FakeDisplayService.cs
```

This structure is not created until OD-003 is resolved and a test framework is approved. The folder sketch above exists for planning purposes only. When the test project is created, it must not alter the structure or build output of the main application project.

Fake adapter implementations live in `tests/IslandTaskbar.Tests/Fakes/`. They implement the same interfaces as the production adapters. They must never be referenced from the main application project.

### G.2 Technical Spikes

OD-007 (location for technical spikes) is unresolved. Do not create spike folders without approval. When OD-007 is resolved, the agreed location will be recorded in `DECISIONS.md` and this document will be updated.

Spike code is prototype code. It must not be in the `src/` tree and must not be treated as production code (`AGENT.md` §4). Any spike that hides the native taskbar is subject to D-010 and D-012 regardless of location.

---

## H. Future Growth

### H.1 Release 2 Additions

Release 2 adds adapter interfaces and implementations for audio, power, network connectivity, and notifications (`ARCHITECTURE.md` §3.3). These files fit directly into the existing structure:

```
Adapters/Interfaces/
    IAudioService.cs
    IPowerStatusService.cs
    INetworkStatusService.cs
    INotificationService.cs

Adapters/Implementations/
    Win32AudioService.cs
    Win32PowerStatusService.cs
    Win32NetworkStatusService.cs
    Win32NotificationService.cs
```

New Release 2 controls (volume, battery, network status) go into `Presentation/Controls/`. New view models go into `ViewModels/`. No new project is needed.

### H.2 Release 3 Additions

Release 3 adds media session integration, system monitoring, the audio-reactive waveform, and control center panels. All of these fit into the existing layer structure:

```
Adapters/Interfaces/
    IMediaSessionService.cs

Adapters/Implementations/
    Win32MediaSessionService.cs

Services/
    SystemMonitorService.cs          ← CPU, GPU, memory metrics
    NetworkThroughputService.cs      ← upload/download (OD-013 pending)

Presentation/Controls/
    MediaControlPanel.xaml
    SystemMonitorPanel.xaml
    AudioWaveformControl.xaml
    ControlCenterPanel.xaml
```

No new top-level project is required unless a specific feature genuinely cannot share the application's process.

### H.3 Recovery Helper (If Needed)

If SPIKE-002 proves that an out-of-process recovery helper is necessary, it is a new minimal console application project alongside the main project:

```
src/
    IslandTaskbar/                    ← existing application project
    IslandTaskbar.RecoveryHelper/     ← new project, only if validated
        IslandTaskbar.RecoveryHelper.csproj
        Program.cs
        RecoveryEntry.cs
```

The helper is a simple, minimal executable. It does not share the main project's service layer. It knows only the main process ID and the Win32 state it needs to restore. It is added to `IslandTaskbar.sln` when created.

### H.4 Settings and First-Run Screen

When OD-012 is resolved and `DATABASE_SCHEMA.md` is updated with a first-run field, a new view and view model are added:

```
Presentation/Windows/
    FirstRunWindow.xaml
    FirstRunWindow.xaml.cs

ViewModels/
    FirstRunViewModel.cs
```

No settings page exists until it is assigned. When it is assigned, `SettingsViewModel.cs` and a settings control or window are added.

### H.5 Multi-Project Growth Rule

Do not create a new project to hold a cross-cutting concern or a layer. The namespace and folder structure already enforces separation. A new project is justified only when:

1. A component must run in a separate process (like the recovery helper).
2. A component must have a fundamentally different output type (console vs. GUI).
3. A test project is added.

Do not create a `.Core`, `.Shared`, `.Common`, or `.Infrastructure` project without an explicit approved need.

---

## I. Files and Directories to Avoid

### I.1 Build Outputs (Never Commit)

```
bin/
obj/
*.user
*.suo
.vs/
TestResults/
```

Ensure `.gitignore` excludes all of these before the first commit.

### I.2 Generated Code (Never Commit)

CsWin32 generates P/Invoke source code at build time. Do not commit the generated files; they are regenerated from `win32.json` on every build.

Do not commit:
```
NativeMethods.g.cs          ← CsWin32-generated output
*.g.cs                      ← any XAML-generated partial classes
*.g.i.cs
```

Ensure `.gitignore` includes `*.g.cs` and `*.g.i.cs`.

### I.3 Structures to Avoid

| Avoid | Reason |
|---|---|
| A project per layer (`.Core`, `.UI`, `.Adapters`) | Premature; adds build complexity without benefit at this stage |
| A `Common/` or `Shared/` folder inside `src/` | Ambiguous boundary; use `Models/` and `Logging/` for shared types |
| A `Helpers/` folder anywhere in the project | Vague purpose; name the actual responsibility |
| `Views/` as an alias for `Presentation/` | Inconsistency with the approved namespace and folder names |
| A `ViewModel` base class before OD-002 is resolved | Requires choosing a MVVM approach; leave flat until approved |
| Empty interface files for future adapters | No speculative adapters (`ARCHITECTURE.md` §3.3; `AGENT.md` §5) |
| A `Repositories/` folder | No database; this terminology implies data access that does not apply |
| A `.env` file | No backend; no secrets; explicitly forbidden (`ENV.md` §7) |
| A `wwwroot/` or `ClientApp/` folder | No web or Electron component; explicitly excluded (`SCOPE.md` §4) |
| Machine-specific absolute paths in `.csproj` | Prevents builds on other machines (`ENV.md` §0) |
| A `Migrations/` folder | No database (`DATABASE_SCHEMA.md` §2) |

### I.4 Speculative Placeholder Files

Do not create:
- Empty `.cs` files as folder markers.
- Stub service files for Release 2 or Release 3.
- Empty XAML files for future controls.
- An `IBaseViewModel` or `IBaseService` before the MVVM approach is decided (OD-002).

The folder structure communicates intent; the folders themselves do not need placeholder files.

---

## J. Document and Structure Change Rules

This document may be updated when:

1. A new file is introduced as part of an assigned task (add it to the tree in Section B).
2. A pending decision (OD-006, OD-007, OD-011, OD-012) is resolved and changes the structure.
3. A new release begins and its files need to be mapped.

Changes follow `AGENT.md` §16: propose the change, get approval, then update. Record the change in `CHANGELOG.md`. Do not unilaterally move files or folders to match a different mental model of the structure.

---

## K. Open Decisions That Affect This Document

| Decision | Effect on structure | Rule until resolved |
|---|---|---|
| OD-006 — Application-data folder name | Folder name constant in `Persistence/` | Use a named provisional constant; do not hard-code |
| OD-007 — Technical spike location | No spike folder exists yet | Do not create a spike folder without approval |
| OD-011 — Island window controller form and layer | `Controllers/` contents and interface | Folder exists; no Win32 logic until resolved |
| OD-012 — First-run state storage | `FirstRunWindow.xaml`, `FirstRunViewModel.cs`, first-run field in persistence | Do not create these files until `DATABASE_SCHEMA.md` is updated |
| OD-003 — Testing framework | `tests/IslandTaskbar.Tests/` contents | Test project is not created until a framework is approved |
| OD-002 — MVVM library and DI approach | Whether a DI container registration file exists | No DI wiring file until approved |
| OD-009 — Recovery helper necessity | Whether `src/IslandTaskbar.RecoveryHelper/` is created | Do not create the recovery helper project until SPIKE-002 is recorded |
| OD-023 — CsWin32 version | `win32.json` package reference version | Do not pin a CsWin32 version without resolving OD-023 |

---

**Document owner:** Product Owner  
**Engineering partner:** Chief Engineer  
**Change control:** Changes follow `AGENT.md` §16. This document is updated when the structure changes due to an approved task, a resolved open decision, or a new release boundary. Agents propose changes via Decision Request and do not edit this document unless the task assigns it.

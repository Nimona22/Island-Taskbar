# Architecture

**Working product name:** IslandTaskbar  
**Document status:** Draft v0.2  
**Last updated:** 2026-11-25

**Depends on:** `PRD.md`, `SCOPE.md`, `USER_STORIES.md`

> This document describes the intended technical shape of a clean-start project. It does not assume that an earlier prototype or generated code exists. Decisions marked **Needs validation** must be proven on the target Windows machine before they are treated as implementation facts.

**Phase 5.2 authority:** `Phase52_Island_App_Presence_Architecture_Contract.md` v1.0 is the approved baseline (D-013 through D-015). Sections 6.1–6.6 below specialize older general sketches for this phase. Phase 5.1 is accepted; its current source and behavior remain the baseline. Native taskbar hiding, Start/Search and unrelated historical debt are not authorized by Phase 5.2.

---

## 0. AI Agent Rules (Read First)

> **These rules apply to every AI agent, coding assistant, or automated tool working on this project. Read this section before writing any code, suggesting any package, or making any structural decision.**

### The agent MUST NOT:

- Change the tech stack (C#, .NET 10 LTS, WinUI 3, Windows App SDK 2.7.0) without a written ADR entry and product owner approval.
- Add Electron, web technologies, a browser shell, or any non-native UI framework — ever.
- Add a database, backend service, cloud dependency, or remote API — none are required.
- Make Windhawk a runtime or installation dependency.
- Call Win32 APIs directly from the presentation layer — all Win32 calls go through adapter interfaces.
- Use Experimental or Preview Windows App SDK APIs in production paths without an explicit decision.
- Add third-party packages without a documented reason — keep dependencies minimal.
- Skip the adapter interface pattern — every Windows integration must sit behind a narrow interface.
- Treat "Needs validation" items as decided — they must be prototyped and proven first.
- Build Release 2 or Release 3 features before Release 1 is complete and tested.
- Add animations, visual effects, or decorative work before core taskbar logic is verified.
- Invent a folder structure — follow `FILE_STRUCTURE.md` once it exists; use the sketch in Section 3.4 until then.

### The agent MUST always:

- Keep UI-bound state on the WinUI dispatcher thread.
- Run slow work (file I/O, window enumeration, metrics sampling) off the UI thread.
- Use event-driven Windows notifications over polling wherever available.
- Isolate all Win32 interop behind the adapter interfaces defined in Section 3.3.
- Use CsWin32-generated declarations for Win32 P/Invoke — no handwritten P/Invoke scattered across the codebase.
- Write atomic settings saves (write temp file → replace).
- Ensure the native taskbar recovery path is never broken by any change.
- Match the visual direction in `PRD.md` Section 6 for all UI work.

---

## 1. Architecture Summary

IslandTaskbar will be an **independent native Windows desktop application** that renders the only visible taskbar while it is active. It will hide the native taskbar through a reversible Windows integration layer; it will not inject code into or patch `explorer.exe` as its primary architecture.

The product will own its visible interface and taskbar experience. Windows will continue to provide the underlying desktop, application processes, window management, Start/Search experiences, media sessions, and system services where supported.

The architecture is intentionally local-first. Core operation will not require a server, cloud account, database, or internet connection.

### High-level shape

```
┌──────────────────────────────────────────────────────────────┐
│                    IslandTaskbar Application                  │
│                                                              │
│  ┌──────────────────────┐  ┌───────────────────────────────┐  │
│  │ Presentation / UI    │  │ ViewModels / UI State         │  │
│  │ Notch, Island, Panels│◄─┤ Commands, selection, state    │  │
│  └──────────────────────┘  └───────────────┬───────────────┘  │
│                                            │                  │
│  ┌─────────────────────────────────────────▼───────────────┐  │
│  │ Application Services                                    │  │
│  │ App launch · Window switch · Pins · Settings · Lifecycle│  │
│  └─────────────────────────────────────────┬───────────────┘  │
│                                            │                  │
│  ┌─────────────────────────────────────────▼───────────────┐  │
│  │ Windows Integration Adapters                            │  │
│  │ Taskbar visibility · Win32 windows · Start/Search       │  │
│  │ Audio · Media · Power · Network · Notifications         │  │
│  └─────────────────────────────────────────┬───────────────┘  │
└───────────────────────────────────────────┼────────────────────┘
                                            │
                           ┌────────────────▼────────────────┐
                           │ Windows 11 / Windows Shell       │
                           │ Explorer · DWM · Core Audio      │
                           │ Media Sessions · Power · Network │
                           └─────────────────────────────────┘

       Separate recovery helper (only if required by validation)
       monitors the main process and restores native taskbar state.
```

---

## 2. Technology Baseline

| Area | Selected baseline | Reason |
|---|---|---|
| Language | C# | Good Windows desktop and Win32 interoperability with a productive development workflow. |
| Runtime | .NET 10 LTS | Preferred LTS track for a long-lived desktop application. .NET 11 is STS — not suitable for this product. |
| UI framework | WinUI 3 | Native Windows UI framework with modern XAML, composition, and accessibility support. |
| Windows app platform | Windows App SDK 2.7.0 stable | Current stable release; supports Windows 11 26H2. Pin and validate the exact package/runtime version when the repository is created. |
| Native integration | Win32 APIs via CsWin32-generated declarations | Keeps native interop explicit and avoids scattering handwritten P/Invoke throughout the codebase. |
| UI pattern | MVVM with small, focused services | Separates visual state from Windows operations and makes core behavior testable. |
| Local persistence | Versioned JSON settings files | No relational data or backend requirement exists in the current scope. |
| Target architecture | x64 initially; ARM64 after core validation | Matches the initial development environment and reduces early build/test combinations. |

> **Note on .NET version:** This document uses .NET 10 LTS. The PRD.md originally referenced .NET 8 — that reference is superseded by this document. .NET 10 LTS is the correct runtime for this project. Do not change this without a written ADR.

### Framework and package rules

- Use stable packages for the application baseline. Do not use Experimental or Preview Windows App SDK APIs in production paths without an explicit decision.
- Keep third-party dependencies minimal. Add a package only when it provides a clear capability or maintenance benefit.
- Use CsWin32 or another maintained source-generated interop approach for Win32 declarations.
- Do not use Electron, a browser-hosted UI, or a web application shell for the taskbar interface.
- Do not make Windhawk a runtime or installation dependency.

---

## 3. Application Structure and Responsibilities

### 3.1 Presentation Layer

Owns the visible experience:

- Collapsed top-center notch.
- Expanded Dynamic Island surface.
- Taskbar sections: Start, Search, pinned apps, running windows, clock, and system controls.
- Temporary status surfaces and later feature panels.
- Keyboard navigation, accessibility names, transitions, and hit testing.

**Rule:** The presentation layer must never directly call low-level Win32 APIs. It calls application services through view models or commands only.

---

### 3.2 Application Services

Own product behavior and coordinate the adapters:

- **Taskbar lifecycle:** starts and stops IslandTaskbar and coordinates native-taskbar visibility.
- **Application launcher:** launches pinned or selected applications and reports failures.
- **Window manager:** discovers eligible top-level windows, tracks changes, restores minimized windows, and activates selected windows.
- **Pin manager:** adds, removes, orders, and resolves pinned application references.
- **Settings service:** loads, validates, migrates, and saves local preferences.
- **Display manager:** tracks monitor layout, primary display, scaling, and display changes.
- **Recovery coordinator:** communicates with a recovery helper if testing shows an out-of-process watchdog is required.

---

### 3.3 Windows Integration Adapters

Encapsulate Windows-specific calls behind narrow interfaces. This allows APIs to be tested and replaced without coupling the UI to shell implementation details.

**Rule:** Every Windows API call must go through one of these adapter interfaces. No exceptions.

| Interface | Responsibility | Release needed |
|---|---|---|
| `ITaskbarVisibilityController` | Hide and restore native taskbar windows | Release 1 |
| `IWindowSystem` | Enumerate, inspect, restore, and activate application windows | Release 1 |
| `IApplicationLauncher` | Dispatch explicitly selected application launch targets; no window activation or Start/Search in Phase 5.2 | Release 1 / Phase 5.2 |
| `IApplicationResolver` | Validate typed targets, real metadata and reliable identity for one supplied window; no window discovery | Phase 5.2 |
| `IApplicationSourcePicker` | Select a native local EXE/LNK source with the real IslandWindow owner | Phase 5.2 |
| `IDisplayService` | Monitor and DPI information | Release 1 |
| `IAudioService` | Volume and mute state/control | Release 2 |
| `IMediaSessionService` | Active media sessions and supported playback commands | Release 3 |
| `IPowerStatusService` | Battery and charging state | Release 2 |
| `INetworkStatusService` | Connectivity and transfer-rate metrics | Release 2 |
| `INotificationService` | Supported notification access and entry points | Release 2 |

**Only implement adapters required by the current release. Do not add placeholder implementations for future adapters — they will be built when needed.**

---

### 3.4 Folder Structure Sketch

> Exact folders and class names will be defined in `FILE_STRUCTURE.md`. Use this sketch until that document exists. Do not invent a different structure.

```
IslandTaskbar/
├── App.xaml / App.xaml.cs          ← Application entry point
├── Presentation/
│   ├── Windows/                    ← Top-level window(s): notch, island
│   ├── Controls/                   ← Reusable UI controls
│   └── Converters/                 ← Value converters for XAML bindings
├── ViewModels/
│   ├── NotchViewModel.cs
│   ├── IslandViewModel.cs
│   └── SettingsViewModel.cs
├── Services/                       ← Application services (Section 3.2)
│   ├── TaskbarLifecycleService.cs
│   ├── ApplicationLauncherService.cs
│   ├── WindowManagerService.cs
│   ├── PinManagerService.cs
│   ├── SettingsService.cs
│   └── DisplayManagerService.cs
├── Adapters/                       ← Windows integration adapters (Section 3.3)
│   ├── Interfaces/                 ← Adapter interfaces (ITaskbarVisibilityController etc.)
│   └── Implementations/            ← Real Win32/WinRT implementations
├── Models/                         ← Data models: PinnedApp, RunningWindow, Settings
├── Recovery/                       ← Recovery helper (if validated as needed)
└── Assets/                         ← Icons, images, resources
```

---

## 4. Windowing and Visual Composition

### 4.1 Main Island Window

- Use a borderless WinUI 3 top-level window for the notch/island surface.
- Keep the window topmost during normal desktop use, subject to Windows' secure desktop and exclusive-fullscreen behavior.
- Anchor the collapsed notch to the horizontal center of the primary display, flush with the top edge.
- Expand downward from the notch; collapse back without moving or resizing the desktop work area.
- Do not register the main island as a work-area-reserving AppBar.
- Use WinUI composition/backdrop capabilities for glass and animation. Do not rely on legacy DWM blur-behind behavior as the source of modern acrylic blur.
- Handle DPI and display changes and recalculate physical window bounds.

### 4.2 Hit Testing and Interaction

The visible notch and expanded surface should receive input. Transparent areas outside the actual interactive surface should allow clicks to reach the application behind them whenever possible.

Hit testing must be deliberate:

- Collapsed state: the interactive region should closely match the visible notch.
- Expanded state: the interactive region should match the island and its controls, not an unnecessarily large transparent rectangle.
- Hover expansion must use an intent delay and must not steal focus from the current application just because the pointer passed over the notch.
- Keyboard access must not depend on hover having activated the window.

**These behaviors require testing on the target Windows build; a transparent visual pixel is not automatically click-through.**

---

## 5. Native Taskbar Visibility and Recovery

This is a dedicated integration boundary because hiding the native taskbar is essential to the one-visible-taskbar requirement.

### 5.1 Visibility Controller

`ITaskbarVisibilityController` is responsible for:

1. Identifying the native taskbar window(s) present for the current display configuration.
2. Recording the relevant pre-existing visibility/auto-hide state needed for restoration.
3. Hiding the native taskbar while IslandTaskbar is active.
4. Restoring the original state on normal shutdown.
5. Reapplying the intended state if Explorer restarts, after re-discovering the taskbar window handles.

The exact Win32 operation used to hide the taskbar is **Needs validation**. It must be reversible, tested on Windows build `26300.9974`, and must not permanently change the user's taskbar preferences. Do not silently substitute auto-hide if that allows the native taskbar to appear on pointer movement and create two visible taskbars.

No code injection, Explorer patching, or Windhawk dependency is part of this design.

### 5.2 Recovery Behavior

Normal shutdown must restore the native taskbar. A `finally` block alone is not sufficient protection against process termination or a crash.

The initial design should evaluate a small, separate recovery helper that:

- Is started when IslandTaskbar takes control of native-taskbar visibility.
- Knows the main process identity and the restoration information.
- Restores the native taskbar if the main process exits unexpectedly.
- Exits cleanly after normal shutdown and restoration.

Build the helper only if the prototype proves it is required; do not add a permanent second process without evidence. A manual recovery method must also be documented for development and support.

---

## 6. Application and Window Management

### Application Launching

Pinned entries should store a stable launch reference rather than depend only on a display name. The reference may resolve to a Start-menu shortcut, executable path, or packaged application identity, depending on the application type. Resolution and launch failures must be handled without crashing the taskbar.

### Running Windows

Use Windows window-enumeration and window-activation capabilities to discover eligible top-level application windows. Filter out hidden, shell-owned, tool, and otherwise irrelevant windows. Support minimizing/restoring and multiple windows from the same application with a predictable selection model.

Window discovery should be event-driven where a supported notification is available; avoid aggressive polling. Any required polling interval must be measured and documented.

### Start and Search

Prefer invoking the existing Windows Start and Search experiences through supported mechanisms or documented keyboard/shell activation behavior. Do not attempt to rebuild the Windows Start menu as part of the MVP.

### 6.1 Island App Presence — approved Phase 5.2 semantics (D-013)

Use two labeled horizontal areas, **Pinned** and **Open windows**, in one continuous warm-glass surface. A pin is durable launch intent; a window is one live workspace. One Chrome pin plus three Chrome windows produces four separate entries. Never group or hide eligible windows behind a pin.

- Clicking a pin requests launch of its revalidated target even when matching windows exist. It never selects, cycles or activates an existing window. The OS/application may reuse an instance; no promise of a new process/window is made.
- Clicking a running entry continues through `WindowManagerService.ActivateAsync` for that exact `RunningWindowId`, including minimized restoration. Keep accepted stable ordering, active/minimized indicators, keyboard access and automatic dismissal.
- Provide an always-reachable **Add application…** action using the native chooser. Pin from a window only when the resolver returns a trustworthy relaunch reference. Cancel changes nothing. Unpin never closes a window, deletes the original shortcut or uninstalls an app.
- Availability and positive running presence are separate runtime facts. Show a running marker only for a trustworthy identity match. No match means unknown, not “not running”; do not show precise counts. Failed enrichment never hides/disables a running entry and never writes/reorders pins.
- No installed-app catalogue, Start/Search (Phase 5.3), grouping, previews, drag reordering, background/startup launches, command-interpreter strings, automatic elevation or native-taskbar changes. `P23-TOPMOST-001` remains unresolved.

### 6.2 Shared identity and immutable model freeze (D-014)

M0 declares the internal types in `IslandTaskbar.Models`; concrete source definitions are the signature authority. Shared declarations are frozen for M0 review, not a claim that M1/M2/M3 are implemented.

| Contract | Meaning / invariant |
|---|---|
| `PinId(Guid Value)` | Persistent GUID; reject `Guid.Empty` at load/service boundaries. Generated once for a successful new pin; moves/restarts do not change it. |
| `ApplicationIdentity(ApplicationIdentityKind Kind, string Value)` | Runtime-only `AppUserModelId` or canonical full `ExecutablePath`. Never title, basename, display name or PID alone. |
| `LaunchReference(LaunchReferenceKind Kind, string Value)` | Typed EXE/LNK/verified-packaged intent; not an identity and not proof of availability. Resolver/persistence boundaries reject invalid kind/value pairs. |
| `PinnedApp`, `PinsData` | Immutable v1 committed pin payload; `PinsData.CurrentSchemaVersion = 1`. No runtime presence, HWND/PID, busy state or icon pixels. |
| `ApplicationDetails`, `ApplicationIcon` | Runtime name, target, optional reliable identity and immutable copied BGRA pixels. No native handle crosses into models. A missing icon does not invalidate an otherwise valid target. |
| `ApplicationResolutionResult`, `ApplicationWindowIdentityResult` | Distinguish Available, Missing, Unsupported, AccessDenied and Failed. Ordinary target resolution requires Application on Available; window identity requires Identity but may lack a trustworthy relaunch reference. |
| `ApplicationSourceSelectionResult` | Selected (typed Source required), Canceled, Unavailable or Failed. Selection neither launches nor pins. |
| `ApplicationLaunchResult` | Accepted, Busy, Canceled, Unavailable, StaleTarget, Unsupported, Denied, Failed or OutcomeUnknown. Accepted proves only OS dispatch acceptance. |
| `PinsLoadResult`, `PinsSaveResult` | Explicit load/save status, opaque revision and write access; invalid/unreadable input never becomes writable empty data. See `DATABASE_SCHEMA.md` §4.1. |
| `PinMutationResult` | Saved, Unchanged, AlreadyPinned, NotFound, NotInitialized, InvalidSource, ReadOnly, Conflict or SaveFailed. InvalidSource retains the resolver status. |
| `PinManagerSnapshot`, `PinnedAppRuntimeState` | Immutable committed pin sequence plus a separate immutable runtime-state dictionary keyed by PinId. Runtime resolution may be pending; presence is only Unknown or positively Running. |

Paths in identity/reference values must already be canonical full paths and compare with `OrdinalIgnoreCase`; opaque AppUserModelIDs compare with `Ordinal`. Kinds participate in equality and hashing. The records implement those value semantics; they do not validate, canonicalize or discover anything. Distinct shortcut paths remain distinct intents even when they resolve to the same EXE. An argument-bearing/profile shortcut gets no generic EXE identity unless the resolver can prove the relationship. Unknown identity is nullable, not a fabricated key. Default/empty identity/reference structs are invalid at boundaries.

Use BCL `ImmutableArray`/`ImmutableDictionary` without additional packages. Arrays must be initialized, including empty collections. Preserve existing `RunningWindow`, `RunningWindowId` and `WindowIcon` unchanged. `ApplicationIcon` avoids introducing mutable pixel arrays into new snapshots.

### 6.3 Frozen interfaces and service responsibilities (D-015)

All six new interfaces extend `IAsyncDisposable`; implementations arrive in later milestones. Every asynchronous operation below requires `CancellationToken cancellationToken`. Existing Phase 5.1 `IDisposable` contracts are unchanged.

```text
IslandTaskbar.Persistence.IPinsFileStore
  Task<PinsLoadResult> LoadAsync(CancellationToken cancellationToken)
  Task<PinsSaveResult> SaveAsync(PinsData data, string? expectedRevision, CancellationToken cancellationToken)

IslandTaskbar.Adapters.Interfaces.IApplicationResolver
  Task<ApplicationResolutionResult> ResolveAsync(LaunchReference source, CancellationToken cancellationToken)
  Task<ApplicationWindowIdentityResult> IdentifyWindowAsync(RunningWindowId runningWindowId, CancellationToken cancellationToken)

IslandTaskbar.Adapters.Interfaces.IApplicationLauncher
  Task<ApplicationLaunchResult> LaunchAsync(LaunchReference launchReference, CancellationToken cancellationToken)

IslandTaskbar.Adapters.Interfaces.IApplicationSourcePicker
  Task<ApplicationSourceSelectionResult> PickAsync(CancellationToken cancellationToken)

IslandTaskbar.Services.IPinManagerService
  event EventHandler? Changed
  PinManagerSnapshot Current { get; }
  Task<PinsLoadResult> InitializeAsync(CancellationToken cancellationToken)
  Task<PinMutationResult> PinAsync(LaunchReference source, CancellationToken cancellationToken)
  Task<PinMutationResult> UnpinAsync(PinId pinId, CancellationToken cancellationToken)
  Task<PinMutationResult> MoveAsync(PinId pinId, PinId? beforePinId, CancellationToken cancellationToken)
  Task<ApplicationWindowIdentityResult> ResolveWindowAsync(RunningWindowId runningWindowId, CancellationToken cancellationToken)
  Task RefreshAvailabilityAsync(PinId pinId, CancellationToken cancellationToken)

IslandTaskbar.Services.IApplicationLauncherService
  Task<ApplicationLaunchResult> OpenAsync(PinId pinId, CancellationToken cancellationToken)
  Task<ApplicationSourceSelectionResult> SelectSourceAsync(CancellationToken cancellationToken)
```

- `PinsFileService` implements `IPinsFileStore`: local wire encoding/validation, exclusive lease, revision compare and atomic commit. It has no Windows adapter, view model or UI dependency.
- `PinManagerService` implements `IPinManagerService`: serialized initialization/reload, pin/unpin/move, save-before-publish and lightweight runtime enrichment. `Current` is an atomically replaced snapshot. `Changed` may occur off the UI thread; the view model marshals reconciliation. Save failure retains committed pins/order/revision. Duplicate canonical references return AlreadyPinned with the existing ID and do not write. Successful unpin/move makes order contiguous from zero.
- `MoveAsync(id, null)` appends; moving before self or into the current position returns Unchanged. A nonexistent pin or before-pin returns NotFound without saving. “Move earlier” inserts before the preceding item. “Move later” inserts before the item two places after it, or appends at the end. Derive this from the latest keyed snapshot; serialized service validation rejects stale targets.
- Repeating `InitializeAsync` is an explicit reload/lease-recovery request, serialized with mutations. Before the first completed load attempt, IsInitialized is false and mutations return NotInitialized. After an attempted load it is true even if recovery is read-only; first-load failures expose a read-only empty view, not writable empty data. Later failures retain the last committed collection. `RefreshAvailabilityAsync` does no persistence; a removed pin is a harmless stale runtime refresh with no new entry.
- `ApplicationLauncherService` implements `IApplicationLauncherService`: snapshot the selected pin, revalidate, check the same pin/reference still exists before dispatch, reject simultaneous requests for the same canonical reference as Busy, then call the launcher. A removed/changed pin is StaleTarget; an unavailable target is Unavailable/Unsupported/Denied as applicable. No fallback to another window or launch target. `SelectSourceAsync` only chooses; the caller explicitly invokes PinAsync for Selected.
- `WindowManagerService` remains the only running-window snapshot/activation owner. Enrichment queries only supplied IDs; the pin manager checks the full ID still exists before and after asynchronous work, discarding stale replies. No additional EnumWindows path or process monitor. `ResolveWindowAsync` exposes this through the service boundary for a window context action: return Missing with no identity/reference for a stale ID; do not persist. Its native work is off-UI and uses the resolver cancellation contract. M3 offers Pin application only when this result contains a trustworthy RelaunchReference, then passes that reference to PinAsync for revalidation. The view model never calls the resolver adapter or derives a relaunch target from a title/basename/PID.

Resolution/read/mutation operations honor cancellation using canceled tasks before irreversible commit. Picker Canceled distinguishes user dismissal; token cancellation may cancel the task after the native dialog is safely closed. Launch Canceled (or a canceled task) is allowed only when no dispatch occurred. Once dispatch may have occurred, return Accepted if known accepted, otherwise OutcomeUnknown; never automatically retry or create a phantom window. Once atomic save commits, return Saved and publish that committed snapshot even if cancellation arrived meanwhile.

### 6.4 Composition, ownership and shutdown

Keep manual construction in `App.xaml.cs`. These are future constructor contracts, not M0 implementations:

```text
PinsFileService(string? dataDirectory = null)
  null -> runtime LocalApplicationData/IslandTaskbar; tests inject an isolated directory
PinManagerService(IPinsFileStore store, IApplicationResolver resolver, WindowManagerService windows)
ApplicationLauncherService(IPinManagerService pins, IApplicationResolver resolver,
                           IApplicationLauncher launcher, IApplicationSourcePicker picker)
Win32ApplicationResolver()
Win32ApplicationLauncher()
Win32ApplicationSourcePicker(Func<nint> ownerWindowProvider)
```

Construct one existing window service and one resolver. The taskbar view model owns new pin/launcher services and the existing window service. The pin manager owns its store/resolver and borrows the window service. The launcher service owns launcher/picker and borrows pins/the same resolver. Never dispose borrowed dependencies.

On shutdown, stop accepting commands, cancel/await and dispose the launcher service first (including picker), then pin manager (unsubscribe windows, drain operations, dispose store/resolver), then the existing window service. Keep the dispatcher alive while awaiting new asynchronous disposal; do not block it with Wait/Result. New asynchronous disposal is required so file commits and apartment-owned COM cleanup can complete before the lease/worker is released. M0 does not change existing shutdown or media behavior.

Native shell/COM instances are created, used and released on their owning worker apartment, off the UI thread. Async continuations must not move COM ownership to arbitrary pool threads. The chooser obtains the real IslandWindow handle through a deferred owner provider: capture it on the caller/UI thread before posting dialog work; zero/unavailable owner returns Unavailable. This avoids constructing a second window or a composition cycle. M3 owns an existing dismissal-guard scope around the awaited chooser call in try/finally, including cancellation and shutdown.

### 6.5 Dynamic horizontal layout — integration requirement only

Preserve 88×26 DIP idle, 420-DIP Taskbar width, top attachment at visible Y=0, Music Mode/GSMTC, warm glass, clipping, click-through and dismissal. Both labeled areas wrap left-to-right, with independent continuous vertical scrolling. No carousel, pagination arrows, vertical app sidebar, grouping or width growth. Pinned budgets a compact first row; Open windows retains its current three-visible-row target and four-column reference where it fits. Wheel/trackpad input stays with the area under the pointer, including at its boundary.

The existing 200-DIP host envelope is insufficient for both areas. M3 must measure combined controls/labels/content, allocate area heights and adjust the existing controller/adapter downward, clamped to display/work-area constraints. Preserve at least one usable row per populated area when that space exists; if the work area is physically too small, report/verify the constrained fallback rather than clipping controls silently. Keep all remaining items scroll-accessible. Do not substitute an item-count-only fixed height or change idle/Music geometry. Verify final rounded/input regions at measured size.

### 6.6 Scope gate and sequential ownership

M0 adds only shared declarations and documentation. The exact file ownership map is `FILE_STRUCTURE.md` §F.4. After M0 review/acceptance, execute **M1 persistence → M2 Windows adapters → M3 integration → M4 verification** with the sole implementation agent. Shared declarations remain integration-owned; changes require a coordinated contract update, not local forks of the types. No M1/M2/M3 implementations are added or wired in M0. Phase 5.2 completion requires real Windows evidence from the approved contract §9; compilation alone is insufficient.

---

## 7. Local Data and Persistence

The current scope does not require a database, server, account system, or remote API.

Use local application data for:

- Pinned application references and their order.
- User preferences and feature toggles.
- Layout and appearance settings.
- A small settings schema/version number for future migrations.
- Recovery metadata only where required, stored with minimum necessary information.

Start with versioned JSON files and atomic writes (write a temporary file, then replace the previous file). Validate data when loading and recover safely from missing or malformed settings. Do not store credentials or sensitive personal information.

**Phase 5.2 pins specialize this general rule (D-014):** `%LOCALAPPDATA%\IslandTaskbar\pins.json`, schema 1 and validated `exe:`, `lnk:` or `packaged:` references. Only a missing file means writable empty pins, and only when the exclusive writer lease is held. Corrupt/invalid/unsupported-version data is preserved in read-only recovery, never overwritten with defaults. Save uses a same-directory flushed temporary file, revision validation and atomic replace/first creation; publish only after success. See `DATABASE_SCHEMA.md` §4.1 for the complete wire and recovery contract.

> **Database decision:** No database is planned for the current product scope. `DATABASE_SCHEMA.md` should explicitly record that no database is required rather than inventing tables.

---

## 8. Packaging and Installation

The project should begin with a development packaging configuration that is easy to build and debug. The final distribution model is **not locked yet**.

Recommended direction for evaluation:

- Prefer an MSIX-packaged WinUI 3 application if package identity is needed for Windows integrations such as notifications and startup integration.
- Consider packaging with external location if the product needs a conventional EXE installer while retaining package identity.
- Consider an unpackaged build only if its simpler deployment materially outweighs the Windows features it gives up.

Before choosing the release installer, verify the required capabilities, runtime deployment, startup behavior, update path, and taskbar visibility behavior in a local prototype. The final user experience must be a normal install/uninstall flow and must not require Windhawk.

---

## 9. Threading, State, and Performance

- Keep UI-bound state on the WinUI dispatcher thread.
- Run window enumeration, file operations, metrics sampling, and other potentially slow work away from the UI thread.
- Marshal results back to the UI thread through a controlled state/update path.
- Prefer Windows events and notifications over continuous polling.
- Use bounded sampling rates for CPU, GPU, network, and audio visualizations.
- Pause or reduce nonessential monitoring when the relevant panel is closed or the app is idle.
- Avoid creating a timer per control; centralize periodic sampling by feature.
- Log meaningful failures and lifecycle events locally. No telemetry or remote data collection is planned by default.

**Performance targets (must be validated before Release 1 is accepted):**
- Idle CPU: under 1%
- Idle memory: under 150MB
- UI interactions: feel immediate, no visible lag
- Animations: smooth at 60fps, no dropped frames during expand/collapse

---

## 10. Security and Permissions

- Run as a standard user by default.
- Do not request administrator privileges unless a specific, approved feature demonstrates a requirement.
- Keep native interop isolated and validate window handles and process state before acting on them.
- Do not execute arbitrary commands from settings or pin metadata.
- Treat application paths and shortcut targets as untrusted input; handle missing or inaccessible targets safely.
- Request Windows permissions only when the user enables the feature that needs them.

---

## 11. Validation Spikes (Must Complete Before Coding Starts)

> These spikes must be completed and results recorded in `DECISIONS.md` before the corresponding production code is written. The agent must not skip spikes or assume results.

| Spike | What to prove | Blocks |
|---|---|---|
| **SPIKE-001** | Reversible native taskbar hide/restore on build `26300.9974` without auto-hide side effects | ADR-008, all of Release 1 |
| **SPIKE-002** | Recovery helper necessity — does a `finally` block survive force-kill? | ADR-009, US-003 |
| **SPIKE-003** | Borderless WinUI 3 window anchored flush to top-center edge without reserving work area | ADR-008, US-004 |
| **SPIKE-004** | Hit-test click-through on transparent window regions | US-005 |
| **SPIKE-005** | MSIX vs unpackaged — which supports required startup and notification integration | ADR-010 |

**Record spike results in `DECISIONS.md` with: what was tested, what machine/build, what worked, what didn't, and the decision made.**

---

## 12. Release-Oriented Implementation Boundaries

### Release 1 — Core Replacement

Implement only what is needed for a dependable first taskbar replacement:

- Application lifecycle and single-instance behavior.
- Notch and expanded island window.
- Reversible native-taskbar hide/restore and recovery path.
- Application launch and pinned app management.
- Running-window discovery and switching.
- Start and Search entry points.
- Clock/date and essential settings.
- Primary-display positioning, DPI, and display-change handling.

### Release 2 — Essential Windows Coverage

Add volume, network/Wi-Fi, battery, calendar/notifications entry points, the supported tray strategy, Explorer-restart handling, and multi-monitor behavior.

### Release 3 — Product-Specific Features

Add media-session controls, audio-reactive waveform, Control Center, network transfer rates, CPU/RAM/GPU monitoring, temporary status surfaces, and expanded customization.

---

## 13. Architecture Decisions and Open Items

| ID | Decision / question | Current status |
|---|---|---|
| ADR-001 | Independent application; no forced Explorer injection | **Decided** |
| ADR-002 | One visible custom taskbar; native taskbar hidden while active | **Decided** |
| ADR-003 | C# + .NET 10 LTS + WinUI 3 | **Decided** |
| ADR-004 | Windows App SDK 2.7.0 stable | **Decided; validate exact version at project creation** |
| ADR-005 | Win32 interop isolated behind adapters; use CsWin32 | **Decided** |
| ADR-006 | No database or backend for current scope | **Decided** |
| ADR-007 | Local versioned JSON for settings and pins | **Decided** |
| ADR-008 | Exact taskbar hide/restore Win32 mechanism | **Needs validation — SPIKE-001** |
| ADR-009 | Whether a separate recovery helper is necessary | **Needs validation — SPIKE-002** |
| ADR-010 | MSIX, sparse package, or unpackaged distribution | **Needs validation — SPIKE-005** |
| ADR-011 | Final multi-monitor model | **Deferred to Release 2 design** |
| ADR-012 | Supported notification-area/tray compatibility | **Needs validation; no universal parity promise** |

Changes to these decisions must be recorded in `DECISIONS.md` with the reason, alternatives considered, and consequences.

---

## 14. Definition of Done for Architecture

This architecture is ready to guide implementation when:

- [ ] The Product Owner approves the independent replacement approach and technology baseline.
- [ ] SPIKE-001 (taskbar hide/restore) has been completed and result recorded in `DECISIONS.md`.
- [ ] SPIKE-002 (recovery helper) has been completed and result recorded in `DECISIONS.md`.
- [ ] SPIKE-003 (window anchoring) has been completed and result recorded in `DECISIONS.md`.
- [ ] SPIKE-004 (hit-test click-through) has been completed and result recorded in `DECISIONS.md`.
- [ ] SPIKE-005 (packaging direction) has been selected and result recorded in `DECISIONS.md`.
- [ ] Module responsibilities and folder structure are reflected in `FILE_STRUCTURE.md` and `COMPONENTS.md`.
- [ ] No implementation claim is treated as verified until it has been built and tested on Windows.

---

**Document owner:** Product Owner  
**Engineering partner:** Chief Engineer  
**Next planned document:** `DATABASE_SCHEMA.md` (expected to document that no database is currently needed), then `DESIGN_SYSTEM.md`

---

## References

- [Microsoft — .NET releases and support](https://learn.microsoft.com/dotnet/core/releases-and-support)
- [Microsoft — What's new for Windows developers](https://learn.microsoft.com/windows/apps/whats-new/whats-new-for-developers)
- [Microsoft — Windows App SDK and supported Windows releases](https://learn.microsoft.com/windows/apps/windows-app-sdk/support)
- [Microsoft — Packaging overview](https://learn.microsoft.com/windows/apps/package-and-deploy/packaging/)
- [Microsoft — Distribute an unpackaged WinUI 3 app](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app)

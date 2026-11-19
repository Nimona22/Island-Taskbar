# CONVENTIONS.md

**Project:** IslandTaskbar  
**Document status:** Draft v0.1  
**Last updated:** 2026-11-19  
**Depends on:** `PRD.md`, `SCOPE.md`, `USER_STORIES.md`, `ARCHITECTURE.md`, `DATABASE_SCHEMA.md`, `ENV.md`, `DESIGN_SYSTEM.md`, `BRAND.md`, `AGENT.md`, `DECISIONS.md`, `TESTING.md`

> This document records the coding and contribution conventions for IslandTaskbar. It governs how contributors write, structure, and maintain code and project documents. It does not restate requirements, architecture, or design; those live in the documents above and are referenced by section.
>
> **Conventions describe how to do work. They do not decide unresolved questions.** Open decisions in `DECISIONS.md` Section 4 are not conventions; they are unresolved. Where a convention touches an open decision, the open status is called out explicitly and the convention stops short of deciding it.

---

## 0. How to Use This Document

- Read this document before writing any code on this project.
- Read it alongside `AGENT.md`, which governs workflow, reporting, and decision authority.
- Where this document and another project document conflict, stop and report the conflict per `AGENT.md` §16. Do not choose the easier interpretation.
- Conventions govern implementation style and structure within an approved design. They do not give permission to decide architecture, requirements, or visual values.
- These conventions apply to Claude Code, Antigravity, and every other contributor or automated tool.

---

## A. General Coding Principles

### A.1 Readability and Focus

Write code that communicates its intent without demanding that the reader hold more context than the code provides. Prefer straightforward solutions over clever ones. Prioritize correctness and reliability; optimize only when a measured problem requires it.

Every code change must have a reason. No drive-by refactors, reformatting, renames, or file moves outside the assigned task (`AGENT.md` §5). If an improvement is noticed outside the task, record it in the completion report and leave it for a later assignment.

### A.2 Avoid Unnecessary Abstractions

Add an abstraction only when it serves the current release. Do not introduce interfaces, base classes, design patterns, or placeholder implementations for features that are not yet in scope. The adapter pattern described in `ARCHITECTURE.md` §3.3 is a required architectural boundary, not an invitation to invent further layers.

### A.3 No Dead Code

Commented-out code is not acceptable as a commit artifact. Remove it. If a path is not yet implemented, let it fail visibly or do not expose it in the UI (`AGENT.md` §5). List intentional gaps in the completion report.

### A.4 No Silent No-Ops

A method that silently does nothing when it fails to do its work is a hidden bug. An unimplemented code path must throw or return a meaningful error, not silently succeed. Exceptions to this rule (for example, a defensive no-op in a finalizer) must be explained in a comment.

### A.5 Smallest Focused Change

Each commit, pull request, and task implementation makes the minimum change that satisfies the acceptance criteria. It does not reach ahead to other stories or releases.

---

## B. C# Conventions

### B.1 Namespaces

The root namespace for the project is `IslandTaskbar`. Do not rename it (`BRAND.md` §2; `AGENT.md` §5). Namespace segments follow the folder structure defined in `ARCHITECTURE.md` §3.4 (and later `FILE_STRUCTURE.md`).

```csharp
// Root namespace
namespace IslandTaskbar;

// Layered sub-namespaces
namespace IslandTaskbar.Presentation.Windows;
namespace IslandTaskbar.ViewModels;
namespace IslandTaskbar.Services;
namespace IslandTaskbar.Adapters.Interfaces;
namespace IslandTaskbar.Adapters.Implementations;
namespace IslandTaskbar.Models;
namespace IslandTaskbar.Recovery;
```

### B.2 Type Names

| Construct | Convention | Example |
|---|---|---|
| Classes | `PascalCase` | `PinManagerService` |
| Interfaces | `IPascalCase` | `IPinManagerService` |
| Enumerations | `PascalCase` | `IslandState` |
| Enum members | `PascalCase` | `IslandState.Collapsed` |
| Records | `PascalCase` | `PinnedApp` |
| Delegates | `PascalCase` with `Handler` or `Callback` suffix where useful | `WindowChangedHandler` |
| Exception classes | `PascalCase` with `Exception` suffix | `TaskbarHideException` |

### B.3 Member Names

| Construct | Convention | Example |
|---|---|---|
| Public properties | `PascalCase` | `DisplayName` |
| Public methods | `PascalCase` | `LaunchApplicationAsync` |
| Private fields | `_camelCase` with leading underscore | `_pinOrder` |
| Constants | `PascalCase` | `MaxPinnedApps` |
| Local variables | `camelCase` | `windowHandle` |
| Parameters | `camelCase` | `launchReference` |
| Event names | `PascalCase` | `WindowActivated` |
| Event handler parameters | `sender`, `e` | `OnWindowActivated(object sender, WindowEventArgs e)` |

Do not use Hungarian notation, type prefixes, or abbreviations that require knowledge of the domain to decode.

### B.4 Access Modifiers

Declare the minimum access a member requires. Default to `private`. Promote to `internal` only when the member is used across the same assembly but must not be public. Promote to `public` only when the member forms part of an interface or is required by the XAML or WinUI binding system.

Always state the access modifier explicitly. Do not rely on the C# default of `private` for class members, because an omission is indistinguishable from a deliberate choice when reading code.

### B.5 Nullable Reference Types

Enable nullable reference types for the project. Annotate reference types accurately: use `?` for parameters and return types that may be null, and treat a non-annotated type as guaranteed non-null. Suppress the nullable warning (`!`) only when you have verified the value cannot be null at that point; include a comment explaining why.

Do not use `#nullable disable` to silence warnings in bulk. Resolve nullable warnings correctly.

### B.6 Async and Await

Use `async`/`await` end to end. Do not block the UI thread. The following are forbidden on the UI thread:

- `.Result`
- `.Wait()`
- `.GetAwaiter().GetResult()`

Use `async void` only for WinUI event handlers where the signature is required by the framework. In all other cases, return `Task` or `Task<T>`.

Pass a `CancellationToken` parameter to every public async method that performs I/O, long computation, or any operation that might be cancelled (`AGENT.md` §6). Propagate it into called async methods.

Name async methods with the `Async` suffix: `LoadPinsAsync`, `RestoreTaskbarAsync`.

### B.7 Exception Handling

Handle exceptions at the boundary where recovery is possible, not everywhere. Adapter implementations catch Windows API failures and either translate them to meaningful exceptions or return a failure result, depending on the calling contract. Services handle failures and avoid crashing the taskbar or stranding the native taskbar hidden (`AGENT.md` §12).

Do not swallow exceptions silently. If an exception cannot be handled at a boundary, re-throw it or log it with its full context before the process terminates.

Do not use exceptions for flow control. Use return values or discriminated result types for expected failure cases (for example, an application that fails to launch is an expected case, not an exception scenario).

User-facing errors are concise and actionable. They do not expose stack traces, internal identifiers, Win32 error codes, or secrets (`AGENT.md` §12; US-029).

### B.8 Resource Disposal

Implement `IDisposable` on any type that holds native handles, COM objects, subscriptions, or other unmanaged resources. Use `using` declarations and `using` blocks at the call site. Release event subscriptions explicitly. Release COM objects and native handles according to their documented contracts.

Unsubscribe from events in `Dispose` to prevent memory leaks from event handler retention. Timers, watchers, and other disposable periodic objects belong to a service's lifetime and must be stopped and disposed when the service is disposed.

### B.9 Comments and XML Documentation

Comments explain **why**, not **what**. If the what is not obvious, refactor the code until it is.

Reference story IDs (US-xxx), ADR IDs (D-xxx, OD-xxx, ADR-xxx), and design token names in comments where useful. For example:

```csharp
// US-003: record prior state before hiding so restore can undo exactly what we changed.
// ADR-008: exact mechanism unvalidated — see SPIKE-001 result.
// Using Space.Compact (8 DIP) per DESIGN_SYSTEM.md §3.3.
```

Annotate provisional values used while an open decision is unresolved:

```csharp
// PROVISIONAL — OD-018: hover intent delay. Replace when interaction behavior is decided.
private static readonly TimeSpan HoverIntentDelay = TimeSpan.FromMilliseconds(300);
```

XML documentation (`///`) is required for public types and members that form part of an interface or adapter contract. It is optional for private implementation details.

### B.10 Formatting and Indentation

Follow existing code style in the repository. Do not introduce `.editorconfig`, code analyzers, formatter packages, or bulk reformatting without approval (`DECISIONS.md` OD-004; `AGENT.md` §5).

The following rules apply until OD-004 is resolved:

- 4 spaces per indent level (not tabs).
- Opening brace `{` on its own line for methods, classes, and namespaces (Allman style).
- One blank line between method bodies.
- No trailing whitespace.
- Keep lines to a readable length. There is no hard limit, but lines longer than 120 characters should be wrapped.
- One `using` directive per line, grouped by namespace (system, then third-party, then project).

Formatting decisions that require an `.editorconfig` or analyzer rule remain open (OD-004). Do not change line endings or indentation across files you are not otherwise modifying.

---

## C. WinUI 3 and XAML Conventions

### C.1 XAML Naming

Name XAML elements only when they are referenced from code-behind. Use `x:Name` with `PascalCase` and a suffix that describes the control type:

```xml
<Button x:Name="ExpandIslandButton" ... />
<TextBlock x:Name="ClockTextBlock" ... />
<ListView x:Name="PinnedAppsListView" ... />
```

Do not name elements that are addressed only through data binding and do not need a code-behind reference.

### C.2 Resource Dictionaries and Shared Styles

Define colors, brushes, corner radii, and spacing in XAML resource dictionaries that mirror the tokens in `DESIGN_SYSTEM.md` §3–§6. The resource key names must match the token names exactly. For example:

```xml
<SolidColorBrush x:Key="Surface.IslandFallback" Color="#17171B" />
<CornerRadius x:Key="Radius.Island" TopLeft="0" TopRight="0" BottomRight="22" BottomLeft="22" />
```

No hardcoded hex color values or raw numeric corner radii are permitted anywhere outside the token definition resource file (`DESIGN_SYSTEM.md` §0; `AGENT.md` §8).

### C.3 Separation of UI and Application Logic

The presentation layer renders state and routes user actions. It does not contain business logic, Windows integration, or data access. Follow the MVVM layering defined in `ARCHITECTURE.md` §3:

- **Views (XAML + code-behind):** bind to view model properties and commands. Code-behind contains only view-specific logic (focus management, element animation triggers, event routing that XAML cannot express).
- **ViewModels:** hold UI state, commands, and presentation logic. Call application services. Do not call adapter interfaces directly. Do not call Win32.
- **Services:** own product behavior and coordinate adapter interfaces.
- **Adapters:** encapsulate Windows API calls behind narrow interfaces.

### C.4 Code-Behind Boundaries

Code-behind for a XAML file may contain:

- Event handlers that cannot be expressed as bindings.
- Animation or visual state logic that is view-specific.
- Focus management.
- Calls to view model methods or commands, never to services or adapters.

Code-behind must not contain:

- Business logic.
- Win32 or Windows API calls.
- Persistence reads or writes.
- Application lifecycle decisions.

### C.5 Data Binding and UI State

Prefer one-way bindings. Use two-way bindings only where the UI genuinely drives state changes (settings controls, editable fields). Do not use code-behind to push values into XAML elements when a binding would work.

UI-bound state must be updated on the WinUI dispatcher thread (`ARCHITECTURE.md` §9). Marshal results from background threads using `DispatcherQueue.TryEnqueue` or the equivalent WinUI mechanism before modifying bound properties.

Do not access UI elements from background threads.

### C.6 Accessibility and Keyboard Support

Every interactive control must have an accessible name that describes its purpose to a screen reader, not just its visual appearance. Use `AutomationProperties.Name` where the control's label is not visible:

```xml
<Button AutomationProperties.Name="Expand taskbar" .../>
```

Essential actions must be reachable by keyboard without hover (`DESIGN_SYSTEM.md` §12; `AGENT.md` §8). Tab order must follow a logical visual sequence. Focus indicators must be visible.

The `Design Acceptance Checklist` in `DESIGN_SYSTEM.md` §15 must pass before any component is marked ready.

### C.7 Design Token Usage

Reference token names in XAML resources and in code comments. Never reference a raw hex value or a raw numeric constant for a visual measurement outside the token definition file:

```xml
<!-- Correct: resource key matches the token name -->
<Border Background="{StaticResource Surface.IslandFallback}"
        CornerRadius="{StaticResource Radius.Island}" />

<!-- Wrong: hardcoded value -->
<Border Background="#17171B" CornerRadius="22" />
```

Refer to spacing tokens in layout: `Space.Compact` (8 DIP), `Space.Standard` (12 DIP), and so on (`DESIGN_SYSTEM.md` §3.3). All layout values use DIPs; physical pixels are used only at Win32 interop boundaries.

---

## D. Windows API and Interop Conventions

### D.1 Adapter Pattern (Required)

Every Windows API call goes through an adapter interface from `ARCHITECTURE.md` §3.3. No exceptions. The presentation layer and view models never call Win32 or WinRT system APIs directly. Services depend on adapter interfaces, not implementations.

Required adapter interfaces for Release 1:

| Interface | Responsibility |
|---|---|
| `ITaskbarVisibilityController` | Hide and restore native taskbar windows |
| `IWindowSystem` | Enumerate, inspect, restore, and activate application windows |
| `IApplicationLauncher` | Launch applications and Windows experiences |
| `IDisplayService` | Monitor and DPI information |

Adapters for later releases are built only when their release begins. Do not add placeholder implementations now (`ARCHITECTURE.md` §3.3).

### D.2 CsWin32 for P/Invoke

Win32 P/Invoke declarations come from CsWin32-generated source only. Do not write handwritten `[DllImport]` declarations scattered across the codebase (`ARCHITECTURE.md` §0; `AGENT.md` §7). Centralize any additional declaration requests in the CsWin32 configuration file.

The exact CsWin32 version remains open (OD-023). Do not pin a version without resolving that decision first.

### D.3 Native Types, Structures, and Handle Ownership

Use the types that CsWin32 generates. Do not cast away from the generated safe handle types without a documented reason. Respect documented handle ownership and lifetime: close handles you open, do not close handles you do not own.

Validate window handles and process identifiers before acting on them. A handle can become invalid between discovery and use. Check return values and Windows error codes for every Win32 call. Handle the failure path without crashing the taskbar.

### D.4 Error Handling at Interop Boundaries

Translate Win32 error codes into meaningful exceptions or result types at the adapter implementation boundary. Services and view models must not interpret raw `HRESULT` values or error codes — the adapter hides that detail.

Do not silently ignore a failed Win32 call. At minimum, log the failure with the operation name and the error code.

### D.5 Windows Version Compatibility

Build `26300.9974` is the development and test target, not a support promise (`ENV.md` §2). Check runtime availability before calling Windows APIs that may not exist on older builds. The minimum supported build is not yet decided (OD-015). Do not assume the newest API is available on all supported builds.

### D.6 Undocumented APIs

Using an undocumented or internal shell behavior requires Chief Engineer approval and a `DECISIONS.md` entry (`AGENT.md` §7; D-003). Do not call undocumented Explorer internals. Do not inject into or hook `explorer.exe`.

### D.7 Native Taskbar Hiding

The exact mechanism is **unvalidated** (ADR-008, OD-008, SPIKE-001). Until SPIKE-001 is recorded and its decision is approved, do not assume a mechanism. Any code that hides the native taskbar must:

- Record the prior state before changing it.
- Restore that prior state on exit.
- Never substitute auto-hide for a true hide (D-002; `ARCHITECTURE.md` §5.1).
- Re-discover taskbar window handles after an Explorer restart.
- Never run unattended or in a loop (D-012; `AGENT.md` §11).
- Not run at all until a manual recovery procedure is documented (D-010; `AGENT.md` §11).

---

## E. Architecture and Component Boundaries

### E.1 Follow the Approved Layer Structure

Follow the layer structure in `ARCHITECTURE.md` §3 exactly:

```
Presentation (XAML / code-behind)
    ↓ binds to
ViewModels
    ↓ calls
Services (application logic)
    ↓ calls via interface
Adapters → Windows / OS
```

No layer may skip to a lower layer. The presentation layer does not call services directly where a view model is the correct intermediary. Services do not call Windows APIs directly; they use adapter interfaces.

### E.2 Folder and Namespace Alignment

Use the folder sketch in `ARCHITECTURE.md` §3.4 until `FILE_STRUCTURE.md` exists:

```
IslandTaskbar/
├── App.xaml / App.xaml.cs
├── Presentation/
│   ├── Windows/
│   ├── Controls/
│   └── Converters/
├── ViewModels/
├── Services/
├── Adapters/
│   ├── Interfaces/
│   └── Implementations/
├── Models/
├── Recovery/
└── Assets/
```

Do not invent a different structure. If the sketch does not cover a new file's correct location, file a Decision Request before creating the file.

### E.3 Island Window Controller

D-009 requires an island window controller. Its form, name, interface, and layer placement are open (OD-011). Do not implement topmost, anchoring, sizing, hit-test, or display-change Win32 logic until OD-011 is resolved. Ask the Chief Engineer for the architecture update.

### E.4 Services and Single Responsibility

Each service in `ARCHITECTURE.md` §3.2 owns a defined set of responsibilities and no more. Do not add responsibilities to an existing service when a new responsibility does not belong to it. If a new service is needed, raise a Decision Request before creating it.

### E.5 No Speculative Work

Build only what the current assigned story requires. Do not add abstractions, adapter stubs, or service skeletons for future releases. Future releases will add what they need when they start (`AGENT.md` §5; `ARCHITECTURE.md` §0).

---

## F. Local Storage Conventions

### F.1 JSON Files Only

User preferences, pins, and layout settings are stored as versioned JSON files in the per-user application data directory. No database, embedded database engine, or cloud backend is permitted (`DATABASE_SCHEMA.md` §0; D-005).

The exact application folder name under the data directory is not yet defined (OD-006). Do not choose a folder name. Do not store data beside the installed binaries.

### F.2 Schema Versioning

Every JSON settings file includes a `schemaVersion` integer field at the top level from day one (`DATABASE_SCHEMA.md` §4). Start at version 1. Increment the version number whenever the schema changes in a way that requires migration. Write migration logic before writing the new schema.

### F.3 Atomic Writes

Always write settings files atomically: write to a temporary file in the same directory, then replace the target file using an atomic file-replace operation. Do not write directly to the target file. This reduces the risk of corruption from a crash mid-write (`DATABASE_SCHEMA.md` §5; `ARCHITECTURE.md` §7).

### F.4 Validation and Safe Defaults

Validate every value read from a settings file. Do not trust that the file contains well-formed data or that fields are present. Fall back to a documented safe default for any missing or unrecognized field. Core taskbar functionality must remain available when optional settings are missing or corrupt (`DATABASE_SCHEMA.md` §5).

### F.5 New Files and Fields

Do not create a new settings file or add a new field without an approved update to `DATABASE_SCHEMA.md` §4. The only files currently defined are `settings.json`, `pins.json`, and `recovery.json` (conditional). The first-run completion field is not yet defined (OD-012); do not add it until `DATABASE_SCHEMA.md` is updated.

### F.6 No Sensitive Data

Do not store credentials, tokens, API keys, or personal content in settings files (`DATABASE_SCHEMA.md` §7; `AGENT.md` §10). No telemetry or remote synchronization is permitted.

---

## G. Dependencies and Packages

### G.1 Minimum Necessary Dependencies

Add a package only when it provides a capability the project genuinely needs and that cannot reasonably be built in-house without significant cost. The dependency must have a documented reason before it is added (`AGENT.md` §5; `ENV.md` §0).

No package may be added while performing a task that only calls for coding conventions to be documented.

### G.2 Stable Releases Only

Use only stable package releases. Do not add Preview, Experimental, or Release Candidate packages in production paths without an explicit decision recorded in `DECISIONS.md` (`ENV.md` §0, §11; `AGENT.md` §5).

### G.3 Approved NuGet Source

Use the standard NuGet source (`nuget.org`) unless a specific, approved dependency requires another source. Do not use private or mirror sources without approval.

### G.4 Version Pinning

Pin every package to an explicit version. Do not use floating version ranges (`*`, `>=`) in production project files. After any package change, run `dotnet restore` before `dotnet build` (`ENV.md` §0).

### G.5 Platform Dependency Upgrades

Upgrade one platform dependency at a time. After upgrading, run restore, build, automated tests (once a framework exists), and relevant Windows integration checks. Record all material version changes in `CHANGELOG.md` and `DECISIONS.md` (`ENV.md` §11).

The approved platform baseline is:

| Item | Approved version |
|---|---|
| .NET SDK | `10.0.404` (pinned in `global.json`) |
| Windows App SDK | `2.7.0` stable |
| Target architecture | `x64` initially |

Do not change these without a written decision entry and Product Owner approval (`ENV.md` §0).

---

## H. Error Handling and Logging

### H.1 Fail Safely

A failure in a non-essential feature must not crash the taskbar or strand the native taskbar hidden. Handle errors at adapter and service boundaries. Keep lifecycle-critical behavior—especially native taskbar hiding and restoration—isolated from optional feature failures (`AGENT.md` §12).

### H.2 No Silent Failures

Do not catch and discard exceptions unless a defensive no-op is documented and justified. If an operation cannot succeed, the caller must know. Use a meaningful exception type or a result type that communicates the failure.

### H.3 User-Facing Errors

User-facing error messages are:

- **Short and actionable.** Tell the user what happened and what they can do.
- **Private.** No stack traces, raw error codes, internal identifiers, file paths, or secrets.
- **Honest.** Do not say "try again" if retrying cannot succeed. Do not imply a feature works when it does not (US-029; `BRAND.md` §6).

### H.4 Diagnostic Logging

Log meaningful lifecycle events and failures locally. At minimum, log:

- Application start and shutdown.
- Native taskbar hide and restore operations and their outcomes.
- Adapter-level Windows API failures (operation name, error code, context).
- Recovery-path events.
- Unexpected exceptions that are caught at a top-level boundary.

Do not log personal content, window contents, or anything that could identify the user's private activities. If telemetry is ever considered, it must go through the decision process (OD-022); the current default is no telemetry.

**Logging framework selection is an open engineering decision.** Do not select or add a logging framework without approval (OD-022; `AGENT.md` §5). Write log output through an interface so the implementation can be replaced when the decision is made.

### H.5 Resource Cleanup on Failure

Ensure resources are cleaned up even on the failure path. Use `try`/`finally` blocks to release handles and restore Windows state when a `using` block cannot span the full lifetime. The native taskbar's prior state must be restored even if the application exits with an unhandled exception.

---

## I. Testing Conventions

> These conventions govern how tests are structured and reported. The testing framework is not yet approved (OD-003). Until OD-003 is resolved, all execution is manual. Do not add a test framework package without approval.

### I.1 Test Classification

Every test carries two labels (from `TESTING.md` §2.1):

- **Class:** `S` (Safe — does not hide the native taskbar) or `H` (Hiding — hides the native taskbar, requires authorization under `TESTING.md` §6).
- **Exec:** `M` (Manual only) or `M/A` (Manual now, automation-eligible after OD-003).

No test is marked `Passed` unless it was executed on the target machine (Windows 11, build `26300.9974`), the expected result was observed, and evidence was recorded. The default status is `Not run` (`TESTING.md` §0).

### I.2 Test Naming

Name tests using a convention that communicates what is being tested and what the expected outcome is. Until a framework is approved, use the description format in the test register:

```
<Component>_<Scenario>_<ExpectedOutcome>
```

Examples: `PinManager_AddPin_PersistsAfterReload`, `SettingsService_CorruptFile_FallsBackToDefaults`.

### I.3 Deterministic Tests

Tests must not depend on the state of a previous test, on the current time, on file system state left over from other tests, or on network connectivity. Use temporary directories for file-system tests and clean them up after each test run.

Do not rely on timing in tests. Do not use `Thread.Sleep` to wait for async operations; use proper async coordination.

### I.4 Adapter Fakes for Unit Tests

Unit and integration tests use fake implementations of adapter interfaces to avoid calling real Windows APIs. A fake is a simplified, controlled implementation of the interface (`TESTING.md` §2). It is not a mock or stub library dependency; write fakes by hand until a framework is approved. Fakes live in the test project, not the production project.

A fake that simulates adapter behavior for testing purposes is distinct from mock functionality shown in the product UI. Only the former is permitted (`AGENT.md` §9; `TESTING.md` §0).

### I.5 Real Data Required for Acceptance

Acceptance criteria require real data from Windows. Tests that use fake adapter implementations do not satisfy acceptance criteria for user stories. Only manual acceptance testing on the real target machine satisfies release acceptance (`TESTING.md` §1; `USER_STORIES.md` §7).

### I.6 Honest Reporting

Do not report unexecuted tests as passing. Do not weaken, skip, or reinterpret acceptance criteria to obtain a pass. If a test is blocked, state why (`TESTING.md` §0). Report partial runs as partial.

Performance claims (idle CPU under 1%, idle memory under 150MB, 60fps) require a measured result with the method described. Do not state performance figures from memory or by estimation (`AGENT.md` §11).

---

## J. Git and Change Management

### J.1 Small, Focused Commits

Each commit represents one logical change. The commit message states what changed and why. Reference story IDs (US-xxx) and decision IDs (D-xxx, OD-xxx) where useful:

```
Implement PinManagerService atomic write (US-007)

Write pins.json via a temporary file to reduce corruption risk.
Follows DATABASE_SCHEMA.md §5 atomic write requirement.
```

The build must pass before a commit is made (`AGENT.md` §13).

### J.2 Restricted Actions (Require Explicit Approval)

The following Git operations require explicit task-level approval before being performed (`AGENT.md` §13):

- `git push` to any remote branch.
- `git push --force` or `git push --force-with-lease`.
- `git rebase` on a shared branch.
- `git reset --hard`.
- `git clean -f` or `git clean -fd`.
- Deleting a local or remote branch or tag.
- Rewriting history in any form.

Do not perform these operations without the task explicitly saying to do so.

### J.3 No Unrelated Changes

Do not modify files unrelated to the assigned task. No mass reformatting, line-ending normalization, or whitespace cleanup across the codebase (`AGENT.md` §13). If cleanup is needed, record it in the report and wait for a separate assignment.

### J.4 No Secrets in Commits

Never commit access tokens, API keys, credentials, passwords, or machine-specific absolute paths. Do not add `.env` files (`ENV.md` §7).

### J.5 Branching and Commit Message Convention

A formal branching strategy and commit-message convention are not yet defined (OD-005). Until OD-005 is resolved, follow these interim rules:

- Small, focused, reviewable commits with descriptive messages.
- One logical change per commit.
- Include the story ID or decision ID in the message where relevant.
- Do not push, force-push, or rewrite history without explicit approval (D-011).

### J.6 Platform and Package Changes

One dependency or platform change at a time. After any such change: restore, build, run tests, and perform relevant Windows integration checks. Record the change in `CHANGELOG.md` and `DECISIONS.md`. Those files do not exist for a new change? Put the entry text in the completion report and ask (`AGENT.md` §13).

---

## K. Documentation Conventions

### K.1 Markdown Style

Project documents are Markdown files. Use ATX headings (`#`, `##`, `###`). Use a single blank line between sections. Use fenced code blocks with a language identifier. Use tables for structured comparisons. Keep line lengths reasonable (soft limit: 100 characters in prose; tables and code may be longer).

### K.2 Document Headers

Every project document begins with a standard header block:

```markdown
# Document Title

**Project:** IslandTaskbar  
**Document status:** Draft v0.x  
**Last updated:** YYYY-MM-DD  
**Depends on:** (list of documents this one builds on)
```

### K.3 Code Examples

Code examples in documentation are illustrative, not production code. Mark them clearly. Use realistic but not actual file paths, GUIDs, or identifiers. Do not use code examples to introduce requirements or decisions that have not been approved.

### K.4 Linking Between Documents

Reference related sections using the format `\`DOCUMENT.md\` §section`. Do not use relative file links that may break when documents are reorganized. When a decision or requirement moves to a new document, update all references in the same change.

### K.5 Decision IDs in Comments and Documents

When a code comment or document statement refers to a specific approved or open decision, cite its ID: D-001 through D-012 for approved decisions, OD-001 through OD-023 for open ones, and ADR-xxx for architecture decisions in `ARCHITECTURE.md` §13. This helps future readers trace a convention back to its authority.

### K.6 Recording Changes

Approved decisions are changed by adding a new `DECISIONS.md` entry that supersedes the old one. Do not rewrite the decision statement of an approved entry in place. Changes to conventions in this document follow the same process: propose, get approval, then update. Record material changes in `CHANGELOG.md` once it exists.

### K.7 Maintaining Document Accuracy

When a convention or project document becomes inaccurate because a decision was made or a document was corrected, the stale document is flagged in a completion report or Decision Request. Do not silently leave a document in conflict with an approved decision. Known conflicts are tracked in `DECISIONS.md` §6.

---

## L. Open Decisions That Affect These Conventions

The following decisions in `DECISIONS.md` Section 4 are unresolved. Conventions in the sections above stop short of deciding them. Do not decide them. File a Decision Request (`AGENT.md` §16) if work is blocked.

| Decision | Sections affected | Rule until resolved |
|---|---|---|
| OD-001 — Animation model and timing | Motion code and XAML transitions | No motion work. See `DESIGN_SYSTEM.md` §9 and `AGENT.md` §8. |
| OD-002 — MVVM library and DI approach | B, C, E | Follow layer structure in `ARCHITECTURE.md` §3. Add no MVVM or DI package. |
| OD-003 — Testing frameworks and measurement strategy | I | All test execution is manual. Add no test framework package. |
| OD-004 — Code formatting and analyzer configuration | B.10 | Follow existing code style. Add no `.editorconfig` or analyzer package. |
| OD-005 — Git branching and commit conventions | J.5 | Small, focused commits with descriptive messages. D-011 applies in full. |
| OD-006 — Application-data folder name and exact paths | F.1 | Do not choose a folder name. Do not store data beside the binaries. |
| OD-011 — Island window controller form | D.1, E.3 | Do not implement anchoring, topmost, hit-test, or display-change Win32 logic. |
| OD-012 — First-run state storage and content | F.5 | Do not add a file or field. Wait for `DATABASE_SCHEMA.md` update. |
| OD-022 — Telemetry and diagnostics | H.4 | No telemetry. Local logging only, no personal content. Write through an interface. |
| OD-023 — Exact package/SDK versions and toolchain verification | G.4, B.10 | Run checks in `ENV.md` §5 before choosing a CsWin32 version or TFM. |

---

## M. Documentation Conflicts Found During Authoring

The following conflicts and gaps were observed during inspection of the referenced documents. These are pre-existing conflicts already recorded in `DECISIONS.md` §6; they are listed here for completeness. This document does not resolve them.

| Ref | Conflict | Source | Interim handling |
|---|---|---|---|
| C-01 | `PRD.md` §7 and `SCOPE.md` §5 say .NET 8. D-004 and `ARCHITECTURE.md` §2 say .NET 10 LTS. | `DECISIONS.md` C-01 | Use .NET 10. |
| C-08 | Spring-based motion (`PRD.md` §6.1, US-005) versus no spring (`DESIGN_SYSTEM.md` §0, §9). | `DECISIONS.md` C-08 | No motion work until OD-001 is resolved. |
| C-09 | CsWin32 described as "preferred," "required," and "or another maintained source-generated approach" across different documents. | `DECISIONS.md` C-09 | Use CsWin32. Introduce no alternative without a Decision Request. |

---

**Document owner:** Product Owner  
**Engineering partner:** Chief Engineer  
**Change control:** Conventions may be proposed by any contributor. Changes require Chief Engineer review and Product Owner approval for decisions that affect architecture, style tooling, or approved packages. Record approved changes in `DECISIONS.md` and `CHANGELOG.md`. Agents propose changes via Decision Request (`AGENT.md` §16). Do not edit this document unless the task assigns it.

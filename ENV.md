# ENV.md

**Project:** IslandTaskbar  
**Document status:** Draft v0.2  
**Last updated:** 2026-11-19  
**Depends on:** `ARCHITECTURE.md`, `DATABASE_SCHEMA.md`

---

## 0. AI Agent Rules (Read First)

> **These rules apply to every AI agent, coding assistant, or automated tool working on this project.**

### The agent MUST NOT:

- Change the .NET version — this project uses **.NET 10 LTS** (`10.0.404`). Do not suggest .NET 8, .NET 9, or .NET 11.
- Change the Windows App SDK version — use **2.7.0 stable**. Do not use Preview or Experimental releases.
- Add a Docker container, Node.js runtime, Python environment, or any non-.NET runtime dependency.
- Add a `.env` file — this project has no backend, no API keys, and no cloud credentials.
- Add environment variables for features that do not exist yet.
- Commit machine-specific absolute paths to any project file.
- Use a NuGet source other than `nuget.org` unless explicitly approved.
- Adopt a Preview or Experimental package without a written decision in `DECISIONS.md`.
- Upgrade any platform dependency without updating `CHANGELOG.md` and `DECISIONS.md`.
- Treat a successful `dotnet build` as proof the feature works — UI and Windows integration must be tested on the real machine.

### The agent MUST:

- Use the `global.json` file (Section 6) to pin the .NET SDK version.
- Pin `Microsoft.WindowsAppSDK` to `2.7.0` in the project file.
- Target `x64` for all initial builds.
- Run `dotnet restore` before `dotnet build` after any package change.
- Never commit secrets, tokens, or credentials of any kind.

---

## 1. Purpose

This document defines the development-machine requirements, SDK baseline, tools, build checks, and environment configuration for IslandTaskbar.

IslandTaskbar is a local-first Windows desktop application. This file describes the **developer environment**, not a cloud deployment environment.

---

## 2. Confirmed Development Target

| Item | Value | Status |
|---|---|---|
| Operating system | Windows 11, version 26H2 | Confirmed by the Product Owner |
| OS build | `26300.9974` | Confirmed by the Product Owner |
| Initial build architecture | `x64` | Selected project baseline |
| Product minimum supported Windows build | Not decided | Must be established through compatibility testing |

Build `26300.9974` is the initial validation target. It does not mean that other Windows 11 builds are already supported.

---

## 3. Required Development Toolchain

Use stable, supported tools. The versions below are the project's baseline as of this document's update date; review official release notes before intentionally upgrading them.

| Component | Baseline | Purpose |
|---|---|---|
| .NET SDK | `10.0.404` | Build and run the C# application |
| .NET runtime servicing level | `10.0.15` | Runtime for November 2026 .NET 10 servicing release |
| Windows SDK | `10.0.26100.0` family or later | Compile-time Win32, WinRT, and COM declarations |
| Windows App SDK | `2.7.0` stable | WinUI 3 and Windows application platform APIs |
| UI framework | WinUI 3 | Native Windows interface |
| Programming language | C# | Application implementation |
| Native interop | CsWin32 | Source-generated Win32 declarations |
| Target build | `x64` initially | Keep the first build/test matrix focused |

> **Important:** The Windows SDK, Windows App SDK, and Windows OS are separate versioned components. Installing one does not replace the others. The Windows SDK version determines which platform APIs are available at compile time; the actual Windows build determines which APIs exist at runtime. Check runtime availability before calling newer Windows APIs.

---

## 4. IDE and Developer Tools

### Primary

- **Antigravity** — Primary AI-assisted coding environment for editing, reviewing, building, and debugging the local project.

  **Setup steps for Antigravity:**
  1. Open Antigravity and point it at the root of the cloned repository folder.
  2. Ensure the terminal inside Antigravity can run `dotnet --version` and returns `10.0.404` or the pinned SDK version from `global.json`.
  3. Load `AGENT.md` (once created) at the start of every session so the AI has the project rules in context.
  4. Load `ARCHITECTURE.md` and `TASKS.md` at the start of every session so the AI knows the current state.
  5. Do not start coding without confirming the build passes (`dotnet build -c Debug -p:Platform=x64`).

- **Git** — Source control. Commit in small, meaningful changes with descriptive messages.
- **PowerShell / Windows Terminal** — Run SDK checks, Git commands, restore, build, and test commands.
- **NuGet** — Restore .NET and Windows App SDK packages.

### Recommended

- **Visual Studio 2026** with the **WinUI application development** workload — useful for diagnosing XAML and build issues even when Antigravity is the primary editor.
- Enable Windows **Developer Mode** on the development machine when required by the WinUI development and deployment workflow.

> Google AI Studio / Gemini Hydrogen may be used to generate or review code but is an optional development assistant — not a runtime dependency, SDK, or build requirement.

---

## 5. Initial Machine Checks

Run these commands in PowerShell after installing the toolchain:

```powershell
# Confirm the Windows edition and OS build
winver

# Confirm installed .NET SDKs and runtimes
dotnet --list-sdks
dotnet --list-runtimes

# Show the active .NET environment
dotnet --info

# Confirm Git is available
git --version
```

Expected .NET SDK baseline: `10.0.404` or a deliberately approved later .NET 10 servicing SDK.

---

## 6. Repository-Level Environment Configuration

### `global.json` (place in repository root)

Create this file when the repository is initialized. It pins the .NET SDK so every developer and build machine uses the same version.

```json
{
  "sdk": {
    "version": "10.0.404",
    "rollForward": "latestPatch"
  }
}
```

> `rollForward: latestPatch` allows the latest patch of `10.0.4xx` but will not silently jump to `10.0.5xx` or higher. Update this file intentionally when the team approves a newer SDK.

### Project file requirements

When the solution is scaffolded, the `.csproj` file must include:

```xml
<!-- Pin Windows App SDK version -->
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.7.0" />

<!-- CsWin32 for Win32 interop declarations -->
<PackageReference Include="Microsoft.Windows.CsWin32" Version="..." PrivateAssets="all" />

<!-- Target framework — exact TFM to be confirmed at scaffolding -->
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
```

Additional setup rules:

1. Use the standard NuGet source (`nuget.org`) unless a dependency requires another approved source.
2. Do not add machine-specific absolute paths to committed project files.
3. Record the Windows SDK target and minimum supported OS version in the project file — minimum build remains open until compatibility testing.

---

## 7. Environment Variables and Secrets

**No `.env` file is currently required.**

The current product has:

- No backend service.
- No database connection.
- No API keys or cloud credentials.
- No mandatory internet service at runtime.

Do not invent environment variables or add secrets for features that do not need them. If a future feature introduces a credential or external service, document its purpose, storage method, permissions, and local-development setup before implementation. **Never commit secrets.**

---

## 8. Local Application Data

User preferences and pinned-app configuration are stored locally under the current user's application-data directory, not beside the installed executable. The exact application folder name and JSON file names are defined in `DATABASE_SCHEMA.md` and will be finalized in `FILE_STRUCTURE.md`.

No database server, SQL tooling, Docker service, Node.js runtime, or cloud account is required for the current application baseline.

---

## 9. Build and Verification Commands

```powershell
# Restore NuGet dependencies
dotnet restore

# Build the initial x64 Debug configuration
dotnet build -c Debug -p:Platform=x64

# Build Release configuration
dotnet build -c Release -p:Platform=x64

# Run automated tests (after test projects exist)
dotnet test -c Debug

# Clean build outputs
dotnet clean
```

> **A successful build is not proof that the taskbar works correctly.** UI behavior and Windows integration must also be tested on the target Windows machine. Do not report a feature as verified unless it has actually been run and tested on Windows 11 build `26300.9974`.

---

## 10. Session Start Checklist

> Run through this before every coding session. Takes less than 2 minutes.

```
[ ] Antigravity is open and pointed at the repository root
[ ] dotnet --version returns 10.0.404 (or pinned SDK version)
[ ] git status is clean or you know what is in progress
[ ] AGENT.md loaded in context (once it exists)
[ ] ARCHITECTURE.md and TASKS.md loaded in context
[ ] dotnet build passes before writing new code
```

---

## 11. Upgrade Policy

- Prefer supported .NET LTS releases for the production baseline unless an approved technical reason requires otherwise.
- Use the latest stable Windows App SDK release only after reviewing release notes and building/testing against it.
- Do not adopt Preview or Experimental packages in production paths without an explicit decision recorded in `DECISIONS.md`.
- Upgrade one platform dependency at a time and run restore, build, automated tests, and relevant Windows integration checks.
- Record all material version changes in `CHANGELOG.md` and `DECISIONS.md`.

---

## 12. Open Environment Items

| Item | Current status |
|---|---|
| Actual installed .NET SDK on the development PC | Not yet verified |
| Actual installed Windows SDK version | Not yet verified |
| Visual Studio 2026 and WinUI workload installed | Not yet verified |
| Antigravity local project/build access | Selected workflow; verify during setup |
| CPU/OS architecture of development PC | Verify before first build; baseline is x64 |
| Minimum supported Windows 11 build | Open; define after compatibility testing |
| Final Windows App SDK deployment model | Open; linked to packaging decision in ARCHITECTURE.md |

---

## 13. Official References

- [.NET 10 downloads and SDK versions](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Windows App SDK release channels](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-channels)
- [Windows App SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- [Windows App SDK supported Windows releases](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/support)
- [Windows SDK and Windows versioning overview](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)
- [Build your first WinUI 3 app](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/get-started-winui3-for-desktop)

---

**Document owner:** Product Owner  
**Engineering partner:** Chief Engineer  
**Next planned document:** `DESIGN_SYSTEM.md`

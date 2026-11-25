# IslandTaskbar

IslandTaskbar is a native Windows 11 desktop application in development. Its goal
is to replace the visible Windows taskbar with a compact notch at the top center
of the screen that expands into taskbar and media controls.

The application uses C#, .NET 10, WinUI 3, and Windows APIs through dedicated
adapters. Core operation is local and requires no cloud account, backend, or
database.

## Development status

The current application is a prototype, not a complete taskbar replacement. It
includes:

- A top-attached notch with expandable warm-glass surfaces.
- Music Mode with real Windows media-session playback controls. The custom
  artwork and animated waveform are illustrative, not fetched track artwork or
  an audio-reactive visualization.
- Taskbar Mode with running-window discovery, application icons, and window
  switching. Taskbar Mode stays open until the user closes it, including while
  media playback changes.

The current prototype does not hide the native Windows taskbar. The complete
replacement and recovery workflows remain future work. Verification is partial:
known gaps include topmost reliability, an observed media pause timeout during
full runtime verification, and an empty-desktop window-enumeration error.

## Development requirements

- Windows 11 on x64. The supported Windows build range is not yet finalized.
- .NET SDK `10.0.404`, pinned in `global.json` with `latestPatch` roll-forward.
- Windows SDK `10.0.26100.0` or later and WinUI 3 build tools, such as Visual
  Studio with the WinUI application development workload.
- Access to NuGet for the initial package restore.

The native project pins Windows App SDK `2.7.0` and CsWin32 `0.3.371`.
Node.js, a Gemini API key, and an `.env` file are not required for the native app.

## Build

Run these commands in PowerShell from the repository root:

```powershell
dotnet --version
dotnet restore IslandTaskbar.sln -p:Platform=x64
dotnet build IslandTaskbar.sln -c Debug -p:Platform=x64
dotnet build IslandTaskbar.sln -c Release -p:Platform=x64
```

## Run the native prototype

After a successful Debug build:

```powershell
& .\src\IslandTaskbar\bin\x64\Debug\net10.0-windows10.0.26100.0\IslandTaskbar.exe
```

For a Release build, replace `Debug` with `Release` in that path. Run from a normal
interactive Windows desktop; isolated automation desktops cannot fully verify
foreground window switching or media-session behavior.

A successful build alone does not establish that Windows integration works.
Runtime behavior must also be tested on the actual desktop.

## Repository guide

- `IslandTaskbar.sln` - Native Windows solution.
- `src/IslandTaskbar/` - Native application source, XAML, services, adapters, and
  assets.
- `src/App.tsx`, `src/main.tsx`, and `package.json` - Separate React/Vite scaffold;
  not part of the native Windows solution.
- [Architecture](ARCHITECTURE.md) - Application layers and Windows integration
  boundaries.
- [Design system](DESIGN_SYSTEM.md) - Visual tokens and interaction guidance.
- [Coding conventions](CONVENTIONS.md) - Contribution and implementation rules.
- [Testing](TESTING.md) - Test procedures and acceptance requirements.

Some planning documents describe intended behavior rather than completed
functionality. Read implementation and verification status alongside them.

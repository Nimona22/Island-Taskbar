# Phase 5.2 M4 text scaling addendum results

Date: 2026-11-26. Base HEAD: `80dd9132118c62796bae82a0c9f2a69fd8bd8dd4` plus
the user's existing uncommitted text-scaling work and this addendum. No commit created.
Environment: Windows 10.0.26300 x64 (RuntimeInformation); .NET SDK 10.0.404.
No IslandTaskbar process was launched. No desktop test or settings change was performed.

## Exact file accounting

Modified during this task:
- `src/IslandTaskbar/Presentation/Windows/IslandWindow.xaml`
- `src/IslandTaskbar/Presentation/Windows/IslandWindow.xaml.cs`
- `src/IslandTaskbar/Models/TaskbarRowSizing.cs` (already present, untracked at entry)

Created during this task:
- `.junie/plans/m4-text-scaling-addendum.md`
- `.junie/plans/m4-text-scaling-results.md`
- `.junie/tests/TextScaling/TextScaling.csproj`
- `.junie/tests/TextScaling/Program.cs`

Preserved pre-existing changes without further edits:
- `src/IslandTaskbar/Styles/Tokens.xaml`: removal of obsolete fixed list-height token.
- `.junie/plans/windows-text-scaling-verification.md`: existing VM verification checklist.

## Decisions

Subscribe/read initial TextScaleFactor together; changes dispatch at low priority and
read the latest value on the UI thread, with closure guards and a diagnostic if the
dispatcher rejects the callback. Invalidate the realized taskbar subtree and root;
the existing LayoutUpdated path performs measurement. Unsubscribe at close.
WinUI continues to scale text automatically; there is no manual font-size multiplier.

Pinned button/container heights become minimums. Both lists use measured uniform
ItemsWrapGrid cells, retaining one-line Pinned / two-line Open windows ellipsis.
A WinUI caption probe reserves the permitted lines even if the first title is short
or containers have not been realized; actual containers are measured at the token
width. No generation counters, polling, forced UpdateLayout, or retry loops added.

Keep 420-DIP nominal width, 76-DIP buttons, 80-DIP cells, 68-DIP title width,
four-column limit, icons and full accessible-name/tooltip bindings. Disable text
scaling only on decorative taskbar glyphs inside fixed icon boxes.
Microsoft documents automatic WinUI text scaling, UI dispatch, and the recommendation
not to scale decorative symbols:
https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements
https://learn.microsoft.com/en-us/windows/apps/develop/input/text-scaling

Retain independent list scrollers with one pin row / up to three window rows.
When chrome plus one row per populated section exceeds the display budget, preserve
those minimum rows and let the outer viewport scroll. Reuse the existing surface,
host-resize and native-polygon path; no controller or native adapter edits.

## Executed checks

All are Auto-eligible / Safe (S); no real adapter or desktop integration runs.

- `dotnet build IslandTaskbar.sln -c Debug -p:Platform=x64 --no-restore -v:minimal`:
  succeeded, 0 warnings, 0 errors; final run 44.98 seconds.
- `dotnet build IslandTaskbar.sln -c Release -p:Platform=x64 --no-restore -v:minimal`:
  succeeded, 0 warnings, 0 errors; 61.57 seconds.
- `dotnet run --project .junie/tests/TextScaling/TextScaling.csproj --no-restore`:
  62 checks passed.
- Same harness with `-c Release`: 62 checks passed.
- `git diff --check`: exit 0; only Git line-ending conversion notices.

Harness coverage: nominal/minimum row heights, later taller titles and order,
shrinkage, empty lists, roomy/exact/short/zero/infinite viewport budgets, and the
existing controller converting growth/shrinkage and polygon geometry to pixels at
1, 1.25, 1.5 and 2 rasterization scales, preserving the 420-DIP surface width.
The viewport-budget test initially failed compilation before GetViewportHeights
was added, then passed. An initial geometry test omitted controller.Initialize;
the harness setup was corrected, without changing production controller behavior.

## Remaining limitations

Manual UI acceptance is **Not run**: startup at 100/150% text, live 100 -> 150 -> 100
changes, WinUI template realization/typography, all-script glyph bounds, screenshots,
focus visuals, keyboard reveal, nested-scroll routing on a short display, shutdown
event timing, and actual native hit testing need an isolated Windows VM.
The fake-adapter checks establish controller arithmetic, not desktop input behavior.
Compilation does not establish the absence of visual clipping. Use the existing VM
checklist and also exercise a display budget smaller than both minimum rows/chrome.

Media implementation, high contrast, topmost, native taskbar and unrelated code
were not changed. This report does not close the broader M4 acceptance gate.

# M4 text scaling addendum

Scope: the user's nine-point review addendum. Keep the existing 420-DIP width,
one-line Pinned titles, two-line Open windows titles, ellipsis and accessible names.
No desktop execution or settings changes; no unrelated subsystems.

- Read initial UISettings.TextScaleFactor after subscribing; dispatch changes at low
  priority, guard closure and invalidate the measured taskbar subtree. Retain shutdown unsubscription.
- Measure item containers at their existing width and reserve the permitted title
  lines using WinUI typography. Set ItemsWrapGrid.ItemHeight to the measured maximum
  so the first short title cannot constrain later titles. Disable scaling of decorative
  taskbar glyphs inside fixed icon boxes, as Microsoft recommends.
- Keep one Pinned / up to three Open windows rows. Budget their viewports while
  preserving a complete row per populated section; use an outer scroller only when
  chrome and those minimum rows exceed the display-height budget.
- Reuse UpdateDynamicTaskbarHeight / ApplyProgress / ReportSurfaceLayout and the
  existing controller/native-region path. Add no generation counters or timers.
- Run a dependency-free .NET sizing regression harness, Debug/Release builds and
  diff checks. Record actual results. VM visual, live scaling, native input and
  keyboard/scroll acceptance remain Not run without an isolated Windows VM.

Execution: inline in the current dirty workspace, preserving the initial changes.
No commits, app launches, desktop tests, or work outside this issue.

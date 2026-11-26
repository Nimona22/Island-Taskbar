---
sessionId: session-261126-045639-j0je
---

# Scope & Safety

### Objective
Verify the IslandTaskbar text-clipping fix at Windows text sizes **100%** and **150%**, with a normal-size regression comparison. This is a future execution checklist only; no application was launched, settings changed, or project file modified during preparation.

### Safety boundary
- Execute only in an isolated Windows VM or disposable environment with a known-good rollback snapshot; never use the user's primary Windows desktop.
- Use only test-owned windows and VM-local populated pins; do not expose personal windows, pin data, or user files.
- Keep resolution and Windows display scaling fixed for both runs (prefer the prior reproduction setup: 1920×1080 at 125% display scaling, if available). Change only the Windows **Text size** setting between 100% and 150%, using Windows Settings—not registry edits.
- Record settings before testing. On any app/Explorer crash, unexpected native-taskbar change, loss of VM control, or unrecoverable UI, stop immediately, preserve evidence, and revert the VM snapshot. Do not repair Explorer/taskbar state manually.
- Do not expand into high contrast, Narrator, other DPI/resolution cells, display changes, Music, or unrelated M4 blockers.

# Verification Checklist

### Project-specific observation points
`IslandWindow.xaml` contains separate `PinnedAppsListView` and `RunningWindowsListView` sections, wrapped item templates, title/icon content, and independent vertical scrolling. `IslandWindow.xaml.cs` calculates `MeasureItemRow` and `CalculateTaskbarHeight`, bounds the host to available display height, and handles keyboard navigation/focus. Use these as the checklist's observation map; do not edit or instrument source for this verification.

### Preflight in the disposable environment
- [ ] Confirm VM identity, Windows build, snapshot/rollback method, display resolution, display scaling/DPI, effective Windows Text size, and test build/version or SHA.
- [ ] Confirm default contrast and other visual settings remain unchanged; text size is the only setting varied.
- [ ] Prepare at least five VM-local test pins (enough to populate the pinned area and exercise overflow beyond the four-column reference row).
- [ ] Prepare at least 13 test-owned running-window entries (enough to exceed the three-row target at four columns), including short titles and multiple long titles that wrap/trim. Include recognizable fixture IDs in the titles.
- [ ] Capture pre-test VM/settings state. Keep fixture and evidence outputs outside the project source tree.

### Run A — Windows Text size 100% baseline
- [ ] Set/confirm Windows Text size at 100% in the VM; leave resolution and display scaling fixed. Confirm the effective setting after Apply.
- [ ] Launch the candidate build only in the VM and populate the test pins/windows.
- [ ] Capture the expanded Island with both labeled sections, a short title, and long-title entries visible.
- [ ] Measure outer Island bounds and actual height; record the pinned and running-window viewport heights, representative row/item heights, visible row counts, and each section's scroll extent. Record DIPs and the physical-pixel bounds or conversion scale used.
- [ ] Verify short titles render completely; long titles may use the product's intentional ellipsis/wrapping, but no glyphs, lines, or icon areas are hard-clipped or overlapped.
- [ ] Scroll each section independently to its end and back. Confirm scrolling one area does not move the other, and every pin/window remains reachable.
- [ ] Using keyboard only: focus the notch, open the Island, verify initial focus on Add, traverse into pins and windows, exercise Home/End and arrow navigation, and verify visible focus and automatic scrolling to focused items. Press Escape; record the destination and compare it with the observed baseline behavior.

### Run B — Windows Text size 150%
- [ ] Change only Windows Text size to 150% using Settings; verify the effective value. Keep the same VM, display scaling, resolution, fixtures, and candidate build.
- [ ] Repeat all visual, measurement, overflow/scroll, keyboard-navigation, focus, and Escape checks from Run A.
- [ ] Compare the measured item rows and host height against actual rendered content: rows must provide room for the enlarged text/icons, and the Island must contain its content without clipping or extending beyond the usable display area. Do not require a fixed height increase; judge the measured layout against the content and available area.
- [ ] Confirm long titles retain legible text and allowed intentional trimming only; short titles, icons, headings, controls, and focus indicators remain visible and usable.
- [ ] Confirm the last pinned item and last running-window item can be reached by scrolling and keyboard navigation at 150%.

### Run C — 100% regression and cleanup
- [ ] Return the disposable VM to Text size 100% (or revert its snapshot) and repeat the essential geometry, title/icon clipping, independent-scroll, and keyboard/focus checks.
- [ ] Compare Run C with Run A: no new regression in the normal-size layout, 420-DIP reference width, top attachment, section separation, wrapping, focus, or item reachability.
- [ ] Close the test-owned app normally, retain the evidence bundle, then revert/discard the VM to the clean snapshot and confirm the test did not alter the user's desktop or project.

# Evidence & Verdict

### Required evidence package
- Run metadata: VM/snapshot identifier, Windows build, candidate build identity/SHA, resolution, display scaling/DPI, effective Text size, date/time, and operator.
- One unedited screenshot per run showing the expanded Island and both sections; additional screenshots at each run for pin overflow, window overflow, long-title/icon presentation, and visible keyboard focus. Use test-only titles and avoid personal data.
- A measurement table for each run: Island bounds/height, pinned and running-window viewport heights, representative row/item heights, available display height, visible rows, scroll extents/offsets, and relevant text/icon/focus bounds. State units and how physical pixels map to DIPs.
- Per-check status (`Passed`, `Failed`, `Blocked`, or `Not run`) with an observation and evidence filename. Preserve failures and interrupted attempts; do not overwrite them with later passes.

### Pass/fail rules
- **Pass at 150%:** no unintended clipping of text, icons, headings, controls, or focus indicators; short titles remain complete; long-title ellipsis/wrapping is deliberate and legible; measured rows and host geometry contain their content; both lists scroll independently; all test items remain keyboard- and scroll-reachable; focus/navigation behavior is no worse than the 100% baseline.
- **Pass at 100% regression:** all applicable checks pass and the established normal-size layout and interactions remain intact.
- **Fail:** any hard-clipped glyph/line/icon, overlapping content, inaccessible overflow item, focus loss that prevents continued keyboard use, or host/layout measurement that leaves visible content outside its usable area.
- **Blocked:** required settings or evidence cannot be confirmed, VM recovery/shell stability is uncertain, or the candidate build/test fixtures are unavailable. Do not infer a pass from source inspection or a successful launch.

This focused plan is not a full Phase 5.2 accessibility/display acceptance; high contrast, Narrator, other DPI/resolution cells, and other M4 issues remain outside scope.

# Delivery Steps

###   Step 1: Prepare the disposable verification environment
A rollback-ready Windows VM contains the candidate build and deterministic test-only pins and windows.

- Create or select an isolated Windows VM/disposable environment with a verified clean snapshot; do not use the user's main desktop.
- Fix and record resolution/display scaling (prefer 1920×1080 at 125% if available); verify Windows Text size starts at 100% and avoid changing other settings.
- Prepare at least five VM-local pins and at least 13 test-owned windows, including short and long titles; keep evidence outside the repository.

###   Step 2: Capture the 100% text-size baseline
The normal-size run records layout, clipping, scrolling, and keyboard/focus behavior with complete evidence.

- Confirm the effective 100% Windows Text size and candidate build identity before launching in the VM.
- Measure Island, section viewport, row/item, and available-height geometry in DIPs and/or physical pixels with the conversion recorded.
- Check titles/icons, overflow and independent scrolling, keyboard navigation, visible focus, and Escape behavior; capture screenshots and per-check outcomes.

###   Step 3: Verify the 150% text-size fix
The 150% run demonstrates that enlarged text remains legible, contained, and reachable without regressing interactions.

- Change only Windows Text size to 150% through Settings inside the VM and verify the applied value.
- Repeat the same fixture, geometry, clipping, scroll, keyboard, and focus checks under the identical display configuration.
- Compare content bounds to measured row/host geometry and retain all failures or interruptions without overwriting earlier evidence.

###   Step 4: Confirm 100% regression and restore the VM
A final normal-size comparison is recorded and the disposable environment is returned to its clean snapshot.

- Restore 100% text size or revert the VM snapshot, then repeat essential geometry, clipping, scrolling, and keyboard/focus checks against the first baseline.
- Assign explicit Passed/Failed/Blocked/Not run status to each criterion and bundle screenshots, measurements, settings, and build/environment metadata.
- Close the test-owned application normally and revert/discard the VM; stop and preserve evidence if any crash or shell/taskbar instability occurs.
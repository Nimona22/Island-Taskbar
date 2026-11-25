# Clean up 1

**Project:** IslandTaskbar  
**Review date:** November 25, 2026  
**Status:** Review complete; cleanup execution awaits user approval.  
**Scope:** Current native application, root web scaffold, Git hooks, and existing regression tests. No new product features are proposed.

No application source or configuration was changed for this review. The existing
README edit and the pre-existing edit to `IslandWindow.xaml.cs` were preserved.
This report is the only review deliverable added. Line numbers refer to the
reviewed source and may move after later edits.

## Evidence and limits

- Read the native source, XAML, resource dictionaries, project configuration,
  root web source/configuration, hooks, and relevant phase reports.
- Ran the existing dependency-free console suites against their linked source:

  | Command | Result |
  |---|---|
  | `dotnet run --project .\Phase51.Tests\Phase51.Tests.csproj --no-restore --verbosity quiet` | PASS: 48 window/service/mode/icon checks |
  | `dotnet run --project .\Phase41.Tests\Phase41.Tests.csproj --no-restore --verbosity quiet` | PASS: 14 media checks |
  | `dotnet run --project .\Phase41.DismissalTests\Phase41.DismissalTests.csproj --no-restore --verbosity quiet` | PASS: 22 dismissal checks |

- Reproduced the staged-filename type-check failure described in CU1-04.
- Calculated the bounding-box/painted-outline mismatch described in CU1-01.
  This calculation used the current formulas; it was not a desktop mouse test.
- Searched references before identifying code as unused. Native source/config
  hashes for 39 files were unchanged between review snapshots.
- `npm run lint` was started but did not finish during the review and was
  interrupted. It is **not** reported as passing or as a compiler failure.
- Did not launch IslandTaskbar, control personal applications, run desktop
  interaction tests, change display settings, or hide the Windows taskbar.
- Did not rebuild the full WinUI application. Passing console suites do not
  establish that the current XAML/native integration is correct.

## Findings

### CU1-01 — Dismissal hit testing includes transparent cutouts

**Priority:** Medium. **Evidence:** Confirmed source/geometry mismatch; physical
desktop effect still needs verification.

- **Where found:** `src/IslandTaskbar/Presentation/Windows/IslandWindow.xaml.cs`,
  lines 705–729; compare `Models/IslandSurfaceOutline.cs`, lines 12–27.
- **Why it is broken:** The pointer poll uses the rectangular layout bounds to
  decide whether the cursor is inside. The painted/native region has concave
  shoulders and curved corners. At the current 420-DIP Taskbar width and a
  64-DIP surface height, local points `(1,30)` and `(419,30)` pass the rectangle
  check but are outside the shared outline. The center point `(210,30)` passes
  both checks. Transparent areas can therefore be treated as inside, preventing
  the countdown or outside-click collapse that the method is meant to perform.
- **Is it useless?** No. Pointer tracking and dismissal are active behavior; the
  shape test is inconsistent with the existing visible/input shape.
- **Effect if removed:** Deleting the method or the dismissal guard would break
  outside-click/leave behavior. Correct the test against the existing shape
  after approval; preserve the current geometry and interaction design.

### CU1-02 — Backdrop failure has no dependable opaque fallback

**Priority:** Medium. **Evidence:** Confirmed missing fallback branch; appearance
on an unsupported backdrop was not tested.

- **Where found:** `Presentation/Backdrops/TransparentIslandBackdrop.cs`, lines
  19–22; `Presentation/Windows/IslandWindow.xaml.cs`, lines 99–103;
  `Styles/Tokens.xaml`, lines 53–55 and 63–64. Paths are under
  `src/IslandTaskbar/`.
- **Why it is broken:** If acrylic is unsupported, the backdrop returns without
  installing a replacement. The idle background is unconditionally overwritten
  with a very low-alpha gradient, and expanded surfaces also use low-alpha
  stops. The resource named `Surface.IslandFallback` itself has alpha `1A`, not
  an opaque background. The promised readable fallback is therefore not ensured
  when the backdrop is absent.
- **Is it useless?** No. Acrylic and the gradients produce the current design.
  The absent fallback path is the issue.
- **Effect if removed:** Removing the backdrop or gradients changes the selected
  appearance and does not establish a proper fallback. Repair the existing
  fallback handling conditionally; do not remove the visual system.

### CU1-03 — Two native setup failures are ignored

**Priority:** Medium. **Evidence:** Confirmed unchecked return values; no actual
failure of these calls was induced.

- **Where found:** `src/IslandTaskbar/Adapters/Implementations/WindowsAppSdkIslandWindowAdapter.cs`,
  lines 57–67.
- **Why it is broken:** The extended-style `SetWindowLongPtr` call and
  `DwmExtendFrameIntoClientArea` call have no result/error handling. Setup
  continues even if the layered style or frame extension was not applied. Other
  operations in the same adapter explicitly check failures, so these two paths
  can silently leave a partially configured overlay.
- **Is it useless?** No. Both operations support the current transparent host.
- **Effect if removed:** Removing them could restore the unwanted rectangular
  background or change input/rendering behavior. Retain them and add correct
  error handling after approval. For `SetWindowLongPtr`, handle its ambiguous
  zero return correctly rather than treating every zero as an error.

### CU1-04 — Pre-commit type checking fails for staged web files

**Priority:** Medium. **Evidence:** Reproduced with the installed compiler.

- **Where found:** `package.json`, lines 39–42; `.husky/pre-commit`, line 53.
- **Why it is broken:** `lint-staged` appends matched staged filenames to
  `tsc --noEmit`. Running the resulting command shape,
  `.\node_modules\.bin\tsc.cmd --noEmit src/App.tsx`, exits 1 with:

  ```text
  error TS5112: tsconfig.json is present but will not be loaded if files are specified on commandline.
  ```

  Therefore, staging a matching web file triggers a failing check even before
  this configuration can establish whether the file has a code defect.
- **Is it useless?** Type checking is useful. This command wiring is broken.
- **Effect if removed:** Removing the invocation avoids the failure but also
  removes web type checking. Correct the project-level invocation if retaining
  the web scaffold, or remove the obsolete web check with that scaffold. Keep
  the unrelated secret-scanning checks.

### CU1-05 — Wave animation list is written but never read

**Priority:** Low. **Evidence:** Confirmed reference search.

- **Where found:** `src/IslandTaskbar/Presentation/Windows/IslandWindow.xaml.cs`,
  field at line 42 and `.Add(...)` at line 338. Compare `StartLoop`, lines
  344–358, and cleanup at lines 544–547.
- **Why it is useless:** `_waveAnimations` only stores animation references.
  Nothing reads or clears the list. `StartLoop` already registers every
  animation in `_animationResources` and every running property in
  `_compositionLoops`, which perform the actual cleanup.
- **Why it is broken:** It is redundant bookkeeping, not a demonstrated crash
  or performance defect.
- **Effect if removed:** Remove the unused field and change
  `_waveAnimations.Add(StartLoop(...))` to `StartLoop(...)`. Animation and
  disposal should remain unchanged. Deleting the entire call would stop
  starting the waveform animations, so that is not a safe cleanup.

### CU1-06 — Two geometry constants no longer control anything

**Priority:** Low. **Evidence:** No references found in native source or local
first-party C#/XAML verification code.

- **Where found:** `src/IslandTaskbar/Models/IslandPrototypeGeometry.cs`,
  `ExpandedTop` at line 12 and `TaskbarHeight` at line 15.
- **Why they are useless:** The surface margin is set directly in
  `ApplyProgress`, and Taskbar height comes from `CalculateTaskbarHeight`.
  Changing these two constants has no effect on the running layout.
- **Why they are broken:** They are misleading configuration leftovers rather
  than a runtime defect.
- **Effect if removed:** No intended runtime or geometry change. Only remove
  these unreferenced constants after approval; leave all active dimensions,
  sizing, and clipping untouched.

### CU1-07 — Enumeration retains a second, redundant COM setup

**Priority:** Low. **Evidence:** Source-traced cleanup candidate; requires native
build/runtime validation before treating removal as verified.

- **Where found:** `src/IslandTaskbar/Adapters/Implementations/Win32WindowSystem.cs`,
  lines 141–142 and 197. Compare `ReadExecutableIcon`, lines 319–335.
- **Why it appears useless:** The outer enumeration method still initializes
  COM with a comment about executable-icon extraction. That extraction now
  runs through `WindowIconLoader` on a separate worker and initializes and
  uninitializes COM inside `ReadExecutableIcon` itself. The remaining outer
  enumeration work contains no direct COM operation needing that setup.
- **Why it is broken:** Redundant setup and a stale explanation obscure which
  thread actually owns the COM-dependent work. This is not a confirmed leak;
  the existing calls are paired.
- **Effect if removed:** Removing only the outer pair should leave icon
  extraction protected by its own pair. Do not remove COM setup from
  `ReadExecutableIcon` or remove the generated bindings still used there.

### CU1-08 — Separate blank web app and unused package dependencies

**Priority:** Medium maintenance cost. **Evidence:** Confirmed separation from
the native solution and no matching first-party imports for the packages below.
Removal of the entire scaffold remains conditional.

- **Where found:** `src/App.tsx`, lines 6–7; `src/main.tsx`; `src/index.css`;
  `index.html`; `vite.config.ts`; `tsconfig.json`; `package.json`, lines 14–37;
  `package-lock.json`; `.env.example`.
- **Why it is unwanted for the native application:** `App.tsx` renders an empty
  div. The native solution references only the C# project. No native code uses
  this React/Vite application. First-party source/configuration has no imports
  of `@google/genai`, `express`, `dotenv`, `lucide-react`, or `motion`; the
  Express types also support no Express source. The environment template retains
  Gemini/app-URL setup unrelated to native operation.
- **Why it is broken or useless:** The web app is an active but empty separate
  entry point, not unreachable code within the native app. Its unused packages
  add install/lockfile maintenance. It is only safe to call the whole scaffold
  unwanted if a separate web preview is no longer desired.
- **Effect if removed:** Native C#/WinUI behavior should be unaffected, but
  `npm run dev/build`, the browser preview, and existing npm/Husky setup would
  change or disappear. Keep the useful secret scanners and resolve their
  installation/invocation first. Do not delete all Node tooling: local
  `Phase41*.mjs` and `Phase51.BrowserFixture.mjs` use Node built-ins for real
  media verification, even though they do not import these app packages.

### CU1-09 — Music tooltip incorrectly says there is no audio playback

**Priority:** Low. **Evidence:** Confirmed mismatch with the active command path.

- **Where found:** `src/IslandTaskbar/Presentation/Windows/IslandWindow.xaml`,
  line 108; compare `ViewModels/MusicPrototypeViewModel.cs`, lines 67–86, and
  `Adapters/Implementations/WindowsMediaSessionService.cs`, lines 186–196.
- **Why it is broken:** The tooltip says “Illustrative waveform and artwork;
  no audio playback.” The artwork/waveform are illustrative, but the controls
  now send real playback commands to Windows media sessions. This wording is
  left over from the earlier purely visual prototype.
- **Is it useless?** The status control and its accessible state are useful;
  the obsolete wording is the problem.
- **Effect if removed:** Removing or correcting only the inaccurate phrase has
  no media behavior effect. Do not remove the status control, state binding, or
  truthful illustrative label.

### CU1-10 — Existing regression suites are excluded from version control

**Priority:** Medium maintenance/reproducibility issue. **Evidence:** Confirmed
with `git ls-files` and `git check-ignore`.

- **Where found:** `.gitignore`, line 29 (`Phase*`);
  `Phase51.Tests/`, `Phase41.Tests/`, and `Phase41.DismissalTests/`.
- **Why it is broken:** All 84 checks ran locally, but the suite source is
  ignored and untracked. A fresh checkout cannot reproduce them from the
  committed repository. The phase-artifact convention currently hides useful
  regression source along with temporary output.
- **Is it useless?** No. These tests are valuable and must not be swept away as
  disposable phase folders.
- **Effect if removed:** Deleting these folders loses existing regression
  coverage. If approved, retain the existing test source through a narrowly
  scoped ignore exception or an approved permanent location, while continuing
  to ignore logs, output, browser profiles, and generated artifacts. This is
  preservation of current tests, not a new test framework or feature.

### CU1-11 — Native pointer polling bypasses the adapter boundary

**Priority:** Low architectural issue. **Evidence:** Confirmed source structure.

- **Where found:** `src/IslandTaskbar/Presentation/Windows/IslandWindow.xaml.cs`,
  imports at lines 25–27, native handle acquisition at line 138, and native calls
  at lines 695–708. The timer is configured at line 143.
- **Why it is an issue:** The view directly calls native key/cursor APIs every
  45 ms while Taskbar Mode is active. `ARCHITECTURE.md` requires native calls to
  sit behind adapters. This puts OS input handling and related geometry logic
  outside the existing testable boundary. No measured CPU regression is claimed.
- **Is it useless?** No. It currently implements outside-click and pointer-leave
  detection where XAML events alone have been unreliable.
- **Effect if removed:** Deleting the polling/timer would change or break those
  behaviors. If approved, move the existing native querying behind the proper
  boundary while preserving its behavior. A timer or input-system redesign is
  not authorized by this report.

### CU1-12 — Descriptions of Taskbar behavior and size are stale

**Priority:** Low. **Evidence:** Confirmed documentation/source mismatch.

- **Where found:** `README.md`, lines 21–22; `Phase51.Design.md`, interaction
  section; `DESIGN_SYSTEM.md`, Phase 5.1 section. Compare
  `Presentation/Windows/IslandWindow.xaml.cs`, lines 565–595 and 671–746, and
  `Models/IslandPrototypeGeometry.cs`, lines 8–18, under `src/IslandTaskbar/`.
- **Why it is broken:** Documentation describes Taskbar Mode remaining open
  until explicitly closed, while current view code also collapses after a
  successful switch, deactivation, and an eligible pointer-leave timeout.
  Documented fixed sizes also differ from the current 88x26 idle, 320x68 Music,
  and 420-DIP-wide dynamically sized Taskbar surface. The README sentence added
  in the earlier README fix carried forward the old persistence description.
- **Is it useless?** The documents are useful, but these statements cannot be
  relied on as an exact description of the current implementation.
- **Effect if removed:** Correcting stale statements has no runtime effect.
  Do not revert the current UI to older documented sizes or persistence rules.
  Preserve the user's latest edits; reconcile wording with the intended current
  behavior after approval.

## Previously recorded issues that are not safe deletion candidates

These remain in the existing Phase 5.1 report. They were not reproduced in a
desktop run during this review, and their root causes must not be invented.

| Issue | Where found | What is known / why not simply remove code | Effect of blind removal |
|---|---|---|---|
| Empty-desktop enumeration exception | `Win32WindowSystem.cs`, lines 132–136; `Phase51.VerificationReport.md`, line 60 | A previous zero-visible-window probe threw `EnumWindows failed`. The current code still throws on an unexplained zero result. Distinguish an actual API failure, callback cancellation/failure, and the observed empty-desktop case before choosing a fix. | Deleting enumeration removes running apps; deleting all error handling could hide genuine failures. |
| Full-run media pause timeout | `WindowsMediaSessionService.cs`, lines 166–207; `Phase51.VerificationReport.md`, line 59 | The earlier full Debug run timed out. Today's 14+22 pure checks pass but do not prove browser/GSMTC command delivery. Session/provider selection and desktop context still require real evidence. | Removing media selection or command code breaks working playback features; removing the timeout can leave commands waiting longer. |
| Overlay topmost reliability | `WindowsAppSdkIslandWindowAdapter.cs`, lines 40–51 and 143–145; `Phase51.VerificationReport.md`, line 61 | Existing `P23-TOPMOST-001` is unresolved. The presence of `IsAlwaysOnTop = true` does not establish why the reported failure happened. | Removing topmost configuration makes the intended overlay behavior worse. A speculative reassertion loop is outside cleanup scope. |

## Items deliberately retained

- Shared design tokens with no current direct use: they are a documented token
  library, not proof of a bug or authorization to prune the design system.
- The real media/window adapters, geometry outline, clipping, backdrop,
  cancellation, dismissal grace, and icon-loader isolation: these have active
  callers and current responsibilities.
- Local test source and verification evidence. Generated build outputs and old
  browser profiles may be archival candidates, but were not sized, ownership
  checked, or approved for deletion in this review.
- Missing future product features are not classified as broken or useless code.

## Proposed approval scope

1. **Small removals/copy correction:** CU1-05, CU1-06, and CU1-09; CU1-07 only
   after native validation confirms it is redundant.
2. **Correct current defects/tooling:** CU1-01 through CU1-04, with focused
   regression checks and no geometry redesign.
3. **Explicit conditional decisions:** CU1-08 (retire or keep the separate web
   scaffold), CU1-10 (retain existing tests in version control), CU1-11 (move
   native input querying), and CU1-12 (reconcile documentation).

**No cleanup has been executed. Awaiting user approval for the selected items.**

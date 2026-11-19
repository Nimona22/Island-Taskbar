# TESTING.md

**Project:** IslandTaskbar  
**Document status:** Draft v0.1  
**Last updated:** 2026-11-19  
**Based on inspection of:** `PRD.md`, `SCOPE.md`, `USER_STORIES.md`, `ARCHITECTURE.md`, `DATABASE_SCHEMA.md`, `ENV.md`, `DESIGN_SYSTEM.md`, `BRAND.md`, `AGENT.md`, `DECISIONS.md`  
**Fulfils:** the test-definition role assigned to this file by `USER_STORIES.md` US-028 and `AGENT.md` §11

> This document defines **how IslandTaskbar is tested and what evidence counts**. It does not define requirements: expected results are taken from the cited source documents and are never redefined here. **It contains no test results.** Nothing described here has been run.

---

## 0. Testing Rules (Read First)

You MUST NOT:

- Report a test as **Passed** unless you executed it on the target environment (Section 3), observed the expected result, and recorded evidence.
- Treat a successful build, code inspection, unit tests alone, mocks, or placeholder UI as acceptance evidence (`ENV.md` §0, §9; `USER_STORIES.md` §0, §7; `AGENT.md` §11).
- Run, or cause to run, any code that hides the native taskbar unattended, or without explicit user authorization for that run (Section 6; D-012).
- Add a test framework, test package, or any non-.NET runtime tool (Node.js, Python, Docker). The framework is not approved (OD-003; `ENV.md` §0).
- Invent pass/fail thresholds, tolerances, or expected behaviors. Where none is documented, record the measurement or observation and raise a Decision Request.
- Fabricate, estimate, or "example" test results or evidence.
- Weaken or skip an acceptance criterion to obtain a pass.
- Start Release 2 or Release 3 test execution before the preceding release is accepted (`SCOPE.md` §3; `USER_STORIES.md` §7).
- Terminate or restart `explorer.exe` as part of an automated step (`AGENT.md` §7).

You MUST:

- Default every test to **Not run** until it is executed.
- Record environment, build, and commit with every result (Section 3.5).
- Label every test **Manual** or **Auto-eligible**, and **Safe (S)** or **Hiding (H)** (Section 2.1).
- Distinguish a fake used inside a unit test from mock functionality in the product. The first is allowed at the adapter boundary; the second never counts as implemented (`AGENT.md` §9).
- Mark tests whose expected result is undefined with **⚑** and raise a Decision Request instead of choosing an expectation (Section 12).

**Result statuses**

| Status | Meaning |
|---|---|
| **Passed** | Executed on target, expected result observed, evidence recorded. |
| **Failed** | Executed, expected result not observed. A defect is filed (Section 9). |
| **Blocked** | Cannot run. Give the reason (for example "no recovery plan exists", "OD-018 open"). |
| **Not run** | Default. Has not been executed. |
| **Not applicable** | Does not apply to this build or release. Give the reason. |
| **Design only** | Defined for a later release; execution is not allowed yet. |

---

## 1. Testing Philosophy

1. **Reliability over superficial coverage.** The highest-risk areas are native-taskbar hiding and recovery, window positioning and hit-testing, DPI and display changes, and window switching. They get the most rigorous testing. No coverage percentage is approved or used as a quality measure.
2. **Test actual behavior.** A passing build proves compilation only. IslandTaskbar integrates with the Windows shell, so UI and Windows behavior must be exercised on the real target machine.
3. **Never report unexecuted tests as passing.** Status defaults to Not run. Partial runs are reported as partial.
4. **Real data, never simulated.** Metrics, network values, audio levels, and status values must come from Windows. A test cannot pass on simulated data (`AGENT.md` §9).
5. **Safety first.** No test may leave the user without a usable taskbar. Tests that hide the native taskbar are controlled by Section 6.
6. **Manual and automated are kept distinct.** No automated test framework is approved, so **all execution is currently manual**. Tests are marked Auto-eligible where they could be automated once OD-003 is resolved. A manual result is never described as automated.
7. **Acceptance criteria belong to the user stories.** This document defines procedures and evidence. It does not change criteria.

---

## 2. Testing Levels

| Level | Purpose | Boundary | Real Windows needed | Execution today |
|---|---|---|---|---|
| **Unit** | Verify logic in isolation: settings validation, pin ordering, window-eligibility filtering rules, view-model state, JSON load/migrate. | Services and view models with **fake adapter interfaces** (`ARCHITECTURE.md` §3.3). No Win32, no UI, no real file system beyond a temporary directory. | No | Not runnable until a framework is approved (OD-003). |
| **Integration** | Verify that real components work together without the live shell: services with the real settings service and a temporary data directory; lifecycle logic with a fake `ITaskbarVisibilityController`. | Real in-process components. Windows integration is faked. | No | Not runnable until OD-003. |
| **Windows API / interop** | Verify each real adapter against real Windows on the target build: window enumeration and activation, launching, display and DPI queries, and later audio, power, network, and media. | One adapter against the real OS. Splits into **Safe** (no hiding) and **Hiding**. | Yes | Manual. |
| **UI and interaction** | Verify window behavior and visuals: positioning, expansion, hit-testing, hover, keyboard, accessibility, materials, DPI. | The real WinUI window on a real display. | Yes | Manual. UI automation approach not approved (OD-003). |
| **End-to-end** | Verify complete user workflows on the real desktop: first run, launch, switch, exit, recovery. | The whole application and the Windows shell. Includes native-taskbar hiding when active. | Yes | Manual. Hiding runs are **H**. |
| **Manual acceptance** | The Product Owner or an assigned tester executes the acceptance criteria of each story on the target machine. | Real machine, real application, real user flow. | Yes | Manual. This is the gate for release acceptance (Section 10). |
| **Regression** | Confirm that previously passing behavior still passes after a change. | See 2.2. | Per test | Manual now; automated subsets later. |

**What no single level can do:** unit and integration tests cannot satisfy an acceptance criterion by themselves. Only manual acceptance on the target machine, or automated tests proven equivalent and approved, can.

### 2.1 Test classification

Every test carries two labels.

| Label | Values |
|---|---|
| **Class** | **S** (Safe): executes no code that hides the native taskbar. **H** (Hiding): executes code that hides it, and requires authorization under Section 6. **S†**: Safe only when the build or configuration under test does not execute hiding; otherwise H (see 6.2). |
| **Exec** | **M**: manual only. **M/A**: manual now, automation-eligible after OD-003 is resolved. |

### 2.2 Regression scope

- **Safety-critical suite:** SAF tests are re-run whenever a change touches lifecycle, visibility, or recovery. Normal exit **and** forced termination must both be re-tested (`AGENT.md` §11).
- **Impacted-area suite:** the tests for each story a change touches, plus its neighbors.
- **Full release suite:** every required test of the release, re-run before acceptance (Section 10).
- Every fixed defect adds or updates a test that would have caught it (Section 9.4).

---

## 3. Test Environment

### 3.1 Target platform

| Item | Value | Source |
|---|---|---|
| Operating system | Windows 11, version 26H2 | `ENV.md` §2 |
| OS build | `26300.9974` | `ENV.md` §2; `PRD.md` §13 |
| Architecture | `x64` primary development target. ARM64 only after core validation. | `ENV.md` §2; `ARCHITECTURE.md` §2 |

This is the **only** validated target. Build `26300.9974` is a test target, not a support promise, and the minimum supported build is undecided (OD-015). **No other Windows version or build is supported or may be claimed.** Runs on other versions or builds are exploratory, must be labeled as such, and do not establish support or count toward acceptance.

### 3.2 Toolchain

| Component | Requirement | Source |
|---|---|---|
| .NET SDK | `10.0.404` (`global.json`, `rollForward: latestPatch`) | `ENV.md` §3, §6 |
| .NET runtime | `10.0.15` servicing level | `ENV.md` §3 |
| Windows App SDK | `2.7.0` stable | `ENV.md` §3; D-004 |
| Windows SDK | `10.0.26100.0` family or later | `ENV.md` §3 |
| UI framework | WinUI 3 | D-004 |
| Build | `dotnet build -c Debug -p:Platform=x64`; `-c Release` for performance runs | `ENV.md` §9 |

Verify the environment with the checks in `ENV.md` §5 (`winver`, `dotnet --list-sdks`, `dotnet --list-runtimes`, `dotnet --info`, `git --version`) and record the output. Exact package versions and installed SDKs are unverified (OD-023). Enable Windows Developer Mode if the WinUI workflow requires it (`ENV.md` §4).

### 3.3 Hardware and display

| Need | Detail | Applies to |
|---|---|---|
| Primary display resolutions | 1080p (1920×1080) and 1440p (2560×1440), selectable | Release 1 (`SCOPE.md` §3) |
| Display scaling | 100%, 125%, 150%, 175%, 200% selectable in Windows | D-008; see X-01 |
| Input | Keyboard and mouse | All |
| Second display | One additional display | Release 2 (US-020) |
| Battery device | A laptop, plus a desktop without a battery for the no-battery case | Release 2 (US-017) |
| Audio output and media sources | At least one output device; media-session apps chosen and recorded by the tester | Release 2 and 3 |
| Network | Wi-Fi and/or Ethernet, and a way to disconnect them | Release 2 and 3 |

The development PC's CPU, memory, and GPU are not documented (`ENV.md` §12). Record them with every result. No minimum hardware is defined. Additional hardware coverage is open (TOD-09).

### 3.4 Dependencies and permissions

- Tests run as a **standard user**. Do not run elevated unless a feature has a documented, approved need (`ARCHITECTURE.md` §10; US-001).
- No Docker, Node.js, Python, or other non-.NET runtime may be introduced for testing (`ENV.md` §0).
- Built-in Windows tools are available without adding dependencies: Task Manager, Resource Monitor, Performance Monitor, Event Viewer, Narrator, Snipping Tool, and the Windows Settings pages. Any other tool needs approval.
- Feature permissions (audio capture, notifications) are requested only when the user enables the feature. Test both **granted** and **denied** states (`ARCHITECTURE.md` §10; US-018, US-023).
- Persisted state lives in the per-user application data directory (`DATABASE_SCHEMA.md` §5). The folder name is undefined (OD-006), so tests refer to "the application's data location" and must not hard-code a path.

### 3.5 Environment record (attach to every result)

```
Date / tester (person or agent):
Windows version and build (winver):
Architecture:
.NET SDK / runtime; Windows App SDK version:
IslandTaskbar build configuration and commit:
Packaging (packaged / unpackaged), if known:
Hardware: CPU / RAM / GPU:
Displays: resolution, scale %, count, primary:
Native taskbar original state (auto-hide on/off):
Shell or taskbar modification tools installed (for example Windhawk): Yes/No — name:
User type (standard/admin):
```

---

## 4. UI and Display Testing

Legend: **S†** = Safe only if hiding is not executed by the build or configuration (see 6.2). All tests are **M** unless marked. Geometry pass tolerances are not defined (TOD-03): record measurements in **physical pixels** and report any non-zero offset.

### 4.1 Notch positioning

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| UI-01 | Capture the collapsed notch at native resolution. Measure its horizontal center against the primary display center. | Centered on the primary display (US-004; `DESIGN_SYSTEM.md` §3.1) | S† | M | R1 |
| UI-02 | Measure the top edge. Confirm the notch's top pixel row equals the display's top row and that rounded corners do not create a gap. | Flush with the top edge, no gap (US-004; `DESIGN_SYSTEM.md` §3.1, §6) | S† | M | R1 |
| UI-03 | Maximize a normal window with the notch active. Compare the desktop work area before and after launch. | Does not reserve work area; the window is not pushed below the notch (US-004; `ARCHITECTURE.md` §4.1) | S† | M | R1 |
| UI-04 | Inspect the collapsed state. | Shows a recognizable product mark or icon and no unnecessary information when idle (US-004; `DESIGN_SYSTEM.md` §10) | S† | M | R1 |

### 4.2 Expansion and collapse

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| UI-05 | Click the notch. | Expands downward from the notch anchor; never sideways or upward (US-005; `DESIGN_SYSTEM.md` §1.5) | S† | M | R1 |
| UI-06 | Collapse by clicking outside, and separately by pressing Escape. | Collapses per the interaction design (US-005; `PRD.md` §8.3). Inactivity-timeout collapse is **Blocked** until OD-018. | S† | M | R1 |
| UI-07 | Observe repeated expand and collapse. | No visible layout jumps. Collapse reverses the expansion path (US-005; `DESIGN_SYSTEM.md` §9) | S† | M | R1 |
| UI-08 | Toggle rapidly, including during a transition. | No stuck, half-open, or flickering state | S† | M | R1 |
| UI-09 | Inspect the expanded surface. | One unified rounded surface, compact and horizontal, not full-width, controls in the documented order Start, Search, Pinned, Running, System, Clock (`DESIGN_SYSTEM.md` §1.5, §3.2) | S† | M | R1 |
| UI-10 | Interact with controls while the transition runs. | Actions are not blocked or delayed by animation (US-005; `DESIGN_SYSTEM.md` §9) | S† | M | R1 |
| UI-11 | Open 12 windows plus several pins, and use a narrower display width. | Lower-priority items go to overflow; controls are not compressed until unusable (`DESIGN_SYSTEM.md` §11) | S† | M | R1 |

Transition feel (spring versus ease-out) is **not** tested here until OD-001 is resolved. Animation smoothness is measured in Section 7.

### 4.3 Hover, click, and outside-click

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| UI-12 | Click the notch repeatedly. | Click expands and collapses (US-005) | S† | M | R1 |
| UI-13 | If hover expansion is enabled: pass the pointer briefly over the notch, then dwell. | A brief pass does not expand. A dwell expands after an intent delay (US-005; `ARCHITECTURE.md` §4.2). Record the measured delay; the value is open (OD-018). Whether hover is on by default is open. | S† | M | R1 |
| UI-14 | Type in another app, hover-expand the island, and observe the foreground window and caret. | Hover does not steal focus from the current application (`ARCHITECTURE.md` §4.2) | S† | M | R1 |
| UI-15 | With the island expanded, click on an app behind it. | The island collapses and the intended click is not blocked (`SCOPE.md` §3). ⚑ Whether the first click is swallowed is not defined. | S† | M | R1 |
| UI-16 | Press Escape while another app has focus and the island is collapsed. | The keypress is not consumed (`PRD.md` §5, principle 5). ⚑ | S† | M | R1 |
| UI-17 | For each interactive control, inspect default, hover, pressed, selected, disabled, keyboard-focus, loading, and error states. | All applicable states defined and distinct (`DESIGN_SYSTEM.md` §8). Hover does not shift layout. | S† | M | R1 |

### 4.4 Materials, transparency, and contrast

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| UI-18 | Inspect the expanded island. | Backdrop applied to the main surface only; subtle border and soft shadow; no glow or neon (`DESIGN_SYSTEM.md` §4.2, §6) | S† | M | R1 |
| UI-19 | Turn off Windows "Transparency effects", then re-enable. | Solid fallback `Surface.IslandFallback` is used and remains readable (`DESIGN_SYSTEM.md` §4.2) | S† | M | R1 |
| UI-20 | View over a bright wallpaper, a dark wallpaper, a busy image, and playing video. Capture screenshots. | Text and icons stay legible (`DESIGN_SYSTEM.md` §4.2, §12). Contrast threshold is not defined (TOD-03): record observations. | S† | M | R1 |
| UI-21 | Inspect edges and the shadow region. | No hard outlines; shadow soft; shadow does not create a click-blocking area (`DESIGN_SYSTEM.md` §6; see UI-27) | S† | M | R1 |
| UI-22 | Enable a Windows high-contrast theme, then increase Windows text size. | UI stays usable, no clipped text, focus indicators visible (`DESIGN_SYSTEM.md` §12) | S† | M | R1 |
| UI-23 | Switch Windows light/dark mode and the system accent color. | Dark graphite theme stays consistent and readable. Focus and selected states use the platform accent (`DESIGN_SYSTEM.md` §4.1). Light theme at launch is open (OD-020). | S† | M | R1 |
| UI-24 | Search the codebase for hardcoded color values outside the token definition file (for example with PowerShell `Select-String`). | None found; all colors reference named tokens (`DESIGN_SYSTEM.md` §0, §4) | S | M/A | R1 |
| UI-25 | Complete the Design Acceptance Checklist for each new component. | All items checked (`DESIGN_SYSTEM.md` §15) | S | M | R1 |

### 4.5 Click-through

A transparent pixel is not automatically click-through (`ARCHITECTURE.md` §4.2). Place a known click target (for example a desktop icon or a small test window) immediately outside the visible surface, and click it.

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| UI-26 | Collapsed: click targets just left, right, and below the visible notch. | Clicks reach the application behind (US-004; `ARCHITECTURE.md` §4.2) | S† | M | R1 |
| UI-27 | Expanded: click targets just outside the island, including its shadow margin and rounded corners. | Clicks reach the application behind; no large transparent dead zones (`PRD.md` §8.2; `DESIGN_SYSTEM.md` §3) | S† | M | R1 |
| UI-28 | Click every visible control of the notch and island. | Controls receive input; click-through is not over-applied | S† | M | R1 |
| UI-29 | Click edge targets during an expand and a collapse. | The interactive region tracks the visible region; no stale "ghost" region remains | S† | M | R1 |
| UI-30 | Right-click, double-click, and drag adjacent to the visible edges. | Behaves as it would without IslandTaskbar | S† | M | R1 |
| UI-31 | Repeat UI-26 and UI-27 at each scale in 4.7. Record the gap between the visible and interactive regions. | Interactive region closely matches the visible region (`ARCHITECTURE.md` §4.2). Tolerance not defined (TOD-03). | S† | M | R1 |

Pass criteria depend on SPIKE-004 (hit-test click-through), which has no recorded result.

### 4.6 Multiple monitors

`SCOPE.md` §6: Release 1 prioritizes the primary display. Multi-monitor behavior is Release 2.

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| UI-32 | Start with a second display attached. | The notch appears on the primary display, correctly placed. No crash or unusable state. | S† | M | R1 |
| UI-33 | Attach and detach a display, and change which display is primary, while running. Record behavior. | No crash or unusable UI. Behavior on unsupported configurations is documented (US-014). | S† | M | R1 |
| UI-34 | Multi-monitor model, per-display resolution and scaling, orphaned-window checks, display attach/removal. | Defined by US-020 once the model is decided (OD-017; ADR-011). Validated before being advertised. | — | — | R2: Design only |

### 4.7 Display resolution and DPI

**Matrix.** Cells marked **G** are the Release 1 gate in `SCOPE.md` §3. Cells marked **D** extend the range required by D-008. Running all ten cells satisfies both (see X-01 and TOD-07 on which cells are mandatory).

| Resolution | 100% | 125% | 150% | 175% | 200% |
|---|---|---|---|---|---|
| 1080p (1920×1080) | G | G | D | D | D |
| 1440p (2560×1440) | G | G | D | D | D |

Run UI-35 to UI-38 in every cell. Record the original display settings first and restore them afterward.

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| UI-35 | Run UI-01 to UI-03 in the cell. | Notch centered and flush; no work-area reservation (US-014) | S† | M | R1 |
| UI-36 | Expand the island. Check every control is visible and usable. | Usable at the supported scale (`DESIGN_SYSTEM.md` §3.2, §11; US-014) | S† | M | R1 |
| UI-37 | Inspect text and icons. | Legible, not clipped or truncated; essential text not shrunk to fit (`DESIGN_SYSTEM.md` §5) | S† | M | R1 |
| UI-38 | Inspect hit targets and icon sharpness. | Comfortable targets; vector icons crisp (`DESIGN_SYSTEM.md` §7, §12) | S† | M | R1 |
| UI-39 | While running, change scaling between adjacent and extreme steps (for example 100→125, 200→100). | Remains centered and attached; not misplaced after the change (US-014; `DESIGN_SYSTEM.md` §11) | S† | M | R1 |
| UI-40 | While running, change resolution between 1080p and 1440p. | Same as UI-39 | S† | M | R1 |
| UI-41 | Change scale and resolution, then restart the application. | Correct placement and size on startup | S† | M | R1 |
| UI-42 | Where the hardware and OS support it, change display orientation. | Layout recalculated (`PRD.md` §12; `DESIGN_SYSTEM.md` §11). Orientation is not in the `SCOPE.md` gate (X-08). | S† | M | R1 |
| UI-43 | Sleep and resume the machine, and lock and unlock. | Position and layout remain correct | S† | M | R1 |

### 4.8 Keyboard and accessibility

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| UI-44 | Using only the keyboard, reach and use Start, Search, pinned launch, running-window switch, expand/collapse, settings, and exit. | Every essential action is reachable without hover (`PRD.md` §12; `DESIGN_SYSTEM.md` §12) | S† | M | R1 |
| UI-45 | Tab through the island. | Predictable, left-to-right order (`DESIGN_SYSTEM.md` §12) | S† | M | R1 |
| UI-46 | Inspect focus on every focusable control. | Clear focus indicator, independent of hover (`DESIGN_SYSTEM.md` §8) | S† | M | R1 |
| UI-47 | Press Escape to collapse, and observe where focus goes. | Collapses (US-005). ⚑ Where focus returns is not defined. | S† | M | R1 |
| UI-48 | Use the keyboard-accessible way to open the interface. | Exists (`PRD.md` §8.3). **Blocked** until the shortcut is defined (OD-018). | S† | M | R1 |
| UI-49 | Use Narrator over every control. | Every icon-only control has an understandable name; states are announced; no state conveyed by color alone (`DESIGN_SYSTEM.md` §4.3, §12) | S† | M | R1 |
| UI-50 | Compare Narrator reading order with visual order. | Consistent | S† | M | R1 |
| UI-51 | Turn off Windows animation effects where reduced motion is implemented. | Nonessential motion shortened or removed; functionality intact (`DESIGN_SYSTEM.md` §9) | S† | M | R1 |

Touch interaction is **Design only** until decided (OD-017).

---

## 5. Windows Integration Testing

### 5.1 Application launch and shutdown

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| WIN-01 | Start as a standard user. | Starts without elevation and shows its interface (US-001) | S† | M | R1 |
| WIN-02 | Disconnect all networks, then use core features. | Core taskbar behavior needs no internet or cloud account (US-001) | S† | M | R1 |
| WIN-03 | Run on a machine without Windhawk or other taskbar-modification tools. | Works; no such dependency (US-001; `ARCHITECTURE.md` §0) | S† | M | R1 |
| WIN-04 | Launch a second instance. | One instance only; never two taskbars (`ARCHITECTURE.md` §12). ⚑ What the second launch does is not defined (X-07). | S† | M | R1 |
| WIN-05 | Exit normally via the application. | The process exits, no orphaned processes remain, settings are saved. Native-taskbar restoration is SAF-02. | H | M | R1 |
| WIN-06 | Enable and disable the startup setting; sign out and in. | Starts at sign-in when enabled, does not when disabled; behavior is clearly described (US-013). Mechanism depends on OD-014. | H | M | R1 |
| WIN-07 | Display change, sleep/resume, lock/unlock, sign-out and sign-in. | Application lifecycle handled without a broken state (`SCOPE.md` §2.3) | H | M | R2 |

### 5.2 Window enumeration and switching

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| WIN-10 | Open several applications. Compare the running list against Alt+Tab and Task Manager. | Eligible top-level windows are shown (US-008; `ARCHITECTURE.md` §6) | S | M | R1 |
| WIN-11 | Include hidden, tool, shell-owned, and system windows. | Excluded from the list (US-008) | S | M | R1 |
| WIN-12 | Open and close windows while the island is open. | The list updates (US-008). No aggressive polling (`ARCHITECTURE.md` §6; see PRF-08). | S | M | R1 |
| WIN-13 | Inspect entries. | Application name and icon shown where available (US-008) | S | M | R1 |
| WIN-14 | Include Win32, packaged (Store-style), and elevated windows. | Behavior recorded. Activation succeeds "where Windows permits it" (US-009). ⚑ Elevated-window expectations are not defined. | S | M | R1 |
| WIN-15 | Open 12 application windows and switch among them. | Switching stays fast and correct (US-009; `PRD.md` §4.1) | S | M | R1 |
| WIN-16 | Open several windows of one application. | Predictable representation and selection (US-009; `DESIGN_SYSTEM.md` §10) | S | M | R1 |
| WIN-17 | Minimize windows. | Still listed; minimized state handled (`ARCHITECTURE.md` §6) | S | M | R1 |
| WIN-20 | Select a running-window entry. | The window comes to the foreground (US-009) | S | M | R1 |
| WIN-21 | Select a minimized window. | Restored and activated (US-009) | S | M | R1 |
| WIN-22 | Switch the foreground window by other means. | The active window is indicated, not by color alone (US-009; `DESIGN_SYSTEM.md` §10) | S | M | R1 |
| WIN-23 | Close a window just before selecting it; select an uncooperative window. | Failure handled without crashing the taskbar (`ARCHITECTURE.md` §10; US-029) | S | M | R1 |
| WIN-24 | Observe focus after the island collapses following a switch. | ⚑ Not defined. Record behavior. | S | M | R1 |

### 5.3 Launching and pinning

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| WIN-30 | Launch at least three pinned applications. | All launch successfully (`SCOPE.md` §3) | S | M | R1 |
| WIN-31 | Pin and launch a Start-menu shortcut, a plain executable, and a packaged application. | Each resolves and launches (`ARCHITECTURE.md` §6) | S | M | R1 |
| WIN-32 | Launch an application that is already running. | Launches or activates per the chosen launch behavior (US-006). ⚑ The behavior is not defined. | S | M | R1 |
| WIN-33 | Launch a missing or inaccessible target. | Failure is communicated without a crash (US-006; US-029) | S | M | R1 |
| WIN-34 | Pin an application. | Added to the pinned section (US-007) | S | M | R1 |
| WIN-35 | Unpin an application. | Removed (US-007) | S | M | R1 |
| WIN-36 | Reorder pins. | Order changes (US-007) | S | M | R1 |
| WIN-37 | Close and reopen the application. | Pins and order persist (US-007) | S | M | R1 |
| WIN-38 | Remove or move a pinned target on disk. | The rest of the taskbar keeps working (US-007) | S | M | R1 |
| WIN-39 | Edit `pins.json` to hold malformed entries, paths with spaces, non-ASCII, UNC, very long paths, and text that looks like a command line. | Handled safely; **no arbitrary command is executed** (`ARCHITECTURE.md` §10) | S | M/A | R1 |
| WIN-40 | Inspect icons. | Real application icons are used (`DESIGN_SYSTEM.md` §7, §10) | S | M | R1 |

### 5.4 Start, Search, clock

| ID | Procedure | Expected (source) | Class | Exec | Rel |
|---|---|---|---|---|---|
| WIN-45 | Use the Start control. | Invokes the Windows Start experience; no claim of replacement (US-010) | S† | M | R1 |
| WIN-46 | Use the Search control. | Invokes Windows Search (US-011) | S† | M | R1 |
| WIN-47 | With IslandTaskbar as the only visible taskbar, use Start and Search again. Also use the Windows key. | Remains usable (US-011). Record where Windows places the Start surface. | H | M | R1 |
| WIN-50 | Watch the clock without reopening the island. | Time and date update live (US-012) | S† | M | R1 |
| WIN-51 | Change time zone, locale, and 12/24-hour format. | Display follows system settings (US-012) | S† | M | R1 |
| WIN-52 | Sleep and resume; pass midnight and a daylight-saving change if possible. | Clock is correct afterward (US-012) | S† | M | R1 |
| WIN-53 | Use the calendar/date entry point, if included in the release. | Opens the calendar experience (US-012; US-018) | S† | M | R1 or R2 |

### 5.5 Release 2 and Release 3 integration

**Design only** until the preceding release is accepted (`SCOPE.md` §3). Mixed expectations below come from the user stories.

| ID | Area | What to verify (source) | Hardware or fixture | Rel |
|---|---|---|---|---|
| R2-01 | Volume and mute (US-015) | Visible level; adjust and mute; reflects real Windows audio state including changes made elsewhere; applied to the real session, not simulated; no-audio-device case handled | Audio output device | R2 |
| R2-02 | Network status (US-016) | Accurate connected and disconnected state for Wi-Fi and Ethernet; reaches Windows network settings; no claim of full network management | Wi-Fi/Ethernet; airplane mode | R2 |
| R2-03 | Battery (US-017) | Percentage and charging state shown, updates on change; desktop without a battery handled gracefully | Laptop and desktop | R2 |
| R2-04 | Calendar and notifications (US-018) | Clear entry points; permission and privacy followed; unsupported behaviors stated honestly; granted and denied states | Notification permission states | R2 |
| R2-05 | Tray items (US-019) | A documented list of tested supported items; each is reachable and interactive; unsupported items have a documented limitation; no universal claim (OD-016) | Chosen test apps | R2 |
| R2-06 | Explorer restart (US-021) | See SAF-07 | — | R2 |
| R2-07 | Multi-monitor (US-020) | See UI-34 | Second display | R2 |
| R3-01 | Media session (US-022) | Sessions detected; title, artist, artwork shown when available; previous, play/pause, next shown only when supported; missing metadata or unsupported commands do not break the UI. Test with at least two different media apps chosen and recorded by the tester. ⚑ Behavior with multiple sessions is not defined. | Media apps | R3 |
| R3-02 | Audio waveform (US-023) | Responds to measured audio; returns to idle in silence; follows playback start and stop; not random; minimum-necessary permission; can be disabled | Audio source | R3 |
| R3-03 | Control Center panel (US-024) | Only implemented controls; values match real Windows state; opens Windows settings where direct control is unavailable | — | R3 |
| R3-04 | Temporary status surfaces (US-026) | Appear for defined events; dismiss per documented timing; do not cover essential controls; non-essential ones can be disabled | — | R3 |
| R3-05 | Customization (US-027) | Only implemented options shown; changes persist; essential controls stay accessible; defaults usable | — | R3 |

---

## 6. Native Taskbar Safety Testing

Hiding the native taskbar changes the real desktop. A failure can leave the user without a usable taskbar. These rules implement D-010 and D-012 and take precedence over any test's convenience.

### 6.1 Mandatory rules

1. **Never run taskbar-hiding code unattended.** This includes scheduled, looped, background, CI, and autonomous agent runs. **Attended** means the user is physically present at the machine and watching for the full duration of the run.
2. **Explicit user authorization before every hiding test.** Authorization covers the listed test IDs for a single run. It is not carried over to later runs, other tests, or other sessions. Record it in the form below.
3. **A documented recovery plan must exist before any hiding test.** **No recovery plan currently exists** (DECISIONS C-14). **Every H test is therefore Blocked.** This document does not define the plan. Its location is open (OD-009).
4. **Confirm the plan before each run.** It must state how, on this machine and with the taskbar hidden, the user can: restore the native taskbar manually; end IslandTaskbar if it is unresponsive; proceed if Explorer is also unresponsive; confirm restoration; and return to the recorded original state.
5. **Prepare the machine.** Save and close important work. Record the original native-taskbar state (including auto-hide) before the run.
6. **Stop conditions.** If the native taskbar does not return, or the user cannot reach a usable taskbar, stop all hiding tests, execute the recovery plan, file a **Critical** defect (Section 9), and re-authorize before any further hiding test.
7. **Verify restoration after every H test**, even when the test passed.
8. Do not terminate or restart `explorer.exe` in any automated step. Explorer-restart tests are manual and attended (`AGENT.md` §7).
9. Agents may prepare tests and scripts. An agent may execute an H test only with the user's explicit authorization for that run and the user present.

**Authorization record**

```
HIDING-TEST AUTHORIZATION
Date/time:
Authorized by (user):
Tester (person or agent):
Test IDs covered (this run only):
Machine / Windows build:
IslandTaskbar build / commit:
Recovery plan reference and version:
User present at the machine for the whole run: Yes / No
Important work saved and closed: Yes / No
Original native taskbar state recorded: Yes / No
```

### 6.2 Safe tests versus tests that require authorization

| Class | What it covers | Authorization |
|---|---|---|
| **S: Safe** | Unit and integration tests with a **fake** `ITaskbarVisibilityController`; read-only queries of native-taskbar state; settings, pins, and persistence logic; window enumeration, launching, and display/DPI queries; first-run logic up to the point before hiding (using the fake); prototype windows that contain **no** hiding code; static checks. | Not required |
| **S†: Safe if hiding is not executed** | UI and interaction tests on a build or configuration that does **not** execute hiding. | Not required only while hiding is not executed. See below. |
| **H: Hiding** | Any test that calls the hide operation or runs a build where hide-on-launch is active: hide and restore; normal-exit restore; crash and force-kill recovery; Explorer restart while active; startup at sign-in with hiding; the end-to-end first-run flow after acknowledgement; end-to-end runs of the full application. | **Required for every run** |

**S† rule.** Once hiding is integrated, a UI test on the full application is H unless an approved configuration runs the application without hiding. The starting-point `settings.json` in `DATABASE_SCHEMA.md` §4 has `taskbar.hideNativeOnLaunch: true`, but no document approves a "no-hide" test configuration (TOD-01). Until one is approved, treat full-application UI tests as H.

### 6.3 Safety tests

All are **M**, class **H**, and **Blocked** until Section 6.1 rule 3 is met. Procedures will be refined by SPIKE-001 and SPIKE-002.

| ID | Scenario | Expected (source) | Rel |
|---|---|---|---|
| SAF-01 | Start the app with the native taskbar originally auto-hide **off**, then repeat with it **on**. | Native taskbar hidden, only IslandTaskbar visible, never two (US-002; `ARCHITECTURE.md` §5.1). Original state recorded. Auto-hide not substituted if the native taskbar can reappear. | R1 |
| SAF-02 | Normal exit via the application. | Native taskbar fully restored to its recorded original state; user taskbar preferences not permanently changed (US-002, US-003); helper (if any) exits cleanly (`ARCHITECTURE.md` §5.2). Compare Windows taskbar settings before and after. | R1 |
| SAF-03 | Application crash (unhandled exception). | Defined recovery path restores the native taskbar (US-003). **Blocked** until a crash-simulation method is approved (TOD-02). | R1 |
| SAF-04 | Forced termination (Task Manager "End task"; command-line termination). | Recovery restores the native taskbar without restarting Windows (US-003). Record time to recover (threshold undefined, TOD-03). | R1 |
| SAF-05 | A person who did not write the plan follows the **documented manual procedure** literally. | Native taskbar restored; the procedure is complete and followable (US-003: "documented way to restore") | R1 |
| SAF-06 | Verify automatic recovery, including helper start, monitoring, restore, and clean exit. | Only if a recovery helper is adopted (OD-009; `ARCHITECTURE.md` §5.2). Otherwise Not applicable. | R1 |
| SAF-07 | Explorer restart while active, and while the app is not running. | App does not assume handles are unchanged; re-discovers the taskbar, reapplies the intended state; never two taskbars; recovery still available (US-021; `ARCHITECTURE.md` §5.1). Required for Release 2. In Release 1, run as an observation to confirm the user is never stranded (X-13). | R2 / R1 obs. |
| SAF-08 | Sign out, restart, shut down, and sleep/resume while active, and after a forced termination. | A usable native taskbar is available after sign-in; no permanent stranding. Depends on the hiding mechanism (OD-008). | R1 |
| SAF-09 | Repeat hide and restore more than once in a session. | Recorded state does not drift between cycles | R1 |
| SAF-10 | Launch a second instance while hidden. | Recorded restore information is not corrupted ⚑ (X-07) | R1 |
| SAF-11 | Present a stale, corrupted, or mismatched `recovery.json` (only if the helper is adopted). | Does not restore wrongly or crash; handled safely. Parsing logic is **S** with a fake; real behavior is **H**. | R1 |
| SAF-12 | Attach a display while the native taskbar is hidden. | No second native taskbar becomes visible (`ARCHITECTURE.md` §5.1 item 1). Primary-display scope in Release 1; full model in Release 2. | R1 / R2 |
| SAF-13 | After each failure mode above, confirm the user has a usable taskbar. | The user is never permanently stranded (`PRD.md` §12; `SCOPE.md` §9; US-029) | R1 |

**Evidence for every H test:** authorization record; recovery plan version; build and commit; before and after screenshots; Windows taskbar settings before and after; process list; relevant logs; observed recovery time.

### 6.4 Open dependency: SPIKE-001 / SPIKE-002 recovery sequencing

**Recorded, not resolved** (DECISIONS C-06).

- D-010 requires a documented and tested recovery strategy, manual and automatic, **before** native taskbar hiding is implemented.
- `ARCHITECTURE.md` §11 requires SPIKE-001 (reversible hide/restore) and SPIKE-002 (is a recovery helper necessary?) to run hiding experiments in order to learn what recovery needs.
- Whether a spike counts as "implementing" hiding, and whether a documented manual-only plan is enough to begin spike runs, is **not decided** and is not decided by this document.
- Until the Product Owner and Chief Engineer clarify, **every** execution of hiding code, spike or production, is subject to 6.1 (D-012).

| Spike | Result | Testing it affects |
|---|---|---|
| SPIKE-001 | None recorded | Mechanism and procedures for SAF-01, SAF-02, SAF-08, SAF-09 |
| SPIKE-002 | None recorded | Whether SAF-06 and SAF-11 apply; SAF-03, SAF-04 expectations |
| SPIKE-003 | None recorded | UI-01 to UI-03 pass criteria |
| SPIKE-004 | None recorded | UI-26 to UI-31 pass criteria |
| SPIKE-005 | None recorded | WIN-06 startup-at-sign-in procedures |

---

## 7. Performance Testing

**Only targets documented in approved documents are pass/fail.** Everything else is measured and reported as a baseline. No other threshold may be invented. Approving thresholds is a decision for the Product Owner and Chief Engineer (TOD-04).

### 7.1 Documented targets

| Target | Source |
|---|---|
| Idle CPU under 1% | `PRD.md` §12; `SCOPE.md` §0, §3; `ARCHITECTURE.md` §9; US-028 |
| Idle memory under 150MB | same |
| Animations smooth at 60fps; no dropped frames during expand/collapse | `PRD.md` §12; `ARCHITECTURE.md` §9 |
| UI interactions feel immediate, with no visible lag; desktop remains responsive | `PRD.md` §12; `ARCHITECTURE.md` §9; US-028 |
| No continuous unnecessary polling; bounded sampling; reduced monitoring when idle or closed | `ARCHITECTURE.md` §9; US-025; US-028 |

No target is documented for **startup time**, **background monitoring overhead beyond idle**, or **resource cleanup**. Measure and report them with no pass/fail.

### 7.2 Measurement conditions

- Use a **Release** build (`dotnet build -c Release -p:Platform=x64`). Figures from Debug builds are informational and may not be cited as evidence of a target.
- Run on the target machine as a standard user with a recorded environment (Section 3.5), a quiet baseline (record what else is running), and the power mode recorded.
- State the **idle definition** you used (for example: collapsed, no input, no feature panels open) and the **observation window and sample interval** you used. The standard values are not approved (TOD-04).
- Use built-in tools only: Task Manager, Resource Monitor, Performance Monitor (per-process CPU, private bytes, working set, handle and thread counts), Event Viewer. The method for frame pacing is not approved (OD-003).

**Proposed, pending Chief Engineer approval under OD-003:** the tool choices above are a proposal, not a decision.

### 7.3 Tests

| ID | Evaluate | Procedure | Expected |
|---|---|---|---|
| PRF-01 | **Startup time** | Cold start (after reboot) and warm start. Record the time from process launch to the notch being visible and interactive; also to "native taskbar hidden" for H runs. Repeat for first run and later runs. | Report only. No target. Start and end events are not approved (TOD-04). |
| PRF-02 | **Idle memory** | Record working set, private working set, and private bytes while idle. Include all IslandTaskbar processes (any recovery helper) and also the main process alone. | Under 150MB. ⚑ Which metric and which process scope the target refers to is not defined (TOD-04). Report all and state which was compared. |
| PRF-03 | **Memory growth** | Record before and after repeated expand/collapse, window open/close churn, and pin changes. | Report only. Flag steadily increasing memory or handle count as a suspected leak. |
| PRF-04 | **Idle CPU** | Measure average CPU over the observation window. Record both percent of one core and percent of total CPU. | Under 1%. ⚑ The basis of "1%" is not defined (TOD-04). Report both and state which was compared. |
| PRF-05 | **CPU during interaction** | Measure during expand/collapse, window switching, and app launch. | Report only |
| PRF-06 | **UI responsiveness** | Interact while a CPU-heavy process runs; with 12+ windows; with a slow or failing app launch. Observe whether the UI thread ever stops responding. | Interactions feel immediate; the island never blocks the desktop (`ARCHITECTURE.md` §9) |
| PRF-07 | **Animation smoothness** | Repeat expand/collapse at each scale and resolution in 4.7, and over playing video. Count dropped frames using the approved method. | No dropped frames at 60fps. **Blocked** until the method is approved (OD-003) and motion work exists (OD-001; `SCOPE.md` §0). |
| PRF-08 | **Polling and wake-ups** | At idle, observe periodic CPU activity and wake-ups. Review code for per-control timers. | Event-driven; no continuous polling (`ARCHITECTURE.md` §9). Record any measured interval. |
| PRF-09 | **Monitoring overhead** (Release 3) | Compare CPU, memory, and (on a laptop) Task Manager power-usage columns with monitoring panels open and closed. | Sampling reduced or paused when the panel is closed; bounded rate (`ARCHITECTURE.md` §9; US-025). Report only. |
| PRF-10 | **Cleanup after normal exit** | After exit, check for residual processes, windows, handles, and temporary files; check settings files are valid. | No leftovers; helper (if any) has exited |
| PRF-11 | **Cleanup after forced termination** | Same checks. | No orphaned processes beyond the intended recovery; settings files not corrupted. H if hiding is active. |
| PRF-12 | **Long-run stability** | Run for an extended session. Record memory, handle, GDI/USER-object counts over time. | Report only. Duration not approved (TOD-04). |

**Performance report fields:** environment record; build configuration and commit; idle definition; observation window and interval; tool used; samples (minimum, average, maximum); result compared against the documented target, or "baseline only".

---

## 8. Feature-Specific Tests

Tests follow release priority (`SCOPE.md` §3). Release 2 and Release 3 items are **Design only** until the previous release is accepted.

### 8.1 First-run experience (US-000; D-006) — Release 1

| ID | Procedure | Expected (source) | Class | Exec |
|---|---|---|---|---|
| FR-01 | Launch with no stored state. | A brief explanation of what IslandTaskbar does and what will change (native taskbar hidden, custom interface shown) (US-000) | S† | M |
| FR-02 | Read the explanation before acknowledging. | The user is told how to exit and restore the native taskbar **before** anything is changed (US-000) | S† | M |
| FR-03 | Observe the native taskbar before acknowledgement. | Not hidden before acknowledgement; never hidden silently (US-000). Logic verified **S** with a fake controller; the real run is **H**. | S / H | M |
| FR-04 | Acknowledge. | The experience starts normally (US-000) | H | M |
| FR-05 | Relaunch. | The explanation does not repeat (US-000) | H | M |
| FR-06 | Inspect how completion is stored. | Stored locally, not in a database (D-006). **Blocked** until the location and field are defined (OD-012). | S | M/A |
| FR-07 | Close or decline the explanation without acknowledging. | The native taskbar stays unmodified (US-000). ⚑ What the app does next is not defined. | S† | M |
| FR-08 | Corrupt or remove the stored state. | Safe defaults; app keeps running (`DATABASE_SCHEMA.md` §5) | S | M/A |
| FR-09 | Operate the first-run screen by keyboard and Narrator. | Fully reachable and readable (`PRD.md` §12) | S† | M |

Resetting first-run state for repeat testing depends on OD-006 and OD-012.

### 8.2 Release 1 taskbar functionality, traceability

| Story | Verified by |
|---|---|
| US-000 first-run | FR-01 to FR-09 |
| US-001 start and use | WIN-01 to WIN-03 |
| US-002 one taskbar | SAF-01, SAF-02 |
| US-003 recovery | SAF-02 to SAF-08, SAF-13 |
| US-004 notch | UI-01 to UI-04, UI-26 |
| US-005 expand and collapse | UI-05 to UI-17, UI-44 to UI-51 |
| US-006 launch | WIN-30 to WIN-33, WIN-40 |
| US-007 pins | WIN-34 to WIN-39, PER-01 to PER-10 |
| US-008 running apps | WIN-10 to WIN-17 |
| US-009 switching | WIN-15, WIN-20 to WIN-24 |
| US-010 Start | WIN-45, WIN-47 |
| US-011 Search | WIN-46, WIN-47 |
| US-012 date and time | WIN-50 to WIN-53 |
| US-013 settings | SET-01 to SET-05 below, WIN-06 |
| US-014 display and DPI | UI-32 to UI-43 |
| US-028 responsiveness | PRF-01 to PRF-12 |
| US-029 failures understandable | ERR-01 to ERR-05, SAF-13 |

**Settings (US-013)**

| ID | Procedure | Expected |
|---|---|---|
| SET-01 | Find and open the application's settings. | Reachable (US-013) |
| SET-02 | Read the startup setting's description. | Clearly described; can be enabled and disabled (US-013) |
| SET-03 | Compare the settings UI with implemented features. | No unimplemented setting is shown as working (US-013) |
| SET-04 | Exit from settings or the island. | Exits and restores the native taskbar (US-013; SAF-02). H. |
| SET-05 | Change settings, restart. | Settings persist (`DATABASE_SCHEMA.md` §5) |

### 8.3 Local persistence (`DATABASE_SCHEMA.md` §0, §5)

| ID | Procedure | Expected | Class | Exec |
|---|---|---|---|---|
| PER-01 | Inspect files after use. | Only `settings.json`, `pins.json`, and (if adopted) `recovery.json`; stored in the per-user data location, not beside binaries | S | M/A |
| PER-02 | Inspect each file. | Contains `schemaVersion`; valid JSON | S | M/A |
| PER-03 | Remove each file. | Safe defaults; app and core features work | S | M/A |
| PER-04 | Corrupt each file (truncated, invalid JSON, wrong types). | Validated; defaults; no crash | S | M/A |
| PER-05 | Add unknown and obsolete fields. | Handled without crashing | S | M/A |
| PER-06 | Set a newer `schemaVersion`. | ⚑ Behavior not defined; must not crash. Record. | S | M/A |
| PER-07 | Interrupt a save between the temporary write and the replace (fault injection at the file-system boundary), and force-terminate during a save. | The previous file remains intact (atomic write) | S | M/A |
| PER-08 | Make a file read-only or locked; fill the disk; use a profile path with spaces and non-ASCII characters. | Failure handled; no crash; no data loss | S | M/A |
| PER-09 | Search the files for credentials, tokens, or personal data. | None (`DATABASE_SCHEMA.md` §0, §7) | S | M |
| PER-10 | Confirm no database engine is used or required to read the data. | None (D-005) | S | M |

### 8.4 Errors, privacy, and security (US-029; `ARCHITECTURE.md` §10)

| ID | Procedure | Expected |
|---|---|---|
| ERR-01 | Trigger launch failures, missing targets, inaccessible paths. | Concise, actionable messages; no secrets or unnecessary technical detail (US-029) |
| ERR-02 | Use windows with sensitive-looking titles; inspect logs. | No personal content in logs by default (US-029). Log location is undefined (TOD-08). |
| ERR-03 | Monitor outbound network activity during normal operation (Resource Monitor). | No telemetry or unrequested connections (`ARCHITECTURE.md` §9; `DATABASE_SCHEMA.md` §7; OD-022) |
| ERR-04 | Start as a standard user. | No elevation prompt (`ARCHITECTURE.md` §10) |
| ERR-05 | Review the repository for secrets, machine-specific paths, and `.env` files. | None (`ENV.md` §0, §7). **S, M/A** |

### 8.5 Internet speed meter (D-007; US-025) — Release 3, Design only

Not started until Release 1 and Release 2 are accepted. The measurement mechanism is not decided (OD-013). The meter must show **actual** download and upload throughput from measured counters and never simulated values.

| ID | Procedure | Expected |
|---|---|---|
| NET-01 | Transfer a file of known size down and up while observing the meter. Compare the meter's integrated reading with size divided by time, and with an independent built-in reference (Task Manager or Resource Monitor). | Reflects actual throughput, not link speed or a speed-test result (US-025). Record the deviation. Tolerance is undefined (TOD-03). |
| NET-02 | Connected with no traffic. | Shows a measured zero, not a stale last value |
| NET-03 | Disconnect (Wi-Fi off, unplug Ethernet, airplane mode). | Shows a **disconnected/unavailable** state, distinct from the connected-idle zero. Not fabricated values (US-025). |
| NET-04 | Reconnect. | Resumes correctly with no spike from stale counters |
| NET-05 | Switch Wi-Fi to Ethernet and back; add and remove adapters; disable and re-enable an adapter. | Tracks the change correctly; no negative, doubled, or stale readings. ⚑ Behavior with multiple active interfaces (sum or primary) is not defined. |
| NET-06 | Connect and disconnect a VPN; run with virtual adapters present. | No double counting. ⚑ Not defined. |
| NET-07 | Generate LAN-only traffic. | ⚑ Whether LAN traffic counts toward an "internet" speed meter is not defined. Record. |
| NET-08 | Sleep and resume; counter reset; very high throughput. | No fabricated spike, overflow, or crash |
| NET-09 | Block all network access and inspect for motion in the meter. | No animation or values appear when there is no data (`AGENT.md` §9) |
| NET-10 | Review the source of the displayed values. | Derived from measured OS counters. Any placeholder is visibly labeled and not counted as implemented. |
| NET-11 | Read the units shown and the accessible name. | Units clearly labeled. ⚑ Unit convention (bits or bytes) is not defined. |
| NET-12 | Measure CPU and battery cost with the meter on and off (PRF-09). | Sampling bounded; no unnecessary CPU or battery use (US-025) |

**CPU, memory, GPU indicators (US-025).** Compare readings with Task Manager under controlled load. Unavailable metrics (for example, a GPU with no counters) must display as unavailable, not as a number. Accuracy tolerance is undefined (TOD-03).

---

## 9. Defect Reporting

### 9.1 Severity levels

| Level | Definition |
|---|---|
| **Critical** | The user is, or can be, left without a usable taskbar; the native taskbar cannot be restored; two taskbars are visible; an arbitrary command can be executed through pin or settings data; data corruption or loss; a crash while the native taskbar is hidden. |
| **High** | A Release 1 core workflow fails (launch, switch, Start, Search, expand/collapse, display handling) with no reasonable workaround. |
| **Medium** | A feature misbehaves but has a workaround; a documented performance target is missed; a design-system violation that leaves the UI usable. |
| **Low** | Cosmetic issue or minor inconsistency with no functional effect. |

**Effect on acceptance.** Any defect that causes an acceptance criterion to fail blocks that story and release regardless of severity (`USER_STORIES.md` §7; `SCOPE.md` §3). Other defects are accepted or deferred only by the Product Owner. A **Critical** defect found in a hiding test stops all hiding tests until it is fixed and a new run is authorized (6.1 rule 6).

### 9.2 Defect report template

```
ID / Title:
Severity:   Critical | High | Medium | Low
Status:     New | Confirmed | In progress | Fixed - awaiting verification | Verified fixed | Duplicate | Cannot reproduce | Deferred (Product Owner decision)
Release / Story / Test ID:
Build and commit:
Environment: (paste the record from Section 3.5, including the original native taskbar state)
Steps to reproduce: (numbered, starting from a stated app state and settings)
Expected behavior: (cite the document section or acceptance criterion)
Actual behavior:
Frequency: Always | Intermittent (n of m runs)
Workaround:
Evidence: screenshots, screen recording, logs, Event Viewer entries, process list, before/after native taskbar state
Regression status: Is this a regression? Last known good build; commit that introduced it; regression test added or updated (ID); build in which it was verified fixed; neighboring tests re-run.
```

### 9.3 Evidence and privacy

- Attach real evidence only. Never fabricate, edit, or approximate it.
- Remove personal content (window titles, file names, account details) from logs and screenshots before sharing.
- Application log location and evidence storage location are not defined (TOD-08).
- Where the defect tracker lives is not defined (TOD-08). Until it is, file reports in the task report format of `AGENT.md` §15.

### 9.4 Fix verification

1. Re-run the failing test and confirm it passes with evidence.
2. Add or update a regression test (manual or Auto-eligible) that would have caught the defect.
3. Re-run the impacted-area suite. Re-run the safety-critical suite if lifecycle, visibility, or recovery was touched.
4. A fix to a hiding-related defect needs a new authorization before it is re-tested.

---

## 10. Release Acceptance Criteria

**Acceptance is never declared by the agent or tester who ran the tests.** The Product Owner accepts; the Chief Engineer reviews (`DECISIONS.md` §2.2). Criteria cannot be waived or changed here. Changes go through `SCOPE.md` §9 and `DECISIONS.md` §2.

### 10.1 What does not count

Successful builds; unit tests alone; code inspection; mocks; placeholders; simulated values; tests marked Not run, Blocked, or Design only; results without evidence (`ENV.md` §9; `USER_STORIES.md` §0, §7).

### 10.2 Prototype or spike claim

A prototype may be described only as "validated" for what was actually exercised. Required evidence:

- The spike record fields in `DECISIONS.md` §5: what was tested, machine and build, what worked, what did not, proposed decision.
- Environment record, build and commit, and screenshots or recordings.
- For any hiding experiment: the authorization record and the recovery plan version (Section 6).
- An explicit list of what was **not** tested.

A prototype is not release-ready. The Chief Engineer accepts the result, and the Product Owner approves the decision (`DECISIONS.md` §2.2).

### 10.3 Release 1 (MVP)

Required evidence, all on the target machine (Section 3.1):

| `SCOPE.md` §3 criterion | Required evidence |
|---|---|
| Starts and the native taskbar is no longer visible | SAF-01 |
| Native taskbar restored on normal exit | SAF-02 |
| Recoverable after crash or force-kill | SAF-03, SAF-04, SAF-05, SAF-08, SAF-13 |
| Notch top-center, no work-area reservation | UI-01 to UI-03 |
| Island expands and collapses without blocking desktop clicks | UI-05 to UI-10, UI-15, UI-26 to UI-31 |
| At least 3 pinned apps launch | WIN-30 with a record of three or more launches |
| Running windows discovered and switched | WIN-10 to WIN-24 |
| Start and Search open from the island | WIN-45 to WIN-47 |
| Clock and date visible and accurate | WIN-50 to WIN-52 |
| Correct at 1080p and 1440p at 100% and 125% | UI-35 to UI-43 in cells G. The full matrix, including 150%, 175%, and 200%, is required by D-008 (X-01). |
| Idle CPU under 1%, idle memory under 150MB | PRF-02, PRF-04 with metric definitions stated (TOD-04) |
| No console errors or unhandled exceptions in normal daily use | Log and Event Viewer review over a documented usage session. The meaning of "console errors" and the session length are undefined (TOD-05). |

In addition, all of the following must be true:

- Every acceptance criterion of US-000 to US-014, US-028, and US-029 is **Passed** with evidence.
- No required test is Not run, Blocked, or Design only.
- All H tests were run attended, with authorization records and a documented recovery plan.
- The traceability table in 8.2 is complete.
- No open Critical or High defect; no open defect that fails an acceptance criterion.
- Regression run completed on the final candidate build.
- The test execution report lists every test with status, date, tester, build, environment, and evidence reference.
- Open decisions that affect a claim are stated, not assumed (Section 11).

### 10.4 Release 2

Release 1 must be accepted first. Then: all Release 2 stories (US-015 to US-021) and the Release 2 scope in `SCOPE.md` §3 Passed with evidence; tray compatibility documented as tested supported and unsupported items, with no universal claim (US-019); multi-monitor validated on tested configurations before it is advertised (US-020); Explorer-restart behavior tested (US-021); full Release 1 regression; no open Critical or High defects.

### 10.5 Release 3

Release 2 must be accepted first. Then: all Release 3 stories (US-022 to US-027) Passed with evidence, including the internet speed meter tests in 8.5 on real measured data; full regression of Releases 1 and 2; no open Critical or High defects.

### 10.6 Evidence package

Test execution report; environment records; evidence files; performance report (Section 7); defect list with status; authorization records; traceability table; build and commit identifiers; the Product Owner's sign-off. Evidence storage is undefined (TOD-08).

---

## 11. Open Testing Decisions

### 11.1 Test framework: for the Chief Engineer

**OD-003 (DECISIONS.md): unit and UI testing frameworks and measurement strategy. Open.** No framework is approved. Do not select one or add test packages or dependencies. The Chief Engineer must decide:

- the unit and integration test framework;
- whether and how UI testing is automated;
- the measurement method for CPU, memory, and frame pacing.

Constraint to respect: no Node.js, Python, Docker, or other non-.NET runtime may be added (`ENV.md` §0). Until resolved, all execution is manual and **no test is Auto-eligible in practice**.

### 11.2 Inherited from DECISIONS.md

| ID | Effect on testing |
|---|---|
| OD-001 Animation model and timing | Motion tests and PRF-07 blocked; transition feel not testable |
| OD-002 MVVM/DI approach | Shapes how fakes are injected in unit tests |
| OD-004 Formatting and analyzers | Static-check tests (UI-24, ERR-05) may need analyzer rules |
| OD-006 App-data folder and paths | PER, FR, ERR-02 reference "the data location" only |
| OD-007 Location for spikes | Where spike test projects live |
| OD-008 Hide/restore mechanism | SAF procedures cannot be finalized |
| OD-009 Recovery helper and recovery-plan documentation | SAF-06, SAF-11; the plan required before any H test |
| OD-010 Anchoring and click-through mechanism | UI-01 to UI-03, UI-26 to UI-31 pass criteria |
| OD-011 Island window controller form | Which adapter the UI/interop tests target |
| OD-012 First-run state storage | FR-06 and first-run reset blocked |
| OD-013 Throughput measurement mechanism | NET tests |
| OD-014 Packaging, installer | WIN-06 startup procedures; install/uninstall testing |
| OD-015 Minimum Windows build | No other build is supported |
| OD-016 Tray compatibility | R2-05 |
| OD-017 Multi-monitor model, touch | UI-34, touch |
| OD-018 Interaction behavior | UI-06, UI-13, UI-48 |
| OD-019 Layout and dimensions | Geometry expectations stay provisional |
| OD-020 Palette, accent, light theme | UI-23 |
| OD-022 Telemetry | ERR-03 |
| OD-023 Versions and toolchain verification | Environment verification (Section 3.2) |

D-010 (recovery before hiding) and D-012 (taskbar-hiding safety) govern Section 6. D-008 (DPI 100% through 200%) governs 4.7.

### 11.3 New testing decisions proposed for DECISIONS.md

These are **not** in `DECISIONS.md` yet. The Chief Engineer should adopt or reject them. IDs are local until adopted.

| ID | Question |
|---|---|
| TOD-01 | Is an approved "no-hide" configuration available for UI and integration testing (relating to `taskbar.hideNativeOnLaunch`)? Without one, full-application UI tests are H. |
| TOD-02 | What approved method simulates an application crash for SAF-03 without adding unapproved production test hooks? |
| TOD-03 | Pass tolerances: notch centering, flush edge, hit-region match, contrast, recovery time, accuracy of system metrics and the speed meter against a reference. |
| TOD-04 | Performance definitions: idle definition and window, CPU percentage basis, memory metric and process scope, frame-pacing method, startup start and end events, soak duration, and thresholds for startup, cleanup, and monitoring overhead. Extends OD-003. |
| TOD-05 | What "normal daily use" means (session definition) and what "console errors" means in a WinUI desktop application (`SCOPE.md` §3, last item). |
| TOD-06 | Expected behavior for every ⚑ item: second launch; launch when already running; first-run decline or close; focus after collapse and Escape; first-click behavior on outside-click; elevated windows; secure desktop and exclusive full-screen; multiple media sessions; newer `schemaVersion`; speed-meter scope, aggregation, and units. |
| TOD-07 | DPI and resolution matrix: which cells are mandatory for acceptance; whether 175% and orientation are included. |
| TOD-08 | Locations for the defect tracker, evidence storage, test-result register, and application logs. |
| TOD-09 | Hardware and environment coverage beyond the single development PC (laptop, second display, audio and media fixtures, other GPUs, ARM64 timing). |
| TOD-10 | Whether `taskbar.restoreNativeOnExit` and `taskbar.hideNativeOnLaunch` may be user-configurable, given US-003 and US-002. |

---

## 12. Conflicts, Gaps, and Open Dependencies

Findings from this inspection (**X-** identifiers are local to this document; **C-** identifiers refer to `DECISIONS.md` §6).

| # | Finding | Handling in this document |
|---|---|---|
| X-01 | **175% scaling** is not named in any existing document. `SCOPE.md` §3 gates 100% and 125%; `DESIGN_SYSTEM.md` §5 names 100%, 125%, 150%, and 200%; D-008 states a 100% through 200% range. The matrix here includes 175% per this task's instruction. | 4.7 matrix; TOD-07. Same underlying issue as C-03. |
| X-02 | US-028 says performance targets are defined in `TESTING.md`, but no approved document defines thresholds beyond those in 7.1, and the metric definitions are missing. | 7.1 lists only documented targets; TOD-04. |
| X-03 | **SPIKE-001/002 recovery sequencing** (C-06): D-010 versus `ARCHITECTURE.md` §11. | Recorded as an open dependency in 6.4. Not resolved. |
| X-04 | **No recovery plan exists** (C-14), so D-012 blocks every H test, including the spikes. | 6.1 rule 3; all H tests Blocked. |
| X-05 | `DATABASE_SCHEMA.md` §4 includes `taskbar.restoreNativeOnExit` and `hideNativeOnLaunch`. If user-configurable, `restoreNativeOnExit: false` would conflict with US-003 (normal shutdown restores the native taskbar), and hiding disabled would conflict with US-002. US-013 mentions basic "visibility control" and "exit/recovery behavior". | TOD-10. Any test of such a setting is H. |
| X-06 | `SCOPE.md` §3 requires "no console errors" in "normal daily use"; neither term is defined for a WinUI desktop app. | TOD-05. |
| X-07 | **Single-instance behavior** is required in `ARCHITECTURE.md` §12 but has no user story or acceptance criterion. | WIN-04, SAF-10 marked ⚑; TOD-06. |
| X-08 | Orientation handling appears in `PRD.md` §12 and `DESIGN_SYSTEM.md` §11 but not in the `SCOPE.md` Release 1 gate. | UI-42 included; TOD-07. |
| X-09 | The 60fps, "no dropped frames", and "feel immediate" targets (`ARCHITECTURE.md` §9) have no defined measurement method. | PRF-06, PRF-07; OD-003. |
| X-10 | `ENV.md` §0 forbids non-.NET runtimes, which constrains UI-automation tool choices. | Noted under OD-003. |
| X-11 | US-005 says hover expansion "if enabled", while whether hover is on by default is open (OD-018). | UI-13 is conditional. |
| X-12 | `DESIGN_SYSTEM.md` §0 allows clearly marked development placeholders; `USER_STORIES.md` §0 says placeholders do not count as passing. | Placeholders are reported as not implemented. |
| X-13 | Explorer-restart handling is a controller responsibility in `ARCHITECTURE.md` §5.1 but a Release 2 story (US-021, `SCOPE.md` §3). Release 1 still requires recovery and no stranding. | SAF-07 runs as an observation in Release 1; full test in Release 2. |
| Inherited | C-01 (stale .NET 8 text in `PRD.md` and `SCOPE.md`: this document uses .NET 10 per D-004); C-02 (US-000 versus `SCOPE.md` and `DATABASE_SCHEMA.md`); C-03 (DPI gate); C-08 (spring versus no spring). | See OD-001, OD-012, 4.7. |

Documents referenced in this file that do not yet exist: `TASKS.md`, `CHANGELOG.md`, `COMPONENTS.md`, `FILE_STRUCTURE.md`, `API.md`.

---

## 13. Change Log

| Date | Change |
|---|---|
| 2026-11-19 | Initial creation. No test results are recorded in this document. |

---

## 14. References

- `PRD.md`: §4.1, §5, §8, §12, §13
- `SCOPE.md`: §0, §2.3, §3, §4, §6, §9
- `USER_STORIES.md`: §0, §7, US-000 to US-029
- `ARCHITECTURE.md`: §0, §3.3, §4, §5, §6, §9, §10, §11, §12
- `DATABASE_SCHEMA.md`: §0, §4, §5, §7
- `ENV.md`: §0, §2, §3, §4, §5, §6, §9, §12
- `DESIGN_SYSTEM.md`: §0, §1.5, §3 to §12, §15
- `BRAND.md`: §6, §9
- `AGENT.md`: §4, §7, §9, §11, §13, §15
- `DECISIONS.md`: D-004 to D-012, OD-001 to OD-023, §5, §6

---

**Document owner:** Product Owner  
**Engineering partner:** Chief Engineer  
**Change control:** Test expectations change only when the source documents or approved decisions change. Agents propose changes through a Decision Request (`AGENT.md` §16).

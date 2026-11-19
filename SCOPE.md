# Product Scope

**Working product name:** IslandTaskbar  
**Document status:** Draft v0.2  
**Last updated:** 2026-11-19  
**Depends on:** `PRD.md`

> This document defines what the product will cover, what belongs in the first release, what is deferred, and what is explicitly excluded. It assumes a clean project start; no previous prototype or generated code is part of the scope.

---

## 0. AI Agent Rules (Read First)

> **These rules apply to every AI agent, coding assistant, or automated tool working on this project. Read this section before writing any code, suggesting any feature, or making any architectural decision.**

### The agent MUST NOT do any of the following without explicit written approval:

- Add any feature not listed in Section 2 or Section 3 of this document.
- Promote a Release 2 or Release 3 feature into Release 1 (MVP).
- Inject code into or patch `explorer.exe` for any reason.
- Add a database, backend service, cloud dependency, or network requirement.
- Use Electron, web technologies, or any non-native UI framework.
- Recreate Windows system flyouts or settings panels that already exist in Windows.
- Add animations, visual effects, or decorative elements before core taskbar workflows are verified working.
- Suggest replacing the Windows shell, File Explorer, or Start menu.
- Guarantee support for notification-area icons without tested evidence.
- Expand the MVP silently — if a feature is unclear, ask before building.

### The agent MUST always:

- Build Release 1 items before touching Release 2 or Release 3.
- Prefer invoking existing Windows experiences over recreating them from scratch.
- Ensure the native taskbar recovery path is never broken by any change.
- Keep idle CPU under 1% and idle memory under 150MB.
- Run with normal user privileges unless elevation is explicitly justified.
- Match the visual direction defined in `PRD.md` Section 6 and `DESIGN_SYSTEM.md`.

---

## 1. Scope Statement

IslandTaskbar is an independent Windows 11 application intended to become the user's **single visible taskbar**. It hides the visible native taskbar while active and presents its own top-edge notch and expandable taskbar interface.

The long-term product scope is to provide the everyday functionality users expect from the Windows taskbar, together with additional media, quick-settings, and system-monitoring features. Delivery is incremental: a dependable core replacement comes first, followed by broader Windows integration and product-specific features.

The project does not aim to modify or inject code into `explorer.exe` as its primary implementation strategy.

---

## 2. In-Scope Product Capabilities

The following capabilities belong to the product's overall scope. Their release priority is defined in Section 3.

### 2.1 Taskbar Replacement and Interaction

- Run as an independent native Windows desktop application.
- Hide the visible native Windows taskbar while the replacement is active.
- Ensure that users do not see two taskbars at the same time.
- Restore the native taskbar on normal exit and provide recovery after an unexpected application failure.
- Display a compact notch attached to the top-center edge of the display.
- Expand the notch downward into a horizontal Dynamic Island-style taskbar surface.
- Support mouse and keyboard interaction, predictable dismissal, and non-intrusive transparent areas.
- Avoid reserving desktop work area for the floating notch/island interface.

### 2.2 Everyday Taskbar Workflows

- Open the Windows Start experience.
- Open or invoke Windows Search.
- Display pinned applications and support pinning, unpinning, and reordering.
- Display running application windows and switch to them.
- Launch applications from the custom interface.
- Display time and date.
- Provide access to common system controls and relevant Windows settings.

### 2.3 Windows Status and Shell Integration

- Volume and mute status/control.
- Network and Wi-Fi status, with access to Windows network controls/settings.
- Battery status on supported devices.
- Calendar and notification entry points.
- A defined strategy for notification-area/system-tray applications.
- Display, DPI, Explorer-restart, and application-lifecycle handling.
- Multi-monitor support, introduced in a staged and testable way.

### 2.4 Product-Specific Features

- Media-session detection and playback controls.
- Audio-reactive waveform based on actual system audio.
- Quick Settings / Control Center-style panel.
- Live network upload/download rate.
- CPU, memory, and GPU usage indicators.
- Temporary status and notification popups.
- User-configurable appearance, layout, and interaction behavior.

---

## 3. Delivery Scope and Priorities

### Release 1 — Core Replacement MVP

**Goal:** Prove that IslandTaskbar can replace the visible taskbar and handle the most important daily app workflows.

**Included:**

- Independent application startup and shutdown.
- Hide the native taskbar while IslandTaskbar is active.
- Restore the native taskbar on normal exit and recover it after an unexpected stop.
- One visible custom taskbar only — never two.
- Collapsed top-center notch and expandable/collapsible island.
- Click, hover (with an intent delay), outside-click dismissal, and keyboard dismissal/access.
- Application launching.
- Pinned application display and basic pin management.
- Running-window discovery and switching.
- Access to Start and Search.
- Clock and date.
- Basic application settings: startup behavior, visibility control, exit/recovery behavior.
- Correct operation on the primary development display including common resolution and DPI changes.
- Visual appearance matching the direction defined in `PRD.md` Section 6.

**Release 1 is NOT complete until all of the following are true:**

- [ ] IslandTaskbar starts and the native taskbar is no longer visible.
- [ ] The native taskbar is fully restored on normal application exit.
- [ ] The native taskbar is recoverable after an unexpected crash or force-kill.
- [ ] The notch is visible at the top-center edge and does not reserve desktop work area.
- [ ] The island expands and collapses without blocking unintended desktop clicks.
- [ ] At least 3 pinned apps can be launched successfully.
- [ ] Running windows can be discovered and switched to.
- [ ] Start and Search both open correctly from the island.
- [ ] Clock and date are visible and accurate.
- [ ] The app runs correctly at 1080p and 1440p with 100% and 125% DPI scaling.
- [ ] Idle CPU stays under 1% and idle memory stays under 150MB.
- [ ] No console errors or unhandled exceptions during normal daily use.

Release 1 does not claim complete parity with every Windows taskbar feature. It must be stable enough for ordinary daily app launching and switching.

---

### Release 2 — Essential Windows Taskbar Coverage

**Goal:** Expand the replacement into a practical everyday Windows taskbar.

**Included:**

- Volume and mute controls.
- Network/Wi-Fi status and appropriate Windows settings access.
- Battery status where hardware supports it.
- Calendar and notification entry points.
- System-tray strategy for supported applications and icons.
- Better notification-area behavior and documented compatibility boundaries.
- Multi-monitor behavior.
- More complete lifecycle handling for Explorer restarts, display changes, and user sign-in/startup.

**Release 2 starts only after Release 1 acceptance criteria are fully met.**

---

### Release 3 — IslandTaskbar Differentiating Features

**Goal:** Deliver the product features that go beyond a conventional taskbar.

**Included:**

- Media-session detection and previous/play-pause/next controls.
- Audio-reactive waveform driven by real audio data.
- Quick Settings / Control Center-style interface.
- Live network transfer rates.
- CPU, memory, and GPU monitoring.
- Temporary animated status/notification surfaces.
- Expanded customization options for layout, appearance, and behavior.

**Release 3 starts only after Release 2 acceptance criteria are fully met.**

---

> The release grouping is a planning baseline. A feature may move between releases only after its dependencies, complexity, and effect on MVP reliability are understood and approved.

---

## 4. Explicitly Out of Scope

The following are **never** part of this project unless a scope change is formally approved:

- Injecting code into, patching, or replacing internal UI components of `explorer.exe`.
- Replacing the Windows shell, desktop, File Explorer, or Start menu implementation.
- Reimplementing every Windows settings flyout when a supported Windows experience can be invoked.
- Guaranteeing compatibility with every third-party or legacy notification-area icon.
- Supporting Windows versions other than Windows 11 in the initial product releases.
- Requiring a cloud account, online service, backend, or database for core taskbar functionality.
- Building a mobile, web, or macOS version as part of this project.
- Adding unrelated desktop widgets or features that do not support the taskbar-replacement vision.
- Using Electron, web technologies, or any non-native UI stack.
- Animations or visual effects that are purely decorative and add no functional value.

---

## 5. Confirmed Tech Stack

> The tech stack is decided and locked. It is documented here to prevent the AI agent from questioning or changing it. Full details are in `PRD.md` Section 7 and `ARCHITECTURE.md`.

| Layer | Decision |
|---|---|
| Language | C# / .NET 8 |
| UI Framework | WinUI 3 (Windows App SDK) |
| System Integration | Win32 / Windows APIs |
| Database | None |
| Cloud / Backend | None |
| Web / Electron | Explicitly forbidden |

---

## 6. Compatibility and Support Boundaries

- The initial development and validation machine is Windows 11, OS build **26300.9974**.
- This build is a test target, not a promise that all Windows 11 builds are supported.
- The minimum supported Windows 11 build and formal compatibility matrix must be decided after implementation testing.
- Notification-area compatibility must be defined by tested application/icon behavior. Unsupported icons must be documented honestly — never silently skipped.
- The first MVP prioritizes the primary display. Broader multi-monitor behavior is planned for Release 2.
- The app must run with normal user privileges unless a specific feature has a demonstrated and documented need for elevation.

---

## 7. Assumptions and Constraints

- IslandTaskbar is a local-first desktop application; core operation must not depend on internet connectivity.
- The native taskbar is hidden, not deleted or permanently modified. A recovery path must always remain available.
- Windows APIs must be preferred over undocumented shell modifications wherever possible.
- The visual interface must not reserve desktop work area.
- Product reliability and essential taskbar workflows take absolute priority over decorative effects.
- No database or remote API is required by the defined product scope.
- The product name and final branding remain provisional.

---

## 8. Scope Boundaries for Remaining Documents

The following decisions are deferred to their appropriate documents:

| Decision | Document |
|---|---|
| Detailed visual tokens, typography, spacing, component appearance | `DESIGN_SYSTEM.md` |
| Brand identity and final product naming | `BRAND.md` |
| Internal modules, responsibilities, and dependencies | `COMPONENTS.md` + `FILE_STRUCTURE.md` |
| Exact Windows API usage and contracts | `API.md` + `ARCHITECTURE.md` |
| Test cases, automation strategy, quality gates | `TESTING.md` |
| Task-by-task implementation sequence | `TASKS.md` |

---

## 9. Scope Change Rules

A proposed feature or change is only accepted when it meets **all** of the following:

1. Supports the one-visible-taskbar product vision or a clearly justified user need.
2. Does not undermine reliable native-taskbar recovery under any circumstances.
3. Has a clear release priority and written acceptance criteria before work begins.
4. Does not silently expand the MVP with unrelated work.
5. Has its impact on compatibility, performance, and maintenance considered and documented.

Changes to the MVP boundary must be reflected in this document and in `PRD.md` and `TASKS.md`.

---

## 10. Scope Approval Checklist

- [ ] Overall product capabilities are correctly represented.
- [ ] Release 1 MVP boundary and acceptance criteria are agreed.
- [ ] Release 2 and Release 3 priorities are agreed.
- [ ] Out-of-scope items are understood by all contributors.
- [ ] Compatibility boundaries are accurate.
- [ ] AI Agent Rules in Section 0 are understood and will be followed.

---

**Document owner:** Product Owner  
**Engineering partner:** Chief Engineer  
**Next planned document:** `USER_STORIES.md`

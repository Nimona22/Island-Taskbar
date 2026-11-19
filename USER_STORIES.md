# User Stories

**Working product name:** IslandTaskbar  
**Document status:** Draft v0.2  
**Last updated:** 2026-11-19  
**Depends on:** `PRD.md`, `SCOPE.md`

> These stories describe user-visible outcomes, not implementation instructions. They assume a clean project start; no previous prototype or generated code is part of the project.

---

## 0. AI Agent Rules (Read First)

> **These rules apply to every AI agent, coding assistant, or automated tool working on this project. Read this section before writing any code.**

### The agent MUST understand:

- User stories define **what the user experiences** — not how to implement it.
- Acceptance criteria are **pass/fail tests** — not feature descriptions.
- A story is only done when **every** acceptance criterion passes on a real Windows 11 machine.
- UI placeholders, hardcoded values, and simulated states do **not** count as passing.
- Stories are ordered by release priority — do **not** build Release 2 or 3 stories before Release 1 is complete.

### The agent MUST NOT:

- Treat acceptance criteria as optional or aspirational.
- Build a story's UI before its core logic works.
- Mark a story complete based on code inspection alone — it must be tested.
- Add features to a story that are not in its acceptance criteria.
- Combine multiple stories into one implementation without approval.
- Skip US-001 through US-003 — these are the foundation every other story depends on.

---

## 1. Actors

- **Alex (primary user):** A power user / developer who runs 8–12 apps simultaneously, cares deeply about desktop aesthetics, and expects both beauty and full everyday taskbar functionality. Full persona in `PRD.md` Section 4.1.
- **Windows user:** Any person using IslandTaskbar as their everyday taskbar replacement.
- **Application:** IslandTaskbar itself, including its background lifecycle and recovery behavior.
- **Windows shell:** Windows services and experiences that IslandTaskbar may invoke or integrate with, such as Start, Search, application windows, notifications, and system controls.

---

## 2. Release 1 — Core Replacement MVP

### US-000 — First-run experience

**As a** Windows user, **I want** to understand what IslandTaskbar will do when I first open it, **so that** I can start using it confidently without surprises.

**Acceptance criteria**
- On first launch, the user sees a brief explanation of what IslandTaskbar does and what will change (native taskbar hidden, custom interface shown).
- The user is informed how to exit and restore the native taskbar before anything is changed.
- The app does not hide the native taskbar silently without the user's awareness on first run.
- After acknowledging the first-run information, the experience starts normally.
- First-run state is stored so the explanation does not repeat on subsequent launches.

---

### US-001 — Start and use IslandTaskbar

**As a** Windows user, **I want** IslandTaskbar to start as a normal desktop application, **so that** I can use my custom taskbar without modifying or injecting code into Explorer.

**Acceptance criteria**
- The application starts under a standard Windows user account unless a specific, documented feature requires otherwise.
- The application displays its own interface when started.
- Core taskbar behavior does not require an internet connection or cloud account.
- The application does not require Windhawk or another taskbar-modification tool.

---

### US-002 — See only one taskbar

**As a** Windows user, **I want** the native Windows taskbar hidden while IslandTaskbar is active, **so that** I see one unified taskbar rather than two.

**Acceptance criteria**
- When IslandTaskbar is active, the native taskbar is not visibly competing with the custom interface.
- IslandTaskbar remains independently rendered and does not depend on injecting code into Explorer.
- Hiding the native taskbar does not permanently delete or irreversibly modify it.
- The user can exit IslandTaskbar and return to the normal Windows taskbar.

---

### US-003 — Recover the native taskbar

**As a** Windows user, **I want** the native taskbar restored if IslandTaskbar exits unexpectedly, **so that** a crash does not leave me without a usable taskbar.

**Acceptance criteria**
- Normal application shutdown restores the native taskbar.
- An unexpected application termination has a defined recovery path.
- Recovery does not require the user to manually restart Windows.
- The user has a documented way to restore the native taskbar if automatic recovery fails.
- Recovery behavior is tested, not treated as successful based only on code inspection.

---

### US-004 — See the compact notch

**As a** Windows user, **I want** a small notch attached to the top-center edge of the primary display, **so that** the taskbar has the intended visual identity while staying unobtrusive.

**Acceptance criteria**
- The collapsed notch is horizontally centered on the primary display and touches the top edge.
- It contains a recognizable product mark or icon.
- It does not reserve desktop work area.
- Transparent areas outside the visible notch do not unnecessarily intercept mouse input.
- The notch matches the dark, glass visual direction defined in `PRD.md` Section 6.

---

### US-005 — Expand and collapse the island

**As a** Windows user, **I want** the notch to expand into a compact horizontal island, **so that** I can access taskbar actions when I need them.

**Acceptance criteria**
- Clicking the notch expands the interface.
- Hover expansion, if enabled, uses an intent delay to reduce accidental activation.
- The interface expands downward from the notch.
- Clicking outside or pressing Escape collapses the interface according to the interaction design.
- Expansion and collapse animations do not block normal interaction or cause visible layout jumps.
- The interface remains usable with keyboard navigation as well as a mouse.
- The expanded island uses rounded corners, translucent background, and smooth spring motion per `PRD.md` Section 6.

---

### US-006 — Launch an application

**As a** Windows user, **I want** to launch an application from IslandTaskbar, **so that** I can open the software I use without returning to the native taskbar.

**Acceptance criteria**
- Selecting an available application launches it or activates it if an appropriate instance is already running, according to the chosen launch behavior.
- Launch failures are communicated without crashing IslandTaskbar.
- Application entries have recognizable names and, where available, icons.

---

### US-007 — Manage pinned applications

**As** Alex, **I want** to pin, unpin, and reorder my most-used applications, **so that** the apps I open constantly are always one click away.

**Acceptance criteria**
- The user can add an application to the pinned section.
- The user can remove an application from the pinned section.
- The user can change the order of pinned applications.
- Pin configuration persists after IslandTaskbar is closed and reopened.
- A missing or unavailable application does not prevent the rest of the taskbar from working.

---

### US-008 — See running applications

**As a** Windows user, **I want** to see my running application windows, **so that** I can understand what is open and switch between tasks.

**Acceptance criteria**
- IslandTaskbar identifies eligible top-level application windows.
- Window entries show an appropriate application name and icon when available.
- System-owned, hidden, or otherwise irrelevant windows are excluded from the normal running-app list.
- The list updates when eligible windows open or close.

---

### US-009 — Switch between running windows

**As** Alex, **I want** to select any running window and bring it to the foreground instantly, **so that** I can move between 8–12 open apps without slowing down.

**Acceptance criteria**
- Selecting a running-window entry activates the corresponding window where Windows permits it.
- Minimized windows can be restored and activated.
- The active window is indicated in the interface.
- Multiple windows belonging to one application have a predictable representation and selection behavior.

---

### US-010 — Access Windows Start

**As a** Windows user, **I want** to open the Windows Start experience from IslandTaskbar, **so that** I can access the applications and actions I normally reach through Start.

**Acceptance criteria**
- A clearly identifiable Start control is available.
- Selecting it invokes the supported Windows Start experience.
- IslandTaskbar does not claim to replace or fully reimplement the Start menu in the MVP.

---

### US-011 — Access Windows Search

**As a** Windows user, **I want** to open Windows Search from IslandTaskbar, **so that** I can find applications, files, and settings using the familiar Windows search experience.

**Acceptance criteria**
- A clearly identifiable Search control is available.
- Selecting it invokes the supported Windows Search experience.
- Search access remains usable when IslandTaskbar is the only visible taskbar.

---

### US-012 — See the date and time

**As a** Windows user, **I want** to see the current time and date, **so that** I can check them without opening another application.

**Acceptance criteria**
- The displayed time updates without requiring the interface to be reopened.
- The displayed values follow the Windows system time zone and locale settings where applicable.
- The user can access the calendar/date experience if included in the current release scope.

---

### US-013 — Configure essential behavior

**As a** Windows user, **I want** basic settings for IslandTaskbar, **so that** I can control how it starts and how I return to the native taskbar.

**Acceptance criteria**
- The user can find and open the application's settings.
- Startup behavior is clearly described and can be enabled or disabled.
- The user can exit IslandTaskbar and restore the native taskbar.
- Settings that are not implemented are not shown as working controls.

---

### US-014 — Adapt to display and DPI changes

**As a** Windows user, **I want** IslandTaskbar to respond to display resolution and scaling changes, **so that** it stays correctly positioned and sized.

**Acceptance criteria**
- The notch remains centered and attached to the top edge after supported display changes.
- Text, icons, and hit targets remain usable at supported Windows scaling settings.
- The interface does not become misplaced or misaligned after a DPI or resolution change.
- Behavior on unsupported display configurations is documented and does not silently break the product.

---

## 3. Release 2 — Essential Windows Taskbar Coverage

> Release 2 stories must not be started until all Release 1 acceptance criteria in `SCOPE.md` Section 3 are fully met.

### US-015 — Control audio volume

**As a** Windows user, **I want** to adjust volume and mute directly from the island, **so that** I do not need to find another way to manage audio while the native taskbar is hidden.

**Acceptance criteria**
- Volume level is visible and can be adjusted.
- Mute and unmute are supported.
- Controls reflect the actual current Windows audio state.
- Changes are applied to the Windows audio session and not simulated.

---

### US-016 — See network status

**As a** Windows user, **I want** to see Wi-Fi or network connection status, **so that** I can tell whether I am connected without opening Windows Settings.

**Acceptance criteria**
- The interface shows an accurate, useful network status where Windows provides it.
- The user can reach the relevant Windows network controls or settings.
- The feature does not imply that IslandTaskbar can manage every network configuration directly.

---

### US-017 — See battery status

**As a** laptop user, **I want** to see battery level and charging status, **so that** I can monitor available power.

**Acceptance criteria**
- Battery percentage and charging state are displayed when the device exposes battery information.
- The interface handles desktop devices or systems without a battery gracefully.
- Battery status updates as the system reports changes.

---

### US-018 — Access calendar and notifications

**As a** Windows user, **I want** to access calendar and notification experiences, **so that** I do not lose these everyday workflows when the native taskbar is hidden.

**Acceptance criteria**
- Clear entry points are available for the calendar and supported notification experience.
- Notification access follows Windows permission and privacy requirements.
- Unsupported notification behaviors are communicated honestly.

---

### US-019 — Use supported notification-area items

**As a** Windows user, **I want** access to important notification-area/system-tray applications, **so that** I can continue using essential background utilities.

**Acceptance criteria**
- The project defines and documents which tray icons and interactions are supported.
- Tested supported items can be accessed and interacted with through the chosen strategy.
- The product does not promise universal compatibility with every legacy or third-party tray icon.
- Unsupported items have a documented limitation or fallback where practical.

---

### US-020 — Use IslandTaskbar across multiple displays

**As a** Windows user with more than one monitor, **I want** predictable taskbar behavior across my displays, **so that** I can work with my preferred monitor arrangement.

**Acceptance criteria**
- The product defines whether the island appears on the primary display only or on selected displays.
- Display attachment, removal, resolution, scaling, and primary-display changes are handled without leaving orphaned windows.
- Multi-monitor behavior is validated on supported configurations before being advertised.

---

### US-021 — Handle Windows shell restarts

**As a** Windows user, **I want** IslandTaskbar to recover sensibly when Windows Explorer restarts, **so that** the replacement remains usable and the native taskbar can be restored when needed.

**Acceptance criteria**
- IslandTaskbar detects relevant shell lifecycle changes where required.
- The application does not assume that Explorer's process or taskbar window handle remains unchanged.
- The replacement and recovery behavior are tested after an Explorer restart.

---

## 4. Release 3 — IslandTaskbar Features

> Release 3 stories must not be started until all Release 2 acceptance criteria are fully met.

### US-022 — Control media playback

**As** Alex, **I want** to see what is playing and control it from the island without switching apps, **so that** I can manage music and video while staying focused on my work.

**Acceptance criteria**
- Supported media sessions are detected through Windows media-session capabilities.
- Track title, artist name, and album artwork are displayed when available from the session metadata.
- Previous, play/pause, and next actions are shown when supported by the active session.
- Missing metadata or unsupported commands do not break the media interface.
- The media surface matches the dark, premium visual direction — no bright or inconsistent styling.

---

### US-023 — See an audio-reactive waveform

**As** Alex, **I want** the island waveform to react to actual system audio, **so that** the visualization reflects what is playing rather than showing random animation.

**Acceptance criteria**
- The waveform responds to measured audio activity.
- When there is no audio activity, the waveform returns to an idle state.
- Audio capture follows Windows privacy expectations and uses the minimum necessary access.
- Visualization remains performant and can be disabled if needed.

---

### US-024 — Use a Control Center-style panel

**As a** Windows user, **I want** a compact quick-settings panel, **so that** I can reach common controls from the island.

**Acceptance criteria**
- The panel contains only controls that are implemented and supported.
- Controls reflect actual Windows state rather than decorative or simulated values.
- Where direct control is unavailable, the panel opens the relevant Windows settings experience.

---

### US-025 — Monitor system activity

**As** Alex, **I want** to see live network transfer and hardware-usage information at a glance, **so that** I know what my system is doing without opening Task Manager.

**Acceptance criteria**
- Upload/download rates are derived from measured network counters.
- CPU, memory, and GPU values are sourced from real system metrics.
- Unavailable metrics are shown as unavailable rather than fabricated.
- Sampling and refresh rates avoid unnecessary CPU or battery usage.

---

### US-026 — Receive temporary status surfaces

**As a** Windows user, **I want** temporary status and notification surfaces, **so that** important updates are visible without keeping large panels open.

**Acceptance criteria**
- Temporary surfaces appear for defined events and dismiss according to documented timing and user interaction rules.
- They do not cover essential application controls unnecessarily.
- Users can disable or configure non-essential visual notifications where practical.

---

### US-027 — Customize the interface

**As** Alex, **I want** to customize the island's appearance and layout to match my preferences, **so that** it feels like mine and not a default product.

**Acceptance criteria**
- Only implemented customization options are exposed.
- Changes persist after restart.
- Customization does not make essential controls inaccessible.
- Defaults remain usable without requiring setup.

---

## 5. Cross-Cutting Quality Stories

### US-028 — Keep the desktop responsive

**As a** Windows user, **I want** IslandTaskbar to use system resources responsibly, **so that** it does not make the desktop feel slow.

**Acceptance criteria**
- Idle CPU stays under 1% and idle memory stays under 150MB.
- Idle and expanded states avoid unnecessary high-frequency work.
- Monitoring and animation workloads are measured during testing.
- The application remains responsive during ordinary app launching and switching.
- Performance targets are defined in `TESTING.md` before release acceptance.

---

### US-029 — Make failures understandable

**As a** Windows user, **I want** clear error information and a recovery path, **so that** I can understand and resolve problems without guessing.

**Acceptance criteria**
- User-facing errors avoid exposing secrets or unnecessary technical details.
- Diagnostic logs, if implemented, do not collect personal content by default.
- Critical failures preserve or restore access to the native taskbar whenever possible.

---

## 6. Priority Summary

| Priority | Stories | Release |
|---|---|---|
| Must have | US-000–US-014 | Release 1 — Core replacement MVP |
| Should have | US-015–US-021 | Release 2 — Essential Windows coverage |
| Later | US-022–US-027 | Release 3 — Product-specific features |
| Quality requirements | US-028–US-029 | All releases |

---

## 7. Acceptance and Change Rules

- A story is not complete until all of its acceptance criteria have been tested on a supported Windows 11 environment.
- UI placeholders do not count as implemented functionality.
- Hardcoded or simulated values do not count as passing acceptance criteria.
- A story may be split into smaller implementation tasks, but its user-visible outcome must remain clear.
- Any change to a release boundary must be reflected in `SCOPE.md` and, where it changes the product promise, `PRD.md`.
- Technical decisions and exceptions should be recorded in `DECISIONS.md` when that document is created.
- Release 2 does not start until every Release 1 story passes. Release 3 does not start until every Release 2 story passes.

---

**Document owner:** Product Owner  
**Engineering partner:** Chief Engineer  
**Next planned document:** `ARCHITECTURE.md`

# Product Requirements Document (PRD)

**Working product name:** IslandTaskbar  
**Document status:** Draft v0.2  
**Last updated:** 2026-11-19  
**Platform:** Windows 11 desktop

> This document defines the product from a clean start. It does not assume that an earlier prototype, codebase, or technical implementation exists. The product name is provisional and can be changed.

---

## 1. Product Summary

IslandTaskbar is a native Windows 11 desktop application that replaces the **visible Windows taskbar** with one custom taskbar experience inspired by the MacBook notch and macOS Dynamic Island.

When IslandTaskbar is running, users should see only IslandTaskbar's interface—not a second Windows taskbar underneath or elsewhere. The native Windows taskbar remains part of the Windows shell but is hidden while the replacement is active. IslandTaskbar runs as its own application and does not require injecting code into or patching `explorer.exe`.

The interface has a compact, always-visible notch attached to the top-center edge of the screen. It expands into a horizontal Dynamic Island-style surface that exposes taskbar actions and the product's own system features.

The product is intended to combine the visual character of a notch/island with the everyday usefulness expected from a Windows taskbar.

---

## 2. Problem and Opportunity

The standard Windows taskbar is familiar and functional, but it does not provide the compact, integrated notch-and-island experience this product aims to deliver.

Some customization tools change the appearance or position of the existing taskbar. Other applications replace the visible taskbar with an independently rendered dock. IslandTaskbar aims to bring these ideas together: an independently rendered replacement with a distinctive notch/island interface and useful Windows taskbar capabilities.

The product must not leave users with two competing visible taskbars or require them to give up essential everyday workflows.

---

## 3. Product Vision

Create one polished, reliable Windows taskbar experience that:

- Replaces the visible native taskbar rather than sitting beside it.
- Combines a top-edge notch, an expandable Dynamic Island surface, and essential taskbar actions.
- Preserves the everyday workflows users rely on: opening apps, switching windows, searching, and accessing system controls.
- Adds useful features such as media controls, audio-reactive visualization, system monitoring, and quick settings.
- Feels native to the Windows desktop while having its own visual identity.

---

## 4. Target Users

### 4.1 Primary Persona

**Alex — Power User / Developer, age 24**

- Runs 8–12 applications simultaneously throughout the day.
- Cares deeply about desktop aesthetics and visual consistency.
- Familiar with macOS and the Dynamic Island — wants that experience on Windows.
- Will immediately notice stuttering animations or layout inconsistencies.
- Wants both beauty and full everyday taskbar functionality — will not accept one without the other.
- Comfortable installing and configuring a third-party desktop tool.
- Expects the product to be stable enough to trust as a daily driver.

### 4.2 User Needs

- Quickly open and switch between applications.
- Access Start, Search, pinned apps, running apps, and common system controls.
- Keep the desktop visually clean, without two visible taskbars.
- Access media and system information without opening several separate windows.
- Trust that the replacement will not leave the desktop without a usable taskbar if the app exits or fails.

---

## 5. Product Principles

1. **One visible taskbar:** IslandTaskbar is the only visible taskbar while active.
2. **Do not break Windows:** Use normal Windows APIs and shell integration where practical; do not force Explorer injection as a product dependency.
3. **User control:** Users can open, interact with, configure, and exit the replacement. The native taskbar must be recoverable.
4. **Useful before decorative:** Taskbar actions must work; animations and visual effects must not substitute for functionality.
5. **Non-intrusive:** The collapsed notch should occupy little visual space and should not unnecessarily intercept clicks intended for other applications.
6. **Incremental delivery:** Build and verify core taskbar workflows before adding advanced system features.

---

## 6. Visual Direction

> This section defines the intended look and feel. All UI decisions made during development must align with this direction. When in doubt, refer back here before choosing a different visual approach.

### 6.1 Overall Aesthetic

- **Inspired by:** macOS Dynamic Island and MacBook notch.
- **Theme:** Dark, premium, restrained — not colorful or playful.
- **Feel:** Glass/translucent surfaces, depth through blur and shadow, not through color.
- **Motion:** Smooth, spring-based animations. Never jarring, never instant-snap.
- **NOT:** Flat Windows-default styling, bright accent colors, heavy gradients, or oversized controls.

### 6.2 Surface and Shape

- Heavily rounded corners on the expanded island surface.
- Background: dark translucent blur (similar to Windows Mica/Acrylic but controlled by the product).
- Subtle drop shadow to separate the island from desktop content below.
- No visible hard borders or outlines on the island itself.

### 6.3 Typography and Icons

- Clean, modern sans-serif font — consistent with Windows 11 system font (Segoe UI Variable) unless a better choice is decided.
- Icons: simple, monoline or filled — not colorful icon sets.
- Text should be legible at small sizes; the island is a compact surface.

### 6.4 Color Palette (Initial Direction)

| Role | Direction |
|---|---|
| Island background | Dark, semi-transparent (#1a1a1a ~80% opacity or Acrylic equivalent) |
| Primary text | White / near-white |
| Secondary text | Muted grey |
| Active / highlight | Single accent color — TBD in DESIGN_SYSTEM.md |
| Destructive / warning | Standard red — only where needed |

> Exact hex values and final palette to be defined in `DESIGN_SYSTEM.md`.

### 6.5 Animation Principles

- Expand and collapse transitions: smooth, feel physical and weighted.
- No animations that delay functionality — user actions must feel immediate even if a visual follows.
- Hover states: subtle, not distracting.
- Loading or processing indicators: minimal, non-blocking.

---

## 7. Tech Stack (Decided)

> This stack is confirmed and must not be changed without updating this document and `ARCHITECTURE.md`. The AI agent must not suggest alternatives or question this decision.

| Layer | Decision |
|---|---|
| Language | C# / .NET 8 |
| UI Framework | WinUI 3 (Windows App SDK) |
| System Integration | Win32 / Windows APIs where WinUI 3 is insufficient |
| Shell Interaction | Windows Shell APIs — no `explorer.exe` injection |
| Packaging | MSIX or self-contained — to be decided in ARCHITECTURE.md |
| Database | None required for core features |
| Cloud / Backend | None — fully local application |
| Web / Electron | Explicitly forbidden |

---

## 8. Experience and Core Behavior

### 8.1 Collapsed / Notch State

- A compact notch is attached directly to the top-center edge of the primary display.
- The notch remains visible while IslandTaskbar is active.
- It contains a recognizable product mark or icon.
- It does not reserve desktop work area.
- Transparent areas around the visible notch should not unnecessarily block clicks to applications behind it.

### 8.2 Expanded / Island State

- The interface expands downward from the notch.
- It is a compact-to-medium horizontal rounded surface, not a full-width Windows-style bar and not an oversized panel.
- It uses a restrained translucent/glass appearance, rounded corners, subtle shadow, and smooth motion.
- It provides access to the taskbar actions and controls included in the current release.
- Only visible, interactive portions should receive input; decorative transparent margins should not create large dead-click areas.

### 8.3 Interaction

The product should support:

- Click to expand or collapse.
- Hover-based preview/expansion with an intent delay, where it does not interfere with normal desktop use.
- Collapse when the user clicks outside, presses Escape, or the interface becomes inactive, according to the final interaction design.
- A keyboard-accessible way to open and use the interface.

Exact hover timing, keyboard shortcut, inactivity timeout, and animation durations are design decisions to be specified later.

---

## 9. Functional Requirements

The product's long-term goal is to cover the everyday capabilities users expect from the Windows taskbar and add its own features. Requirements are grouped by delivery priority below.

### 9.1 Core Replacement Requirements (MVP)

These define the first meaningful taskbar-replacement release:

- **FR-01** `[MUST]` Run as an independent Windows desktop application.
- **FR-02** `[MUST]` Hide the visible native taskbar while IslandTaskbar is active.
- **FR-03** `[MUST]` Show only one visible taskbar: IslandTaskbar.
- **FR-04** `[MUST]` Restore the native taskbar on normal exit and provide a recovery mechanism if IslandTaskbar unexpectedly stops.
- **FR-05** `[MUST]` Display the collapsed top-center notch and the expanded island interface.
- **FR-06** `[MUST]` Launch applications from the custom interface.
- **FR-07** `[MUST]` Display pinned applications and let users add, remove, and reorder pins.
- **FR-08** `[MUST]` Display running application windows and let users activate/switch to them.
- **FR-09** `[MUST]` Provide access to Windows Start and Search workflows. Prefer invoking the existing Windows experiences where supported rather than recreating them unnecessarily.
- **FR-10** `[MUST]` Display date and time.
- **FR-11** `[SHOULD]` Provide basic settings for startup behavior, appearance, and taskbar visibility/exit behavior as appropriate for the first release.

### 9.2 Essential System and Shell Features (Post-MVP Phase 1)

These are required for the product to approach the everyday usefulness of the native taskbar. Their exact implementation and release order will be decided during architecture and implementation planning:

- `[SHOULD]` Volume and mute controls.
- `[SHOULD]` Network/Wi-Fi status and access to relevant Windows settings.
- `[SHOULD]` Battery status for supported devices.
- `[SHOULD]` Notification access and a notification entry point.
- `[SHOULD]` Calendar/date access.
- `[SHOULD]` A practical system-tray strategy for supported icons and controls.
- `[SHOULD]` Multi-monitor behavior.
- `[SHOULD]` Correct behavior when Explorer restarts or display configuration/DPI changes.

**Compatibility note:** The product should aim for broad everyday taskbar coverage, but must not promise that every third-party legacy notification-area icon can be cloned or re-hosted. That capability depends on Windows and application-specific behavior and must be tested. Unsupported cases must be documented rather than silently presented as supported.

### 9.3 Additional Product Features (Post-MVP Phase 2)

These features differentiate IslandTaskbar from a basic taskbar replacement. They should be added after core taskbar workflows are dependable:

- `[NICE]` Media-session detection and previous/play-pause/next controls.
- `[NICE]` A waveform that reacts to actual system audio, not random animation.
- `[NICE]` Quick Settings / Control Center-style panel.
- `[NICE]` Live network upload and download rates.
- `[NICE]` CPU, RAM, and GPU usage indicators.
- `[NICE]` Temporary notification and status popups.
- `[NICE]` Customizable layout, appearance, and behavior.

---

## 10. MVP Definition and Release Boundaries

The first MVP is not expected to reproduce every native Windows shell feature. It must prove that the replacement concept works as a dependable daily interface before advanced features are added.

### MVP Must Include

- Independent application window and startup/shutdown behavior.
- One visible custom taskbar and hidden native taskbar while active.
- Reliable native-taskbar restoration/recovery.
- Collapsed notch and expandable island UI matching the Visual Direction in Section 6.
- Application launching.
- Pinned application management.
- Running-window discovery and switching.
- Start and Search access.
- Clock/date.
- Basic settings and keyboard/mouse interaction.
- DPI and display-size handling for the supported initial display configuration.

### Post-MVP

- Full notification-area compatibility.
- Advanced notification center and quick settings experience.
- Media controls and audio-reactive waveform.
- Network speed and hardware monitoring panels.
- Advanced customization and broader multi-monitor support.
- Installer, update, diagnostics, and polished first-run experience.

The release plan may promote a post-MVP item into MVP only when the team has verified the cost and impact.

---

## 11. Non-Goals

The product will not initially:

- Inject custom code into `explorer.exe` or patch undocumented Explorer taskbar internals as its primary architecture.
- Replace the entire Windows shell or file manager.
- Reimplement every Windows system flyout from scratch when a supported Windows experience can be invoked.
- Guarantee compatibility with every third-party notification-area icon.
- Support operating systems other than Windows 11 in the initial product scope.
- Require a cloud account, backend service, or database for core taskbar operation.
- Use Electron, web technologies, or any non-native UI framework.

---

## 12. Non-Functional Requirements

- **Reliability:** The native taskbar must be recoverable if the application exits unexpectedly.
- **Performance:** Idle CPU usage under 1%; memory footprint under 150MB at idle. Animations must remain smooth at 60fps without continuous unnecessary polling.
- **Responsiveness:** UI interactions should feel immediate and must not block the desktop.
- **DPI/display handling:** Positioning and sizing must respond correctly to display resolution, scaling, orientation, and display changes within the supported configuration.
- **Compatibility:** The application must be tested on the declared Windows 11 build range; broad compatibility must not be claimed based on one machine.
- **Security:** Run with normal user privileges wherever possible. Do not request elevation without a documented need.
- **Accessibility:** Essential actions must be reachable by keyboard, and controls must expose understandable names to accessibility tools.
- **Privacy:** Core functionality works fully locally. No personal usage data collected.

---

## 13. Initial Platform and Constraints

- Initial development and validation environment: Windows 11, build **26300.9974**.
- The minimum supported Windows 11 release/build is **not yet decided**.
- The product is a native Windows desktop application — see Section 7 for the confirmed tech stack.
- No existing prototype or generated code is assumed to be part of the new project.

---

## 14. Success Criteria

The first MVP is successful when:

1. A user can start IslandTaskbar and see only its custom taskbar interface.
2. The native taskbar is hidden while IslandTaskbar is active and is restored after a normal exit.
3. The native taskbar can also be recovered after an unexpected application stop.
4. The notch sits flush against the top-center edge and does not reserve desktop work area.
5. The island expands and collapses predictably without causing broad click-interception problems.
6. Users can launch pinned apps, discover running windows, and switch between them.
7. Start and Search workflows are accessible.
8. The app handles the supported display/DPI configuration without becoming misplaced or unusable.
9. The app remains responsive during normal desktop use.
10. The expanded island visually matches the direction defined in Section 6 — dark, glass, rounded, smooth motion.

---

## 15. Risks and Open Product Decisions

These questions are intentionally open; they must be resolved in later project discussions/documents rather than guessed in this PRD:

- Final product name and branding.
- Whether the expanded taskbar surface contains all controls at once or uses sections/panels for overflow.
- Exact layout and information hierarchy for pinned apps, running apps, and system controls.
- Whether hover expansion is enabled by default or is configurable.
- Default keyboard shortcut.
- Exact Windows versions/builds supported at launch.
- Multi-monitor behavior and whether each monitor receives its own taskbar surface.
- Which notification-area icons can be supported, proxied, or replaced with custom controls.
- Installer and update strategy.
- Whether optional diagnostics or telemetry will exist; default assumption is no telemetry.
- Final accent color for the design system.
- MSIX vs self-contained packaging approach.

---

## 16. Definition of Done for This PRD

This PRD is ready to guide the next project documents when the product owner agrees that:

- The product vision and one-visible-taskbar rule are accurate.
- MVP and post-MVP boundaries are acceptable.
- The functional requirements describe the intended product without promising unsupported Windows behavior.
- The tech stack is confirmed and locked.
- The visual direction is accurate enough to guide initial design decisions.
- Open decisions are explicitly listed rather than hidden behind assumptions.

---

**Document owner:** Product Owner  
**Engineering partner:** Chief Engineer  
**Next documents after approval:** `ARCHITECTURE.md` → `DESIGN_SYSTEM.md` → `AGENT.md`

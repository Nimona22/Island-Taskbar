# Design System

**Project:** IslandTaskbar (working name)  
**Document status:** Draft v0.2  
**Last updated:** 2026-11-19  
**Related documents:** `PRD.md`, `SCOPE.md`, `USER_STORIES.md`, `ARCHITECTURE.md`

> This document defines the visual and interaction foundations for the interface. It does not finalize the product brand, logo, or every pixel measurement. Provisional values are marked so they can be refined after the first native prototype is reviewed.

---

## 0. AI Agent Rules (Read First)

> **These rules apply to every AI agent, coding assistant, or automated tool working on this project. Read this section before writing any UI code, choosing any color, or making any visual decision.**

### The agent MUST NOT:

- Use hardcoded color values anywhere in the codebase — always use the named tokens from Section 4.
- Invent new color tokens without adding them to Section 4 first.
- Use a custom font — Segoe UI / Segoe UI Variable only until `BRAND.md` decides otherwise.
- Add animations that are purely decorative — every animation must have a purpose from Section 9.
- Use spring/bounce animations — ease-out for entering, ease-in for leaving only.
- Add blur, glow, or neon effects not defined in this document.
- Build hover-only interactions for essential taskbar actions — keyboard must always work.
- Use color alone to communicate state — always pair with icon, label, or shape.
- Build a full-width taskbar — the island is compact and horizontal, not edge-to-edge.
- Show two visible taskbars at any time for any reason.
- Fill the island with all features at once — overflow goes in panels.
- Skip the interaction states in Section 8 — every interactive control needs all defined states.
- Use fake or placeholder values for live data (waveform, metrics, battery) — clearly mark them as placeholder during development.
- Treat open decisions in Section 14 as decided — do not silently pick a value.

### The agent MUST:

- Reference token names (e.g. `Surface.IslandFallback`, `Radius.Island`) in code comments and XAML resources.
- Use the spacing rhythm from Section 3.3 (4/8/12/16/24 DIP) as the starting point for all layout.
- Use DIPs for all layout — physical pixels only at Win32 interop boundaries.
- Follow the layout diagrams in Section 1.5 as the spatial reference for collapsed and expanded states.
- Check every new component against the Design Acceptance Checklist in Section 15 before marking it ready.
- Keep the notch flush to the top edge — never a gap between notch and screen edge.

---

## 1. Design Direction

IslandTaskbar should feel like a polished, compact part of the Windows desktop — not a conventional full-width Windows taskbar placed at the top.

The visual direction combines:

- The minimal silhouette of a MacBook-style notch.
- The expandable, information-rich behavior of a Dynamic Island.
- The practical utility and familiarity of a Windows taskbar.
- A restrained translucent-glass material with clear, readable controls.

The interface must remain functional first. Decoration, blur, shadows, and animation must never make controls difficult to identify, reach, or use.

### 1.5 Spatial Layout Reference

> These diagrams are the agent's spatial reference for what collapsed and expanded look like. Build to match these proportions. Exact pixel dimensions are confirmed during prototyping.

**Collapsed state — notch only, flush to top edge:**

```
┌─────────────────────────────────────────────────────────────────┐  ← screen top edge
│                      ╔═══════════╗                              │
│                      ║  ⬡ icon  ║                              │
│                      ╚═══════════╝                              │
│                     (notch, centered)                           │
│                                                                 │
│                    [desktop content]                            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

- Notch is pill-shaped, rounded lower corners only.
- Top edge of notch is flush with screen top — no gap.
- Notch does NOT reserve work area — desktop content fills the full screen.
- Transparent area around the notch does not block clicks.

---

**Expanded state — island grows downward from notch:**

```
┌─────────────────────────────────────────────────────────────────┐  ← screen top edge
│          ╔══════════════════════════════════════╗               │
│          ║  ⬡  [Start][Search] [App1][App2]...  ║               │
│          ║  ──────────────────────────────────  ║               │
│          ║  [Running: Win1] [Win2]  🔊 🔋 📶 🕐 ║               │
│          ╚══════════════════════════════════════╝               │
│                   (island, expands downward)                    │
│                                                                 │
│                    [desktop content]                            │
└─────────────────────────────────────────────────────────────────┘
```

- Island grows downward — never sideways or upward.
- One unified rounded surface — not multiple floating cards.
- Compact horizontal layout — not a full-width bar.
- Clear left-to-right hierarchy: Start → Search → Pinned Apps → Running Apps → System Controls → Clock.
- Interactive area matches visible surface only — no large transparent dead zones.

---

## 2. Core Design Principles

1. **One visible taskbar:** The custom interface is the only visible taskbar while the application is active.
2. **Compact by default:** The idle notch occupies very little space. The expanded interface appears only when requested or when a deliberate notification/status event requires it.
3. **Clear hierarchy:** Frequently used actions are easy to identify; secondary actions belong in panels or overflow surfaces.
4. **Desktop-respectful:** The notch is attached to the top edge, does not reserve work area, and does not create unnecessary click-blocking regions.
5. **Consistent behavior:** Similar controls look, respond, and animate consistently across the application.
6. **Readable glass:** Translucency is visual treatment, not a substitute for sufficient foreground/background contrast.
7. **Meaningful motion:** Animation communicates expansion, selection, progress, or live data. It must not be random decoration.
8. **Accessible interaction:** Essential actions must remain available without hover and must support keyboard and assistive technology.

---

## 3. Layout and Geometry

### 3.1 Collapsed Notch

- Align horizontally to the center of the primary display by default.
- Attach directly to the top edge with no visible gap.
- Use a compact horizontal pill/notch silhouette with rounded lower corners; the top edge remains flush with the screen edge.
- Always show a recognizable product mark or icon.
- Keep the visible and interactive region closely matched.
- Do not show a second taskbar, dock, or persistent bottom bar.

The exact width, height, icon size, and optional status indicators are intentionally **not locked** in this document. They will be tuned against a running prototype and the user's display scaling.

### 3.2 Expanded Island

- Expand downward from the same top-center anchor.
- Keep a horizontal, compact-to-medium composition.
- Use one unified rounded surface rather than several disconnected floating bars.
- Maintain visual balance between app/taskbar actions and system controls.
- Avoid squeezing every future feature into the main surface. Secondary or low-frequency features should open a focused panel or overflow surface.
- The expanded surface must remain usable at common Windows display scales and resolutions.

Exact expanded dimensions and the final ordering of Start, Search, pinned apps, running apps, and system controls remain open design decisions.

### 3.3 Alignment and Spacing

Use a 4-DIP spacing rhythm as the initial layout grid:

| Token | Value | Usage |
|---|---|---|
| `Space.Micro` | 4 DIP | Tight icon-to-label or internal micro-spacing |
| `Space.Compact` | 8 DIP | Compact control padding and related item spacing |
| `Space.Standard` | 12 DIP | Standard control-group spacing |
| `Space.Section` | 16 DIP | Section padding and separation |
| `Space.Major` | 24 DIP | Major group separation and panel padding |

These are starting tokens, not a requirement to force every layout into multiples of four. Adjust for visual balance and Windows scaling.

---

## 4. Color and Surface Tokens

### 4.1 Initial Theme Direction

The initial visual direction is **dark graphite glass**. This reflects the black top-edge notch and translucent dark surface described in the PRD. A light theme is not required for the first prototype.

The values below are provisional implementation tokens. They establish consistency for the first prototype; they do not establish final brand colors.

| Token | Initial value | Usage |
|---|---|---|
| `Surface.IslandFallback` | `#17171B` | Opaque fallback if backdrop material is unavailable |
| `Surface.ElevatedFallback` | `#222229` | Menus, popovers, and secondary panels |
| `Surface.ControlHover` | `#303039` | Hovered control background |
| `Text.Primary` | `#F5F5F7` | Main labels and important values |
| `Text.Secondary` | `#C2C2CC` | Supporting labels and descriptions |
| `Text.Muted` | `#92929E` | Inactive or low-emphasis information |
| `Border.Subtle` | `#26FFFFFF` | Low-emphasis surface boundary (white ~15% opacity) |
| `Divider.Subtle` | `#1AFFFFFF` | Internal separators (white ~10% opacity) |
| `State.Success` | `#45B879` | Successful or healthy status |
| `State.Warning` | `#E6B450` | Caution or attention needed |
| `State.Error` | `#E06B6B` | Errors and destructive states |
| `Accent.Platform` | Windows system accent | Focus, selected state, primary accent until brand color finalized |

> **Rule:** Every color in the codebase must reference one of these tokens by name. No hardcoded hex values in XAML or C# outside of this token definition file.

### 4.2 Material Rules

- Use Windows-supported composition/backdrop materials where available.
- Keep a solid fallback surface so the interface remains readable when transparency effects are disabled or unsupported.
- Apply blur/translucency to the main island surface, not indiscriminately to every control.
- Use subtle borders and shadows to distinguish the island from changing desktop content.
- Do not use transparency so strong that text or icons disappear over bright wallpapers.
- Avoid excessive glow, gradients, and neon effects unless the brand direction later explicitly calls for them.

### 4.3 Color Semantics

- Color must communicate state, not merely decorate.
- Do not rely on color alone to communicate selected, muted, disconnected, charging, warning, or error states — pair it with an icon, label, shape, or accessible name.
- Interactive controls should have visually distinct default, hover, pressed, selected, disabled, and focus states.

---

## 5. Typography

- Prefer the Windows system UI typeface (Segoe UI / Segoe UI Variable where available).
- Do not bundle a custom font for the first release unless a concrete brand requirement justifies it.
- Use tabular numerals for clock, counters, and rapidly changing measurements where supported.
- Keep text short. The island is a compact interface, not a dashboard full of descriptions.

| Token | Initial size | Weight | Usage |
|---|---:|---|---|
| `Type.Display` | 16–20 DIP | Semibold | Important short values or prominent time |
| `Type.ControlLabel` | 13–14 DIP | Semibold/Medium | Primary control labels |
| `Type.Body` | 12–14 DIP | Normal | Supporting text and panel content |
| `Type.Caption` | 11–12 DIP | Normal | Secondary metadata and compact status |

These sizes are provisional and must be checked at 100%, 125%, 150%, and 200% display scaling. Do not shrink essential text below comfortable readability to fit more controls.

---

## 6. Shape, Borders, and Elevation

| Token | Initial radius | Usage |
|---|---:|---|
| `Radius.Control` | 8 DIP | Small buttons, text inputs, compact controls |
| `Radius.Card` | 12 DIP | Small cards and grouped surfaces |
| `Radius.Panel` | 16 DIP | Secondary panels and popovers |
| `Radius.Island` | 20–24 DIP | Expanded island main surface |
| `Radius.Notch` | 12 DIP lower corners | Collapsed top-edge notch |

Rules:

- The notch's top edge must align flush with the display edge — do not round the top corners in a way that creates a visible gap.
- Use thin, low-contrast borders only when needed to define an edge.
- Shadows should be soft and restrained. They must not create large invisible hit areas.
- Avoid stacking many nested rounded cards inside the already rounded island.

---

## 7. Iconography and App Icons

- Use a consistent set of simple, recognizable, vector-based icons for product controls.
- Prefer Windows-compatible vector assets (WinUI `PathIcon`/`FontIcon` or packaged SVG assets rendered appropriately).
- Use a consistent stroke/fill language and optical alignment.
- Initial icon sizes: 16 DIP for dense secondary controls, 20 DIP for standard controls, 24 DIP for prominent actions.
- Use actual application icons for pinned and running applications when available; do not replace app identities with arbitrary generic symbols.
- Provide tooltip and accessible-name text for icon-only controls.
- The final IslandTaskbar logo and brand symbol are defined separately in `BRAND.md`.

---

## 8. Interaction States

Every interactive component must define the states that apply to it:

| State | Description |
|---|---|
| **Default** | Clearly visible and not mistaken for disabled |
| **Hover** | Subtle background or emphasis; must not cause layout movement |
| **Pressed** | Immediate tactile feedback, without exaggerated scale or bounce |
| **Selected/Active** | Persistent visual indication when a control represents an active mode or view |
| **Disabled** | Visibly unavailable, with a reason exposed where useful |
| **Keyboard focus** | Clear focus indicator that is not dependent on hover |
| **Loading/Updating** | Only where an operation genuinely takes time; avoid fake progress |
| **Error** | Concise, actionable feedback without crashing or blocking the whole taskbar |

> **Rule:** Hover-only controls are not acceptable for essential taskbar actions. Every state must be reachable by keyboard.

---

### Phase 3.1 provisional prototype values

The assigned Phase 3.1 visual prototype uses a 96×24 DIP idle notch and a
320×64 DIP expanded panel, with an 8 DIP gap above the floating panel. Its
transparent host envelope is 368×88 DIP, converted to physical pixels at the
existing controller boundary. These dimensions are provisional (OD-019).
Existing `Radius.Notch`, `Radius.Island`, spacing, color and type tokens apply.
The expanded panel rounds all four corners; the idle notch has square upper
corners at the display edge.

For this prototype only, `Motion.Standard` is 220 ms: cubic ease-out for
expansion and ease-in for collapse, without bounce. The Windows reduced-motion
setting removes movement. This does not close OD-001 for the final product.
The surface uses the existing dark fallback palette with a subtle vertical
gradient and `Border.Subtle`; desktop backdrop blur is not implemented.

### Phase 3.2 Music Mode prototype values

The Phase 3.2 assignment preserves the 96×24 DIP idle notch and uses a
400×72 DIP music panel within a 448×96 DIP host. The existing 8 DIP top gap,
corner radii, 220 ms `Motion.Standard`, palette and typography remain.
Provisional music tokens: `Music.Control.Size` 32 DIP, `Music.Play.Size` 40 DIP,
`Music.Play.Radius` 20 DIP, `Music.Collapse.Size` 24 DIP,
`Music.Waveform.Height` 28 DIP, `Music.Waveform.BarWidth` 3 DIP,
`Music.Waveform.BarGap` 5 DIP, `Music.Waveform.BarRadius` 1.5 DIP.

Previous/play-pause/next act only on local prototype state. Previous/next cycle
three deterministic illustrative patterns, without track titles, artists,
artwork or audio data. The visible state caption begins with “Illustrative”.
The waveform uses fixed shapes and a deterministic time-based transform while
playing; it freezes while paused and stops in idle. Windows reduced motion
uses a static pattern. None of these visuals represent audio reactivity.

### Selected warm-glass Music Mode (native Phase 3.2 update)

The user's selected design supersedes the earlier 400×72 DIP music study.
Music Mode is 256×64 DIPs, attached at y=0, with 12 DIP concave shoulders and
22 DIP lower corners. The idle 96×24 notch and its original tokens remain.
The host is 328×80 DIPs. A shared sampled outline defines paint and native input.

`Music.Surface` uses 90%/85% warm alpha stops over the platform host backdrop
brush, without adding a blur effect or CPU-based capture. `Music.Play.Surface` uses the supplied
reference's orange direction. Controls are 24×28 DIPs, play/pause is 32×32,
icons are 14 DIPs; the source artwork renders at 40 DIPs (80-pixel decode) inside
a 52 DIP ring. The illustrative waveform is 42×22 DIPs with eleven 2 DIP bars,
2 DIP gaps and 1 DIP bar radii. Ring strokes are 1.3 DIPs. The small visible
“Demo” label exposes the playing/paused/pattern state through its accessible name.

Artwork rotates over 14 seconds; the ring and waveform use deterministic
composition animations. Animation controllers pause them in hidden/empty-viewport,
collapsed, paused and reduced-motion states. The UI timer runs only for geometry
transitions. No performance guarantee is made without measurement.

### Phase 5.1 Running Applications

The approved Phase 5.1 design adds a 320x80 DIP Taskbar Mode using the selected
warm-glass Music palette, shared 12 DIP shoulders and 22 DIP lower corners.
Idle remains 96x24 DIPs and Music Mode remains 256x64 DIPs. The transparent
host envelope grows to 368x96 DIPs; the measured native region excludes its margins.

Taskbar tokens: `Taskbar.Item.Width` 96, `Taskbar.Item.Extent` 104 (including
4 DIP left/right margins), `Taskbar.Item.Height` 52, `Taskbar.Title.Width` 88,
`Taskbar.Icon.Size` 24, `Taskbar.Indicator.ActiveWidth` 24. The content margin is
24,4,24,4 DIPs. Actual icons and window titles identify each window separately.
An active window uses an orange bar; other windows use a 4 DIP dot, with state
also announced in accessible names. Existing caption typography and control
styles apply. Scroll buttons and arrow/Home/End keys expose overflow without
shrinking entries. Enter/Space switch the focused window; Escape collapses.

Clicking idle opens Taskbar Mode. The user explicitly chose to keep it open
until closed, even if media starts or ends. A Music button opens a usable session;
the existing Music collapse control's context menu offers running applications.
Existing artwork, waveform, playback controls, and media dismissal remain intact.

## 9. Motion and Timing

Motion should make the notch-to-island transition feel continuous and deliberate.

| Token | Initial duration | Usage |
|---|---:|---|
| `Motion.Fast` | 100–140 ms | Hover and small control feedback |
| `Motion.Standard` | 180–240 ms | Expand/collapse and panel transitions |
| `Motion.Slow` | 260–320 ms | Larger secondary surface transitions |

Rules:

- Expansion originates from the notch and moves downward; collapse reverses the same path.
- Use ease-out for entering, ease-in for leaving — no spring/bounce by default.
- Avoid animating layout properties when a composition transform/opacity transition provides smoother results.
- Do not delay essential actions behind decorative animations.
- Live waveforms and usage visualizations must reflect real audio or system measurements when implemented; placeholder animation must be clearly marked during development.
- Provide or respect a reduced-motion setting where practical — shorten or remove nonessential movement without removing functionality.

Exact hover intent delay, outside-click timing, inactivity timeout, and keyboard shortcut are not finalized here.

---

## 10. Component Design Rules

### Notch
- Always visible while the replacement is active.
- Contains the product mark/icon.
- Compact, deliberate hit target — matches visible area.
- Does not display unnecessary information in idle state.

### Island Surface
- Single unified surface that expands from the notch.
- Hosts the active taskbar layout or a focused feature panel.
- Clear internal hierarchy and controlled spacing.

### Taskbar Action
- Recognizable icon and optional short label.
- Consistent hit target, focus, hover, pressed, and disabled states.
- Reports failure clearly when an action cannot be completed.

### Pinned Application Item
- Uses the real app icon at a consistent visual size.
- Supports tooltip/name, selected or launch feedback, and a context-menu path for pin management.

### Running Application Item
- Clearly distinguishes running from pinned-only apps.
- Indicates the active foreground application without relying on color alone.
- Handles multiple windows from one app through a deliberate grouping/selection pattern.

### System Control
- Shows a useful current state (volume, battery, network) when available.
- Opens a focused control surface or the appropriate Windows experience.
- Clearly indicates unavailable or unsupported hardware states.

### Popup and Secondary Panel
- Opens from its related control and remains visually connected to it.
- Does not unexpectedly cover the main island controls.
- Dismisses predictably through outside click and Escape.
- Uses the same surface, type, spacing, and focus rules as the main island.

---

## 11. Responsive Behavior and Display Scaling

- Use DIPs for layout and let WinUI handle scaling; physical pixels only at Win32 interop boundaries where required.
- Recalculate layout and anchoring when display resolution, orientation, DPI, or monitor configuration changes.
- Keep the notch centered on the selected display.
- On narrow displays, prioritize essential actions and move lower-priority items into overflow rather than compressing controls until they become unusable.
- Do not assume one fixed width works for all resolutions.
- Multi-monitor layout remains an open product decision — the visual system must not prevent it from being added later.

---

## 12. Accessibility and Usability

- Essential functions must be accessible by keyboard; hover is an enhancement, not the only entry point.
- All icon-only controls need accessible names and tooltips where useful.
- Keyboard focus must be visible and must follow a predictable order.
- Respect Windows text scaling and high-contrast/accessibility settings where supported.
- Maintain adequate contrast for text, icons, borders, and focus indicators over variable desktop backgrounds.
- Avoid relying only on tiny status dots or subtle color differences to convey important states.
- Keep hit targets comfortable for mouse and touch-capable devices.

---

## 13. Do and Don't

### Do
- Keep the collapsed notch visually minimal.
- Use one coherent glass surface for the expanded island.
- Use real app icons and recognizable Windows concepts.
- Keep controls aligned and consistently spaced using the token values.
- Use subtle motion to explain state changes.
- Prioritize taskbar workflows and readability.
- Reference token names in all code, not raw values.

### Don't
- Recreate a full-width conventional taskbar at the top.
- Show two visible taskbars.
- Fill the island with every available feature at once.
- Use random waveforms, fake live metrics, or misleading status values.
- Add blur, glow, or animation that harms clarity or performance.
- Make essential actions available only on hover.
- Create large transparent click-blocking regions around the visible interface.
- Hardcode hex color values anywhere in the codebase.
- Add spring or bounce to any animation without explicit approval.

---

## 14. Open Design Decisions

The following decisions remain intentionally open — do not silently decide them:

- Final product name, logo, and brand identity (`BRAND.md`).
- Final dark palette, accent color, and whether a light theme is needed at launch.
- Exact notch and expanded-island pixel dimensions.
- Whether time or status indicators appear in the collapsed notch alongside the icon.
- Exact ordering and grouping of Start, Search, pinned apps, running apps, and system controls.
- Which controls remain visible in the main island and which live in panels/overflow.
- Default hover behavior, intent delay, collapse timeout, and keyboard shortcut.
- Final animation curves and timing after on-device testing.
- Touch-specific interaction and detailed multi-monitor layout.

---

## 15. Design Acceptance Checklist

A design or component is ready for implementation review when:

- [ ] It follows the one-visible-taskbar product rule.
- [ ] The notch is centered and flush to the top edge in the collapsed state.
- [ ] The expanded interface grows downward and remains compact and horizontal.
- [ ] All colors reference named tokens — no hardcoded hex values.
- [ ] Text and controls remain readable over varied desktop backgrounds.
- [ ] All interaction states from Section 8 are defined and consistent.
- [ ] Transparent margins do not create unnecessary click interception.
- [ ] Essential actions are accessible without hover.
- [ ] Animations are purposeful and do not delay actions.
- [ ] The layout responds correctly to supported DPI and display changes.
- [ ] Any unresolved visual choice is recorded in Section 14 rather than silently treated as final.

---

**Document owner:** Product Owner / Lead Designer (visual direction and approval), Chief Engineer (technical consistency and implementation guidance).

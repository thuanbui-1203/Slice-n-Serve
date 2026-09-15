# Section 1 — Game Overview: Foundation Plan

**Source:** *Game Concept Document: Slice & Serve*, §1 (Story, Genre, Engine, Platforms, Vibe).
**Sibling plan:** [`section-2-core-mechanics.md`](./section-2-core-mechanics.md) — owns the board, orders, and **Phase 0 Foundations**.
**Runbook:** [`section-1-implementation-steps.md`](./section-1-implementation-steps.md).
**Locked decisions:** [`README.md`](./README.md).

---

## 1. What Section 1 owns — and what it does not

§1 of the concept document is *Story, Genre, Engine, Platforms, Vibe*. Those are not mechanics,
so Section 1 cannot deliver gameplay. It delivers the **cross-platform frame** the rest of the
project is built inside.

This plan was reconciled against a Section 2 plan that already existed and had independently
claimed a "Phase 0 Foundations" of its own. Rather than fight over the same ground, the split
is explicit:

| Area | Owner | Why |
| --- | --- | --- |
| Folder tree, assembly definitions, namespaces, layer scheme, `Physics2D` settings, physics materials, `Game.unity`, the `Board` action map, `BoardConfig` | **§2, Phase 0** | §2's choices are grounded in measurements it took from this project (the 0.02 s timestep, the tunneling budget at 40 u/s) and its tuning constants are already calibrated to them |
| Platform targets, orientation lock, aspect policy, safe-area handling, performance budgets, cross-platform parity verification | **§1, this plan** | §2 never addressed portrait phones, 4:3 tablets, notches, or non-Windows builds — and its camera assumes a fixed 16:9 frame |
| Board behaviour, knife, ingredients, orders, patience | **§2, Phases 1–5** | mechanics |

**Section 1 is done when** the project is configured to ship on Windows and Android, the app
cannot render a portrait frame, the 19.2 × 10.8 board is fully visible and un-cropped on every
supported aspect from 4:3 to 21:9, the HUD clears a landscape notch, and the performance
budgets below are stated as enforceable numbers. **Zero gameplay logic is delivered here.**

Explicitly not in Section 1: ingredients, knife physics, orders, customers, economy, upgrades,
difficulty scaling, PvP.

---

## 2. Concept → engineering translation

The §1 "vibe" sentence — *Overcooked meets Peggle/Pong, layered with a satisfying idle-game
progression loop* — is a constraint set, not decoration. Each clause is a requirement that has
to be baked in now, because retrofitting it is expensive:

| Concept clause | Engineering consequence | Where |
| --- | --- | --- |
| "Overcooked" — fast, split-attention, legible | 60 FPS floor on mid-tier mobile; HUD legible in the screen corners; state never hidden by a notch or rounded corner | §8 budgets, §6.4 safe area |
| "Peggle/Pong" — bouncing knife, angle-driven | Deterministic 2D physics at a fixed step; collision-driven, not animation-driven; a hard cap on simultaneous knives | §2 §4.6 (tunneling), §8 budgets |
| "satisfying idle-game progression" | Every balance number lives in a data asset, never in code, so §3/§4 can retune without a rebuild | §2 §3.1, §2 `BoardConfig` |
| "PC **and** Mobile, cross-platform" | One input path, one aspect policy, two build targets, no platform-specific gameplay code | §5, §7 |
| "drag-and-release … for Mobile (touch) and PC (mouse)" | Mouse, `Touchscreen` and `Pen` share one code path; power measured in world units, not pixels, so the feel is DPI-independent | §7 |

---

## 3. Locked decisions

Full log in [`README.md`](./README.md#locked-decisions). The ones this section is accountable for:

| # | Decision |
| --- | --- |
| **D1** | Unity `6000.3.16f1`, URP `17.3.0` / `Renderer2D`. Version pinned; upgrades are scheduled, not incidental. |
| **D2** | **Landscape 16:9, PC-first.** Portrait and portrait-upside-down are disabled at the OS level. |
| **D3** | **Board box is 19.2 × 10.8 world units**, keep §2's `worldBounds = Rect(-9.6, -5.4, 19.2, 10.8)`. The camera **contains-fit** that box, clamped to the `[4:3 … 21:9]` envelope, letterboxed outside it. |
| **D4** | Runtime targets: Windows x64 (primary dev + shipping), Android ARM64 (primary mobile), iOS ARM64 (configured, not built — no macOS host). |
| **D5** | The §2 action map (`Board` / `Aim` / `Launch`) is the single aiming path on both platforms. Mouse and touch must produce **identical** launch values for identical world-unit drags. |
| **D6** | **Reconciliation:** where §1 and §2 disagreed on names or numbers, §2's choice wins — folder layout, single `SliceNServe.Runtime` assembly, namespaces, 50 Hz timestep, swept ingredient collection, `Board` action map, `Game.unity`, `BoardConfig`. §1 keeps only what §2 never decided. |
| **D7** | **Layer scheme is §2's names at corrected indices:** `Knife` = 8, `Ingredient` = 9, `BoardWall` = 10, `Obstacle` = 11. §2's draft used 6–9, which collides with Unity's builtin reserved range (`TagManager.asset` shows indices 0–7 as builtin slots). |

### 3.1 Trade-offs recorded honestly

**D2 — landscape, PC-first.** A landscape board makes the ping-pong knife geometry and the
Overcooked-style counter/HUD rails straightforward, and matches the `Peggle` reference. It costs
the mobile one-handed portrait posture. If the project pivots to mobile-first, the aspect policy
in §6 survives but the board layout and every HUD layout must be re-authored.

**D6 — one runtime assembly instead of five.** The first draft of this plan used five assemblies
(`Core`/`Gameplay`/`UI`/`App`/`Editor`) specifically so that *gameplay cannot reference UI* was
enforced by the compiler. §2 uses one `SliceNServe.Runtime` assembly so that its test assemblies
can reference a single runtime surface. Adopting §2's version means **that rule is now a
convention, not a build error.** It is mitigated by §2's own design — the order and patience
systems are plain C# objects with no `MonoBehaviour` and no UI reference, which is what actually
makes them testable, and what a second assembly would have been protecting. Revisit if a UI
dependency ever appears inside `SliceNServe.Core`, `.Board`, `.Orders` or `.Data`.

**D6 — 50 Hz physics with a 60 FPS floor.** §2 keeps `Fixed Timestep = 0.02` and justifies it
(tunneling math, determinism). Physics therefore steps at 50 Hz while rendering targets 60 FPS;
`RigidbodyInterpolation2D.Interpolate` on the knife (which §2 already specifies) is what keeps
that smooth. This supersedes the first draft's proposal to move to `1/60`.

---

## 4. Ownership detail — what §1 adds on top of §2 Phase 0

§2 Phase 0 produces the project skeleton. §1 adds exactly three things to it and changes nothing
else about it:

| §1 addition | Goes in | Why |
| --- | --- | --- |
| `ViewportMath` + `BoardViewportAdapter` | `Scripts/Runtime/Core/ViewportMath.cs`, `Scripts/Runtime/Board/BoardViewportAdapter.cs` | §2 §4.4 hardcodes `orthographicSize = 5.4`, which is correct at 16:9 and wrong on every other aspect. The adapter computes it instead — and it evaluates to **exactly 5.4 at 16:9**, so none of §2's world-unit tuning changes. |
| `SafeAreaFitter` | `Scripts/Runtime/UI/SafeAreaFitter.cs` | §2 has no safe-area story at all, and a landscape notch sits on the left or right edge where its HUD is. |
| Player/platform settings | `ProjectSettings/` | Orientation lock, bundle identifiers, IL2CPP/ARM64, colour space, incremental GC. §2 explicitly left these alone. |

`Scripts/Runtime/UI/` is a new folder under §2's runtime layout, namespace `SliceNServe.UI`, for
cross-cutting view plumbing. §2's own views (`OrderTicketView`, `CustomerView`) stay where §2 put
them.

---

## 5. Platform & build-target matrix

| Target | Class | Scripting | Arch | Min OS | Build in §1? |
| --- | --- | --- | --- | --- | --- |
| **Windows x64** | primary dev + PC | IL2CPP (Mono OK for dev builds) | x86_64 | Windows 10 | yes — reference build |
| **Android** | primary mobile | IL2CPP | **ARM64 only** | API 25 (Android 7.1) | yes — must build and boot |
| **iOS** | secondary mobile | IL2CPP | ARM64 | iOS 13.0 | configuration only, no build |
| macOS | optional | — | — | — | **no** — see O2 |
| WebGL | not planned | — | — | — | no |

- `AndroidTargetArchitectures: 2` is already ARM64-only, satisfying Google Play's 64-bit rule.
  Do **not** add ARMv7 "just in case" — it doubles build time for no supported device.
- API 25 is Unity 6's default. Raising it is a shipping-time decision.
- iOS is configured but not built (no macOS host here). Treat it as "must not be actively
  broken" — no Windows-only packages, no platform-gated code without an `#if` — not as verified.
- `companyName` / `productName` / bundle identifiers must be set. The template default
  (`DefaultCompany`) is a release-blocking placeholder.

---

## 6. Display, orientation & aspect policy

### 6.1 Orientation

- `Default Orientation` → **Landscape Left**.
- Mobile: **Auto Rotation with only the two landscape orientations enabled.** Portrait and
  Portrait Upside Down are off.
- The project currently has `defaultScreenOrientation: 4` = Auto Rotation with all four allowed.
  **Locking this down is the highest-value change §1 makes**: a portrait frame around a
  19.2 × 10.8 landscape board is a broken shipping build, not a layout bug you patch later.
- Keep *both* landscape orientations rather than hard-locking `Landscape Left` — a player with
  the charging port on the right would otherwise see the game upside down.

### 6.2 The board box

`BoardConfig.worldBounds = Rect(-9.6, -5.4, 19.2, 10.8)` — §2's value, unchanged. Half-width
`9.6`, half-height `5.4`. Everything §2 authors in world units (launcher at `(0, -4.4)`, the
ingredient zone, the 8 → 22 u/s launch envelope) is untouched by this plan.

### 6.3 Aspect policy

One rule, `ViewportMath`:

```
effectiveAspect = clamp(screenAspect, 4/3, 21/9)
orthoSize       = max(boardHalfHeight, boardHalfWidth / effectiveAspect)
```

**Contain-fit:** the whole 19.2 × 10.8 box is always visible, at every aspect, never cropped.
Leftover space becomes backdrop and HUD rails.

| Screen aspect | `orthoSize` | Visible world area | Leftover space |
| --- | --- | --- | --- |
| 4:3 (1.333) | 7.200 | 19.2 × 14.4 | 1.8 u of backdrop above and below |
| 3:2 (1.500) | 6.400 | 19.2 × 12.8 | 1.0 u above and below |
| 16:10 (1.600) | 6.000 | 19.2 × 12.0 | 0.6 u above and below |
| **16:9 (1.778)** | **5.400** | **19.2 × 10.8** | **none — exactly §2's reference frame** |
| 19.5:9 (2.167) | 5.400 | 23.4 × 10.8 | 2.1 u of backdrop left and right |
| 20:9 (2.222) | 5.400 | 24.0 × 10.8 | 2.4 u left and right |
| 21:9 (2.333) | 5.400 | 25.2 × 10.8 | 3.0 u left and right |
| 32:9 (3.556) | 5.400 | 21:9 **pillarboxed** | black bars left and right |

The `5.400` at 16:9 is the important row: the policy reproduces §2's hardcoded camera exactly at
its reference aspect, so adopting it costs §2 nothing.

Outside `[4:3 … 21:9]` the viewport is clamped and `Camera.rect` is set to the largest centred
rectangle of the clamped aspect — a letterbox. This exists for one reason: **an ultrawide player
must not be able to see more board than a 16:9 player and gain an aiming advantage.** Inside the
envelope the extra space is decorative only.

Because the board is authored in world units and the policy only ever *widens the frame*, **§2's
world-unit tuning is aspect-independent** — speeds, radii and spawn geometry behave identically
on a 4:3 tablet and a 20:9 phone. Only the on-screen size of the board changes. That is the main
reason to adopt contain-fit here rather than §2's fixed-16:9 camera.

**Not used:** Unity's Pixel Perfect Camera. It quantises the orthographic size and fights a
contain-fit policy. Whether the art is pixel-perfect is a later art decision; revisit then.

### 6.4 HUD & safe area

- Canvas: Screen Space – Overlay, `CanvasScaler` = Scale With Screen Size, reference
  **1920 × 1080**, match **0.5**.
- HUD root is inset by `Screen.safeArea` on **all four edges**, via one `SafeAreaFitter`
  component on the HUD root. In landscape the notch is on the *left or right* edge, so horizontal
  insets are the ones that actually bite.
- Android has `androidRenderOutsideSafeArea: 1`. **Keep it:** the board and backdrop should fill
  the physical display; only the UI respects the safe area. Safe-area handling lives in one
  component, never in individual layouts.
- One-time setup §2 flagged: `Window → TextMeshPro → Import TMP Essential Resources` before the
  first ticket renders text.

---

## 7. Cross-platform input parity

§2 owns the input implementation (§2 §5.2): the `Board` action map with `Aim` and `Launch`, the
`AimInput` state machine, and the drag-to-velocity mapping
(`power = Clamp01(|drag| / maxDragLength)`, `maxDragLength = 3.5 u`, dead-zone `0.35 u`,
`launchDir = -drag.normalized`, `speed = Lerp(8, 22, powerEase(power))`).

**Section 1 owns the requirement that those numbers are platform-independent, and the evidence
that they are.** That is not automatic — it is a property someone has to protect:

1. **One binding set, not two.** §2 binds `<Pointer>/position`, `<Touchscreen>/primaryTouch/position`
   and `<Pen>/position` on `Aim`. All three derive from the `Pointer` device layout, so the same
   `InputAction` serves mouse, touch and pen, and `AimInput` needs no `#if UNITY_IOS` branch.
   *(Equivalent shorthand if the binding list ever grows: a single `<Pointer>/position` binding
   covers the same three devices. Keep §2's explicit list unless there is a reason to change it.)*
2. **Power is measured in world units, not pixels.** §2's `maxDragLength` is a world-unit
   `BoardConfig` field — correct. A pixel-normalised drag would make a phone thumb and a desktop
   mouse produce different power for the same intent, which is exactly the bug §5 PvP cannot
   tolerate later.
3. **No gameplay value may branch on device.** §2 guards multi-touch by binding `primaryTouch`
   only; that is an input-shape guard, not a gameplay one. Nothing downstream of `AimInput` may
   read "is this a touch".
4. **Verification (runbook Step 5):** one drag of the same world-unit length, once with the mouse
   and once through the Device Simulator's touch path, must report identical `power` and
   `direction`.

Because `AimInput` arrives in §2 Phase 1, §1's parity check depends on Phase 1 landing. §1 defines
the requirement and the procedure; it cannot execute it alone.

---

## 8. Performance & non-functional budgets

Derived from the "fast-paced, ping-pong" vibe. These are §1 commitments — later sections fit
inside them, they don't renegotiate them.

| Budget | Target | Why |
| --- | --- | --- |
| Frame rate floor | 60 FPS on a mid-tier Android (Snapdragon 6-series class) | Bounce readability collapses below 60 |
| Physics step | Fixed `0.02` (50 Hz), max allowed timestep `0.3333` | §2's decision; knife uses `RigidbodyInterpolation2D.Interpolate` to stay smooth against a 60 FPS display |
| Speed guard | `speedHardCap` 40 u/s, knife collider radius 0.18 u, walls 2.0 u thick | §2 §4.6; the anti-tunneling budget |
| Live ingredients | §2 gameplay target 12, max 18. **Ceiling 120 sprites** | 12/18 is the design target; 120 is the point past which the frame budget is at risk, so §3 spawn-rate upgrades must scale between them, not past them |
| Simultaneous knives | **Ceiling 16** (MVP: 1) | Hard cap; §3 "Knife Split Chance" multiplies *within* the cap |
| Draw calls | ≤ 60 in a run | Sprites batched through per-category atlases sharing one material |
| Steady-state GC | **zero** allocated bytes per frame during a run | No `new`, no LINQ, no boxing in update paths; knives and ingredients pooled (§2 already pools the knife) |
| Input → launch latency | ≤ 1 render frame + 1 physics step | Drag-and-release must feel instantaneous |

The GC rule, the knife cap and the ingredient ceiling are the three that get quietly broken —
which is why they are stated here rather than discovered in profiling after §3 lands.

---

## 9. Workstreams

| # | Workstream | Deliverable | Acceptance |
| --- | --- | --- | --- |
| **F1** | Repository preflight | Template-import changes committed as their own commit, so every later diff is §1 work only | `git status --short` is empty |
| **F2** | Platform & player configuration | Orientation locked to landscape; bundle identifiers; IL2CPP + ARM64; Linear colour space; incremental GC; bundle version `0.1.0` | `defaultScreenOrientation` serialises as landscape, not `4`; switching the active target to Android compiles without backend/architecture warnings |
| **F3** | Aspect policy | `ViewportMath` (pure) + EditMode tests covering the §6.3 table | The 19.2 × 10.8 box is un-cropped at 4:3, 16:10, 16:9, 19.5:9, 21:9 and pillarboxed at 32:9; `orthoSize` is exactly 5.400 at 16:9 |
| **F4** | Camera adapter | `BoardViewportAdapter` on `Game.unity`'s camera, replacing §2's hardcoded `orthographicSize = 5.4` | The Scene view updates live while dragging the Game view's aspect dropdown; resizing a Windows build window re-frames without cropping |
| **F5** | Safe-area HUD | `SafeAreaFitter` on the HUD root | HUD content sits inside `Screen.safeArea` on all four edges; verified against a notched device profile in the Device Simulator |
| **F6** | Cross-platform parity verification | The §7 procedure, executed once §2 Phase 1 lands | Identical `power` and `direction` from mouse and from simulated touch for the same world-unit drag |
| **F7** | Platform builds | Windows x64 and Android ARM64 players that boot `Game.unity` | Windows build boots; Android APK installs, boots, and **cannot be rotated to portrait** |

---

## 10. Open decisions

| # | Question | Status |
| --- | --- | --- |
| O1 | Aiming feel: slingshot vs. swipe-through | **Resolved by §2 D4** — pull-back slingshot. §1 agrees; it keeps the finger off the target on mobile, which matters more here than on desktop. |
| O2 | Is macOS a PC shipping target? | **Open.** Recommendation: Windows-only for now. macOS costs a signing/notarisation pipeline for no §1 benefit. Decide before the first public build. |
| O3 | Frame-rate policy: lock 60, or unlock to 120? | **Open.** Recommendation: unlock with a 60 floor. The physics step stays `0.02` regardless, so a 120 Hz device gets smoother presentation, not a different simulation. Decide in §2. |
| O4 | UI stack: uGUI or UI Toolkit? | **Resolved by §2 §6.5** — uGUI (world-space patience bars, prefab-able tickets). §1 adopts it; the CanvasScaler contract in §6.4 is stack-neutral either way. |
| O5 | When do Addressables come in? | **Open.** §6, when restaurant themes multiply content. Not in §1 — premature. |

## 11. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Aspect sprawl (4:3 tablets → 21:9 phones) | Board cropped, or HUD under a notch | The §6.3 contain-fit + clamp policy and §6.4 `SafeAreaFitter`, verified against the Device Simulator (already installed) before §1 sign-off |
| §1 and §2 drift back apart | Two documents describing two different projects | The reconciliation is recorded as D6/D7 in the README, and §1's §4 table states the only three things §1 adds to §2 Phase 0 |
| Layer 6/7 habit from §2's draft | Layers sitting in Unity's builtin reserved range | Corrected to 8–11 as D7, in both documents |
| `orthographicSize` set in two places | The adapter and a hand-set camera value fighting | The adapter is the only writer; §2 §4.4's hardcoded `5.4` becomes a fallback default, not a source of truth |
| Parity check blocked on §2 Phase 1 | §1's headline claim goes unverified | Stated openly in §7 rather than implied — the check runs the moment `AimInput` exists |

---

## 12. Definition of done for Section 1

1. Windows x64 and Android ARM64 builds both launch and reach `Game.unity`.
2. Orientation is landscape-only; no portrait frame is renderable on mobile.
3. The 19.2 × 10.8 board box is fully visible, un-cropped, and non-advantageous at
   4:3 / 16:10 / 16:9 / 19.5:9 / 21:9, and pillarboxed at 32:9.
4. `orthographicSize` is 5.400 at 16:9 — identical to §2's reference frame.
5. HUD content clears the safe area on a notched landscape device profile.
6. EditMode tests for `ViewportMath` are green.
7. A drag-and-release gesture yields identical `power` and `direction` from mouse and touch
   (executed once §2 Phase 1 exists — see §7).
8. Zero gameplay logic is present: no ingredients, no knife physics, no orders, no economy.

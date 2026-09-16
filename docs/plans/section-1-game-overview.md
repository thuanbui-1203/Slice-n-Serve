# Section 1 — Game Overview: Foundation Plan

**Concept source:** [`../concept.md`](../concept.md), §1 — *Story, Genre, Engine, Platforms, Vibe*.
**Runbook:** [`section-1-implementation-steps.md`](./section-1-implementation-steps.md).
**Sibling:** [`section-2-core-mechanics.md`](./section-2-core-mechanics.md) — owns the board, the knife, orders, and **Phase 0 Foundations**.
**Decisions:** [`README.md`](./README.md) is the single decision log. This section owns **D1, D2, D7, D8, D9**.

---

## 1. What Section 1 owns — and what it does not

§1 of the concept document is *Story, Genre, Engine, Platforms, Vibe*. Those are not mechanics, so
Section 1 cannot deliver gameplay. It delivers the **cross-platform frame** everything else is built
inside.

The split with Section 2 is explicit, because both sections initially claimed the same ground:

| Area | Owner | Why |
| --- | --- | --- |
| Folder tree, assemblies, namespaces, layer scheme, `Physics2D` settings, materials, `Game.unity`, the `Board` action map, `BoardConfig` | **§2, Phase 0** | §2's choices are grounded in measurements taken from this project (the 0.02 s timestep, the tunneling budget at 40 u/s), and its tuning constants are calibrated to them |
| Platform targets, orientation lock, aspect policy, safe-area handling, performance budgets, cross-platform parity verification | **§1, this plan** | §2 never addressed portrait phones, 4:3 tablets, notches or non-Windows builds — and its camera assumed a fixed 16:9 frame |
| Board behaviour, knife, ingredients, orders, patience | **§2, Phases 1–5** | mechanics |

**Section 1 is done when** the project is configured to ship on Windows and Android, the app cannot
render a portrait frame, the 19.2 × 10.8 board is fully visible and un-cropped on every supported
aspect from 4:3 to 21:9, the HUD clears a landscape notch, and the performance budgets below are
stated as enforceable numbers. **Zero gameplay logic is delivered here.**

Explicitly not in Section 1: ingredients, knife physics, orders, customers, economy, upgrades,
difficulty scaling, PvP.

---

## 2. Concept → engineering translation

The §1 "vibe" sentence — *Overcooked meets Peggle/Pong, layered with a satisfying idle-game
progression loop* — is a constraint set, not decoration. Each clause is a requirement to bake in now,
because retrofitting it is expensive.

| Concept clause | Engineering consequence | Where |
| --- | --- | --- |
| "Overcooked" — fast, split-attention, legible | 60 FPS floor on mid-tier mobile; HUD legible in the screen corners; state never hidden by a notch or rounded corner | §8 budgets, §6.4 safe area |
| "Peggle/Pong" — bouncing knife, angle-driven | Deterministic 2D physics at a fixed step; collision-driven, not animation-driven; a hard cap on simultaneous knives | §2 §4.6, §8 budgets |
| "satisfying idle-game progression" | Every balance number lives in a data asset, never in code, so §3/§4 can retune without a rebuild | §2 §3.1, §2 `BoardConfig` |
| "PC **and** Mobile, cross-platform" | One input path, one aspect policy, two build targets, no platform-specific gameplay code | §5, §7 |
| "drag-and-release … for Mobile (touch) and PC (mouse)" | Mouse, `Touchscreen` and `Pen` share one code path; power measured in world units, not pixels, so the feel is DPI-independent | §7 |

---

## 3. Decisions this section owns

Full log in [`README.md`](./README.md#locked-decisions). These are the ones §1 is accountable for.

| # | Decision |
| --- | --- |
| **D1** | Unity `6000.3.16f1`, URP `17.3.0` / `Renderer2D`. The version is pinned; upgrades are scheduled, not incidental. |
| **D2** | **Landscape, PC-first.** Portrait and portrait-upside-down are disabled at the OS level. |
| **D7** | Layer indices `Knife` = 8, `Ingredient` = 9, `BoardWall` = 10, `Obstacle` = 11 — §2's names at corrected indices, since 0–5 are Unity's builtin slots. |
| **D8** | The camera **contains-fit** §2's 19.2 × 10.8 box, clamped to the `[4:3 … 21:9]` envelope, letterboxed outside it. |
| **D9** | §2's `Board` action map is the single aiming path on both platforms. Mouse and touch must produce **identical** launch values for identical world-unit drags. |

### 3.1 Trade-offs, recorded honestly

**Landscape, PC-first (D2).** A landscape board makes the ping-pong geometry and the
Overcooked-style counter/HUD rails straightforward, and matches the Peggle reference. It costs the
mobile one-handed portrait posture. If the project ever pivots to mobile-first, the aspect policy in
§6 survives but the board layout and every HUD layout must be re-authored.

**One runtime assembly instead of five (D3, §2's).** Five assemblies would have made *"gameplay must
not reference UI"* a compiler error. One assembly makes it a convention. It is mitigated by §2's own
design — the order and patience systems are plain C# with no `MonoBehaviour` and no UI reference,
which is what actually makes them testable, and what a second assembly would have been protecting.
**Revisit if a UI dependency ever appears inside `SliceNServe.Core`, `.Board`, `.Orders` or `.Data`.**

**50 Hz physics with a 60 FPS floor (D5, §2's).** Physics steps at 50 Hz while rendering targets
60 FPS; `RigidbodyInterpolation2D.Interpolate` on the knife is what keeps that smooth.

---

## 4. What §1 adds on top of §2 Phase 0

§2 Phase 0 produces the project skeleton. §1 adds exactly three things to it and changes nothing else.

| §1 addition | Goes in | Why |
| --- | --- | --- |
| `ViewportMath` + `BoardViewportAdapter` | `Scripts/Runtime/Core/ViewportMath.cs`, `Scripts/Runtime/Board/BoardViewportAdapter.cs` | §2 §4.4 hardcodes `orthographicSize = 5.4`, which is right at 16:9 and wrong at every other aspect. The adapter computes it instead — and evaluates to **exactly 5.4 at 16:9**, so none of §2's world-unit tuning changes. |
| `SafeAreaFitter` | `Scripts/Runtime/UI/SafeAreaFitter.cs` | §2 has no safe-area story, and a landscape notch sits on the left or right edge where its HUD lives. |
| Player/platform settings | `ProjectSettings/` | Orientation lock, bundle identifiers, IL2CPP/ARM64, colour space, incremental GC. §2 explicitly leaves these alone. |

`Scripts/Runtime/UI/` is a new folder under §2's runtime layout, namespace `SliceNServe.UI`, for
cross-cutting view plumbing. §2's own UI (`OrderTicketView`, `PatienceBar`) lives there too.

---

## 5. Platform & build-target matrix

| Target | Class | Scripting | Arch | Min OS | Built in §1? |
| --- | --- | --- | --- | --- | --- |
| **Windows x64** | primary dev + PC | IL2CPP (Mono fine for dev) | x86_64 | Windows 10 | yes — reference build |
| **Android** | primary mobile | IL2CPP | **ARM64 only** | API 25 (Android 7.1) | yes — must build and boot |
| **iOS** | secondary mobile | IL2CPP | ARM64 | iOS 13.0 | configuration only, no build |
| macOS | optional | — | — | — | **no** — see O2 |
| WebGL | not planned | — | — | — | no |

- `AndroidTargetArchitectures: 2` is already ARM64-only, satisfying Google Play's 64-bit rule. Do **not** add ARMv7 "just in case" — it doubles build time for no supported device.
- API 25 is the current minimum. Raising it is a shipping-time decision.
- iOS is configured but not built (no macOS host). Treat it as *"must not be actively broken"* — no Windows-only packages, no platform-gated code without an `#if` — not as verified.
- `companyName` is currently `DefaultCompany`, a release-blocking placeholder. Bundle identifiers are empty.

---

## 6. Display, orientation & aspect policy

### 6.1 Orientation

- `Default Orientation` → **Landscape Left**.
- Mobile: **Auto Rotation with only the two landscape orientations enabled.** Portrait and Portrait Upside Down are off.
- The project currently ships `defaultScreenOrientation: 4` (all four allowed). **Locking this down is the highest-value change §1 makes**: a portrait frame around a 19.2 × 10.8 landscape board is a broken build, not a layout bug to patch later.
- Keep *both* landscape orientations rather than hard-locking `Landscape Left` — a player holding the phone with the charging port on the right would otherwise see the game upside down.

### 6.2 The board box

`BoardConfig.worldBounds = Rect(-9.6, -5.4, 19.2, 10.8)` — §2's value, unchanged. Half-width `9.6`,
half-height `5.4`. Everything §2 authors in world units (launcher at `(0, -4.4)`, the ingredient
zone, the 8 → 22 u/s envelope) is untouched by this plan.

### 6.3 Aspect policy

One rule, in `ViewportMath`:

```
effectiveAspect = clamp(screenAspect, 4/3, 21/9)
orthoSize       = max(boardHalfHeight, boardHalfWidth / effectiveAspect)
```

**Contain-fit:** the whole box is always visible, at every aspect, never cropped. Leftover space
becomes backdrop and HUD rails.

| Screen aspect | `orthoSize` | Visible world area | Leftover space |
| --- | --- | --- | --- |
| 4:3 (1.333) | 7.200 | 19.2 × 14.4 | 1.8 u above and below |
| 3:2 (1.500) | 6.400 | 19.2 × 12.8 | 1.0 u above and below |
| 16:10 (1.600) | 6.000 | 19.2 × 12.0 | 0.6 u above and below |
| **16:9 (1.778)** | **5.400** | **19.2 × 10.8** | **none — exactly §2's reference frame** |
| 19.5:9 (2.167) | 5.400 | 23.4 × 10.8 | 2.1 u left and right |
| 20:9 (2.222) | 5.400 | 24.0 × 10.8 | 2.4 u left and right |
| 21:9 (2.333) | 5.400 | 25.2 × 10.8 | 3.0 u left and right |
| 32:9 (3.556) | 5.400 | 21:9 **pillarboxed** | black bars left and right |

The `5.400` row is the important one: the policy reproduces §2's hardcoded camera exactly at its
reference aspect, so adopting it costs §2 nothing.

Outside `[4:3 … 21:9]` the viewport is clamped and `Camera.rect` is set to the largest centred
rectangle of the clamped aspect. That exists for one reason: **an ultrawide player must not see more
board than a 16:9 player and gain an aiming advantage** (**D9**). Inside the envelope the extra space
is decorative.

Because the board is authored in world units and the policy only ever *widens* the frame, **§2's
tuning is aspect-independent** — speeds, radii and spawn geometry behave identically on a 4:3 tablet
and a 20:9 phone. Only the on-screen size changes.

**Not used:** Unity's Pixel Perfect Camera. It quantises the orthographic size and fights a
contain-fit policy. Whether the art is pixel-perfect is a later art decision; revisit then.

### 6.4 HUD & safe area

- Canvas: Screen Space – Overlay, `CanvasScaler` = Scale With Screen Size, reference **1920 × 1080**, match **0.5**.
- The HUD root is inset by `Screen.safeArea` on **all four edges**, via one `SafeAreaFitter`. In landscape the notch is on the *left or right* edge, so the horizontal insets are the ones that bite.
- Android has `androidRenderOutsideSafeArea: 1`. **Keep it:** the board and backdrop should fill the physical display; only the UI respects the safe area, and safe-area handling lives in one component rather than in individual layouts.
- One-time setup: `Window → TextMeshPro → Import TMP Essential Resources` before the first ticket renders text.

---

## 7. Cross-platform input parity

§2 owns the input implementation (§2 §5.2): the `Board` action map with `Aim` and `Launch`, the
`AimInput` state machine, and the drag-to-velocity mapping (`power = Clamp01(|drag| / 3.5)`,
dead-zone `0.35 u`, `launchDir = -drag.normalized`, `speed = Lerp(8, 22, powerEase(power))`).

**Section 1 owns the requirement that those numbers are platform-independent, and the evidence that
they are.** That is not automatic — it is a property someone has to protect:

1. **One binding set, not two.** `Aim` binds `<Pointer>/position`, `<Touchscreen>/primaryTouch/position` and `<Pen>/position`. All three derive from the `Pointer` device layout, so one `InputAction` serves mouse, touch and pen and `AimInput` needs no `#if UNITY_IOS` branch.
2. **Power is measured in world units, not pixels.** `maxDragLength` is a world-unit `BoardConfig` field. A pixel-normalised drag would make a phone thumb and a desktop mouse produce different power for the same intent — exactly the bug §5 PvP cannot tolerate later.
3. **No gameplay value may branch on device.** Binding only `primaryTouch` is an input-shape guard, not a gameplay one. Nothing downstream of `AimInput` may read "is this a touch".
4. **Verification (runbook Step 5):** one drag of the same world-unit length, once with the mouse and once through the Device Simulator's touch path, must report identical `power` and `direction`.

Because `AimInput` arrives in §2 Phase 1, this check depends on Phase 1 landing. §1 defines the
requirement and the procedure; it cannot execute it alone.

---

## 8. Performance & non-functional budgets

Derived from the "fast-paced, ping-pong" vibe. These are §1 commitments — later sections fit inside
them, they do not renegotiate them.

| Budget | Target | Why |
| --- | --- | --- |
| Frame rate floor | 60 FPS on a mid-tier Android (Snapdragon 6-series class) | Bounce readability collapses below 60 |
| Physics step | Fixed `0.02` (50 Hz), max allowed `0.3333` | §2's decision; the knife uses `RigidbodyInterpolation2D.Interpolate` to stay smooth against a 60 FPS display |
| Speed guard | `speedHardCap` 40 u/s, knife collider radius 0.18 u, walls 2.0 u thick | §2 §4.6 — the anti-tunneling budget |
| Live ingredients | §2's gameplay target is 12, working max 18. **Ceiling 120 sprites** | 12/18 is the design target; 120 is where the frame budget is at risk, so §3's spawn-rate upgrades scale *between* them, never past them |
| Simultaneous knives | **Ceiling 16** (MVP: 1) | Hard cap; §3's "Knife Split Chance" multiplies *within* the cap |
| Draw calls | ≤ 60 in a run | Sprites batched through per-category atlases sharing one material |
| Steady-state GC | **zero** allocated bytes per frame during a run | No `new`, no LINQ, no boxing in update paths; knives and ingredients pooled |
| Input → launch latency | ≤ 1 render frame + 1 physics step | Drag-and-release must feel instantaneous |

The GC rule, the knife cap and the ingredient ceiling are the three that get quietly broken — which
is why they are stated here rather than discovered in profiling after §3 lands.

---

## 9. Workstreams

| # | Workstream | Deliverable | Acceptance |
| --- | --- | --- | --- |
| **F1** | Repository preflight | **Done.** The template import was committed as its own commit (`create GameManager`), so later diffs contain only real work | `git status --short` is empty |
| **F2** | Platform & player configuration | Orientation locked to landscape; bundle identifiers; IL2CPP + ARM64; Linear colour space; incremental GC; bundle version `0.1.0` | `defaultScreenOrientation` serialises as landscape, not `4`; switching the active target to Android compiles with no backend or architecture warnings |
| **F3** | Aspect policy | `ViewportMath` (pure) + EditMode tests covering the §6.3 table | The box is un-cropped at 4:3, 16:10, 16:9, 19.5:9 and 21:9, and pillarboxed at 32:9; `orthoSize` is exactly 5.400 at 16:9 |
| **F4** | Camera adapter | `BoardViewportAdapter` on `Game.unity`'s camera, replacing §2's hardcoded `orthographicSize = 5.4` | The Scene view updates live while dragging the Game view's aspect dropdown; resizing a Windows build window re-frames without cropping |
| **F5** | Safe-area HUD | `SafeAreaFitter` on the HUD root | HUD content sits inside `Screen.safeArea` on all four edges, verified against a notched device profile |
| **F6** | Cross-platform parity verification | The §7 procedure, executed once §2 Phase 1 lands | Identical `power` and `direction` from mouse and from simulated touch for the same world-unit drag |
| **F7** | Platform builds | Windows x64 and Android ARM64 players that boot `Game.unity` | Windows build boots; the APK installs, boots, and **cannot be rotated to portrait** |

---

## 10. Open decisions

| # | Question | Status |
| --- | --- | --- |
| O1 | Aiming feel: slingshot vs. swipe-through | **Resolved — D12.** §1 agrees: it keeps the finger off the target on mobile. |
| O2 | Is macOS a PC shipping target? | **Open.** Recommendation: Windows-only. macOS costs a signing/notarisation pipeline for no benefit here. Decide before the first public build. |
| O3 | Frame-rate policy: lock 60, or unlock to 120? | **Open.** Recommendation: unlock with a 60 floor. The physics step stays `0.02`, so a 120 Hz device gets smoother presentation, not a different simulation. |
| O4 | UI stack: uGUI or UI Toolkit? | **Resolved — D15.** §1 adopts it; the §6.4 CanvasScaler contract is stack-neutral either way. |
| O5 | When do Addressables come in? | **Open.** §6, when restaurant themes multiply content. Premature now. |

---

## 11. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Aspect sprawl (4:3 tablets → 21:9 phones) | Board cropped, or HUD under a notch | The §6.3 contain-fit + clamp policy and §6.4 `SafeAreaFitter`, verified against the Device Simulator (already installed) before §1 sign-off |
| §1 and §2 drift back apart | Two documents describing two different projects | The split is recorded above and in the README; §4 states the only three things §1 adds to §2 Phase 0 |
| Layer indices sitting in Unity's builtin range | Physics silently ignores the scheme | Corrected to 8–11 as **D7** |
| `orthographicSize` written in two places | The adapter and a hand-set camera value fighting | The adapter is the only writer; §2 §4.4's `5.4` is the reference value it reproduces, not a competing source of truth |
| Parity check blocked on §2 Phase 1 | §1's headline claim goes unverified | Stated openly in §7 rather than implied — the check runs the moment `AimInput` exists |

---

## 12. Definition of done

1. Windows x64 and Android ARM64 builds both launch and reach `Game.unity`.
2. Orientation is landscape-only; no portrait frame is renderable on mobile.
3. The 19.2 × 10.8 board box is fully visible, un-cropped and non-advantageous at 4:3 / 16:10 / 16:9 / 19.5:9 / 21:9, and pillarboxed at 32:9.
4. `orthographicSize` is 5.400 at 16:9 — identical to §2's reference frame.
5. HUD content clears the safe area on a notched landscape device profile.
6. `ViewportMathTests` are green.
7. A drag-and-release gesture yields identical `power` and `direction` from mouse and touch (executed once §2 Phase 1 exists — see §7).
8. Zero gameplay logic is present: no ingredients, no knife physics, no orders, no economy.

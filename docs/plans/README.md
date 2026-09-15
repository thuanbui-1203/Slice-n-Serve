# Slice & Serve — Planning Sessions

Engineering plans derived from the game concept document. The concept doc is the source of truth
for *what* the game is; these plans are the source of truth for *how* it gets built. One planning
session per concept-document section.

---

## Section 2 conflict — RESOLVED

Two Section 2 plans were written in parallel and assumed two incompatible Section 1 foundations.
That was flagged as **frozen** while a parallel session was still writing. **It is now resolved**,
in favour of option 2 (the `section-2-core-mechanics.md` direction): the Section 2 planning session
re-ran against `section-1-*.md`, and the `02-*.md` drafts were retired.

| Document | Role now |
| --- | --- |
| [`section-2-core-mechanics.md`](./section-2-core-mechanics.md) | **The Section 2 plan.** It owns Phase 0 of the project. |
| [`section-2-implementation-steps.md`](./section-2-implementation-steps.md) | **The Section 2 runbook.** Ordered steps with a verification after each. |
| [`section-1-game-overview.md`](./section-1-game-overview.md), [`section-1-implementation-steps.md`](./section-1-implementation-steps.md) | The Section 1 plan and runbook, consistent with the Section 2 documents above. |
| `01-game-overview.md`, `01-implementation-steps.md` | Signposts to the reconciled Section 1 documents. |
| `02-core-mechanics.md`, `02-core-mechanics-steps.md` | **Retired.** Signposts only; their content is superseded and must not be followed. |

The one contribution of the retired `02-*.md` drafts — the belief that `Knife ↔ Ingredient` had to
be amended from ON to OFF — is recorded below as **D6**, i.e. as Section 2's *own* decision rather
than an amendment to Section 1. There is no outstanding amendment, and every D-row points the same
way. **This folder is implementable.**

---

| Session | Concept section | Files |
| --- | --- | --- |
| **01** | §1 Game Overview | [`section-1-game-overview.md`](./section-1-game-overview.md) (plan) · [`section-1-implementation-steps.md`](./section-1-implementation-steps.md) (runbook) |
| **02** | §2 Core Mechanics (2.1 board, 2.2 orders & patience) | [`section-2-core-mechanics.md`](./section-2-core-mechanics.md) (plan) · [`section-2-implementation-steps.md`](./section-2-implementation-steps.md) (runbook) |
| 03 | §3 Economy & Upgrades | — not yet scheduled — |
| 04 | §4 Difficulty Scaling | — not yet scheduled — |
| 05 | §5 Multiplayer (PvP) | — not yet scheduled — |
| 06 | §6 Future Updates Roadmap | — not yet scheduled — |

**Conventions**
- A plan is named `section-N-<slug>.md`; where a section needs an executable procedure as well it
  gets a `section-N-implementation-steps.md` runbook: ordered, exact, with a verification after
  each step, meant to be followed by a human in the Unity Editor rather than applied automatically.
- Naming, indices and tuning values are owned by whichever section *first decided* them. Later
  sections adopt them; they do not invent parallel versions.
- `01-*.md` and `02-*.md` are **signposts only**. Each links to its replacement and should not be
  followed directly.

---

## Baseline (recorded from the repository)

| Item | Value |
| --- | --- |
| Unity Editor | `6000.3.16f1` (`a56f230f6470`) |
| Render pipeline | URP `17.3.0`, `Renderer2D`, `Assets/Settings/UniversalRP.asset` |
| Input | `com.unity.inputsystem` `1.19.0`; `activeInputHandler: 1` (Input System only) |
| UI / tests | `com.unity.ugui` `2.0.0`; `com.unity.test-framework` `1.6.0` |
| Android | min API `25`, `AndroidTargetArchitectures: 2` (ARM64 only) |
| iOS | `iPhoneSdkVersion: 988` (template default — replace with 13.0) |
| Default orientation | `4` = Auto Rotation, all four orientations — **Section 1 locks this to landscape** |
| Time | `Fixed Timestep: 0.02` (50 Hz) — kept deliberately by Section 2 |
| Layers | indices 0–7 are Unity's builtin slots; **user layers start at 8** |
| Scenes | `Assets/Scenes/SampleScene.unity` (URP 2D template) |
| Scripts | none yet |

## Locked decisions

Binding for later sessions. Changing one means amending this log and checking every session that
depends on it.

| # | Decision | Session |
| --- | --- | --- |
| **D1** | Unity `6000.3.16f1` + URP 2D Renderer. The editor version is pinned; an upgrade is a scheduled task with a regression pass, never incidental. | 01 |
| **D2** | **Landscape, PC-first.** Portrait and portrait-upside-down are disabled at the OS level. Lead platform is Windows; Android is the primary mobile target; iOS is configured but not built. | 01 |
| **D3** | **One runtime assembly.** `SliceNServe.Runtime` + two test assemblies, namespaces `SliceNServe.{Core,Board,Orders,Data}`. *Consequence:* "gameplay must not reference UI" is a convention, not a compiler error — see the trade-off note in the §1 plan. | 02 |
| **D4** | **Board box is 19.2 × 10.8 world units**, `worldBounds = Rect(-9.6, -5.4, 19.2, 10.8)`, authored in world units and therefore aspect-independent. | 02 |
| **D5** | **Physics stays at `Fixed Timestep = 0.02` (50 Hz)** with `RigidbodyInterpolation2D.Interpolate` on the knife. Rendering targets a 60 FPS floor. | 02 |
| **D6** | **Ingredients are non-solid** (trigger colliders) and are collected by an explicit `Rigidbody2D.Cast` sweep in the knife's `FixedUpdate`, not by trigger callbacks. `Knife ↔ Ingredient` is **off** in the collision matrix. | 02 |
| **D7** | **Indices for the layer scheme:** `Knife` = 8, `Ingredient` = 9, `BoardWall` = 10, `Obstacle` = 11. | 01 |
| **D8** | **Camera framing is contain-fit**, not fixed 16:9: `orthoSize = max(halfHeight, halfWidth / clamp(aspect, 4/3, 21/9))`, letterboxed outside that envelope. This evaluates to exactly `5.4` at 16:9, so it reproduces Section 2's reference camera and leaves its tuning untouched. | 01 |
| **D9** | **One aiming gesture, identical on mouse and touch**, driven by the single `Board` action map (`Aim`, `Launch`). Power is measured in world units, so a desktop drag and a phone thumb produce the same launch for the same intent. | 01 |

### D7 — why the layer indices moved

Section 2's draft placed `Knife`/`Ingredient`/`BoardWall`/`Obstacle` at indices 6–9. This
project's `ProjectSettings/TagManager.asset` confirms indices **0–7 are Unity's builtin reserved
slots** (`Default`, `TransparentFX`, `Ignore Raycast`, `Water`, `UI` plus three unnamed). User
layers conventionally start at 8, so the scheme shifts to **8–11**. The layer *names* are
Section 2's and are unchanged.

## Reconciliation notes

Section 1 and Section 2 were planned independently and initially disagreed on roughly a dozen
concrete points — folder layout, assembly count, namespaces, layer indices, the fixed timestep,
the knife↔ingredient collision rule, board size, scene name, action-map name and config type
name. The resolution, recorded as D3–D9:

- **Section 2's choices win for names and numbers**, because they are grounded in measurements
  taken from this project (the 0.02 s timestep, the tunneling budget at 40 u/s) and its tuning
  constants are already calibrated to them.
- **Section 1 keeps only what Section 2 never decided:** platform targets, the orientation lock,
  the aspect policy, safe-area handling, performance budgets, and cross-platform parity
  verification. Section 2 never addressed portrait phones, 4:3 tablets, notches or non-Windows
  builds, and its camera assumed a fixed 16:9 frame.
- **One correction is applied to Section 2:** the layer indices (D7).

Section 1 states exactly what it adds on top of Section 2's Phase 0 — three things — in §4 of its
plan.

## Open decisions

| # | Question | Recommendation | Decide by |
| --- | --- | --- | --- |
| O1 | Aiming feel: slingshot vs. swipe-through | **Resolved (D9 / §2 D4):** pull-back slingshot. | — |
| O2 | Is macOS a shipping target, or Windows-only for PC? | Windows-only for now; macOS costs a signing/notarisation pipeline for no current benefit. | before the first public build |
| O3 | Frame-rate policy: lock 60, or unlock to 120? | Unlock with a 60 floor. The physics step stays `0.02` regardless, so a 120 Hz device gets smoother presentation, not a different simulation. | Session 02 |
| O4 | UI stack: uGUI or UI Toolkit? | **Resolved (§2 §6.5):** uGUI — world-space patience bars and prefab-able tickets. | — |
| O5 | When do Addressables come in? | Session 06, when restaurant themes multiply content. Premature before then. | Session 06 |

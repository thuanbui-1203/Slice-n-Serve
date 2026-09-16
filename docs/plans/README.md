# Slice & Serve — Planning Index

The plans in this folder are the source of truth for **how** the game gets built.
[`../concept.md`](../concept.md) is the source of truth for **what** it is.

**Nothing here has been applied to the project yet.** Both runbooks are written to be followed by
hand in the Unity Editor; they are not applied automatically.

---

## Sessions

| Session | Concept section | Plan | Runbook |
| --- | --- | --- | --- |
| **01** | §1 Game Overview | [`section-1-game-overview.md`](./section-1-game-overview.md) | [`section-1-implementation-steps.md`](./section-1-implementation-steps.md) |
| **02** | §2 Core Mechanics (2.1 board, 2.2 orders & patience) | [`section-2-core-mechanics.md`](./section-2-core-mechanics.md) | [`section-2-implementation-steps.md`](./section-2-implementation-steps.md) |
| 03 | §3 Economy & Upgrades | — not scheduled — | — |
| 04 | §4 Difficulty Scaling | — not scheduled — | — |
| 05 | §5 Multiplayer (PvP) | — not scheduled — | — |
| 06 | §6 Future Updates Roadmap | — not scheduled — | — |

**Conventions**

- A plan is `section-N-<slug>.md`; a runbook is `section-N-implementation-steps.md`. Runbooks are
  ordered, exact, and carry a verification after each step.
- **A plan states intent; a runbook states actions.** Where they disagree, the plan wins and the
  runbook is wrong.
- Ownership: a name, index or tuning value belongs to whichever section *first decided* it. Later
  sections adopt it rather than inventing a parallel version.
- `README.md` (this file) is the **single decision log**. Plans reference decisions as `D<n>` from
  here; they do not keep private numbering of their own.
- Historical drafts are deleted, not signposted. `git log` is the archive.

---

## Baseline — verified from the repository

Recorded by inspecting the project, not assumed. Re-verify before relying on it.

| Item | Value |
| --- | --- |
| Unity Editor | `6000.3.16f1` (`a56f230f6470`) |
| Render pipeline | URP `17.3.0`, 2D Renderer (`Assets/Settings/Renderer2D.asset`) |
| Packages | 53 dependencies; notably `com.unity.inputsystem` `1.19.0`, `com.unity.ugui` `2.0.0`, `com.unity.test-framework` `1.6.0`, `com.unity.2d.*`, `com.unity.device-simulator.devices` `1.0.1`, `com.unity.collab-proxy` `2.12.4` |
| Input | `activeInputHandler: 1` — **Input System package only**; legacy `Input.*` does not work |
| Input asset | `Assets/InputSystem_Actions.inputactions` — template maps `Player` + `UI` only |
| Colour space | Linear (`m_ActiveColorSpace: 1`) |
| Time | `Fixed Timestep: 0.02` (50 Hz), `Maximum Allowed Timestep: 0.33333334` |
| `Physics2D` | gravity `(0, -9.81)`, `maxTranslationSpeed: 100`, `velocityThreshold: 1`, `simulationMode: 0` (FixedUpdate), iterations `8`/`3`, `queriesStartInColliders: 1`, no default material |
| Layers | **no custom layers**; indices 0–5 are `Default`/`TransparentFX`/`Ignore Raycast`/`Water`/`UI`, 6–31 unnamed |
| Orientation | `defaultScreenOrientation: 4` — Auto Rotation, all four. **Not yet locked to landscape.** |
| Identity | `companyName: DefaultCompany` (**placeholder**), `productName: Slice & Serve` |
| Android | `AndroidMinSdkVersion: 25`, `AndroidTargetSdkVersion: 0` (automatic), `AndroidTargetArchitectures: 2` (ARM64 only) |
| iOS | `iPhoneSdkVersion: 988` (template default — replace with `13.0`) |
| Scenes | `Assets/Scenes/SampleScene.unity` (URP 2D template, 393 lines). Build list contains only that. |
| Scripts | `Assets/Scripts/GameManager.cs` — a 16-line **empty auto-generated stub**, no namespace, outside the planned `Scripts/Runtime/` layout and with no `.asmdef` |
| Assets | `Assets/Settings/{UniversalRP,Renderer2D,UniversalRenderPipelineGlobalSettings,DefaultVolumeProfile}.asset`, `Assets/Settings/Scenes/URP2DSceneTemplate.unity`, `Assets/Settings/Lit2DSceneTemplate.scenetemplate` |
| Git | 2 commits (`Initial check-in`, `create GameManager`); clean tree; remotes `origin` (GitHub) and `gitlab` |
| `.gitattributes` | defines an `lfs` attribute macro; **no path currently uses it** |

> The template has been imported but otherwise untouched: no `.asmdef`, no `Assets/Scripts/Runtime/`,
> no `BoardConfig` asset, no `Game.unity`, no `Gameplay`/`Board` action map. Every runbook step is
> still outstanding.

---

## Locked decisions

Binding. Changing one means amending this table **and** checking every document that cites it.

| # | Decision | Section |
| --- | --- | --- |
| **D1** | Unity `6000.3.16f1` + URP 2D Renderer. The version is pinned; an upgrade is a scheduled task with a regression pass, never incidental. | 01 |
| **D2** | **Landscape, PC-first.** Portrait and portrait-upside-down are disabled at the OS level. Windows x64 is the lead platform, Android ARM64 the primary mobile target, iOS is configured but not built (no macOS host). | 01 |
| **D3** | **One runtime assembly.** `SliceNServe.Runtime` plus two test assemblies; namespaces `SliceNServe.{Core,Board,Orders,Data}`. *Consequence:* "gameplay must not reference UI" is a convention, not a compiler error — see the trade-off note in the §1 plan. | 02 |
| **D4** | **Board box is 19.2 × 10.8 world units**, `worldBounds = Rect(-9.6, -5.4, 19.2, 10.8)`, authored in world units and therefore aspect-independent. | 02 |
| **D5** | **Physics steps at `Fixed Timestep = 0.02` (50 Hz)** with `RigidbodyInterpolation2D.Interpolate` on the knife. Rendering targets a 60 FPS floor. | 02 |
| **D6** | **Ingredients are non-solid pickups** (trigger colliders, no `Rigidbody2D`) collected by an explicit `Rigidbody2D.Cast` sweep in the knife's `FixedUpdate`, not by trigger callbacks. `Knife ↔ Ingredient` is **off** in the collision matrix. | 02 |
| **D7** | **Layer scheme:** `Knife` = 8, `Ingredient` = 9, `BoardWall` = 10, `Obstacle` = 11. Indices 0–5 are Unity's builtin slots, so user layers start at 6 at the earliest; 8–11 leaves room and matches convention. | 01 |
| **D8** | **Camera framing is contain-fit**, not fixed 16:9: `orthoSize = max(halfHeight, halfWidth / clamp(aspect, 4/3, 21/9))`, letterboxed outside that envelope. It evaluates to exactly `5.4` at 16:9, reproducing the §2 reference camera, so adopting it costs §2 nothing. | 01 |
| **D9** | **One aiming gesture, identical on mouse and touch**, driven by the single `Board` action map (`Aim`, `Launch`). An ultrawide player must never see more board than a 16:9 player. | 01 |
| **D10** | **Board gravity is zero** — `Physics2D.gravity = (0, 0)`. The concept doc says both "ping-pong" and "pinball"; zero-g is chosen because aiming must be *predictable*, which the drag-and-release scheme depends on. | 02 |
| **D11** | **Ingredient overflow is discarded** with a wasted-ingredient cue. Recipe matching is an imperative: the draw reads *"automatically contribute to completing their dishes"*, so there is no player choice about which order an ingredient feeds. *(Alternative: a pantry buffer. Revisit in §3.)* | 02 |
| **D12** | **Aiming is a pull-back slingshot** — launch direction is opposite the drag, so the finger never covers the target. | 02 |
| **D13** | **Failure is reputation**, not lost revenue: a run starts with 3, each expired order decrements it, and 0 stops spawning. No game-over screen in §2. | 02 |
| **D14** | **The patience bar lives on the order ticket only** for MVP. The concept doc's "visible patience bar per customer" is satisfied by the ticket; a world-space bar is optional polish. | 02 |
| **D15** | **UI stack is uGUI** (world-space patience bars, prefab-able tickets), with the service layer required to run with *no UI present* — that is how the EditMode tests run, and it is what keeps a later swap to UI Toolkit cheap. | 02 |

---

## How the two sections divide the work

Section 1 and Section 2 were planned independently and initially disagreed on roughly a dozen
concrete points — folder layout, assembly count, namespaces, layer indices, the fixed timestep, the
`Knife ↔ Ingredient` collision rule, board size, scene name, action-map name and config type name.

The resolution, recorded above as D3–D9:

| Area | Owner |
| --- | --- |
| Folder tree, assemblies, namespaces, layer scheme, `Physics2D` settings, materials, `Game.unity`, the `Board` action map, `BoardConfig` | **§2, Phase 0** |
| Platform targets, orientation lock, aspect policy, safe-area handling, performance budgets, cross-platform parity verification | **§1** |
| Board behaviour, knife, ingredients, orders, patience | **§2, Phases 1–5** |

**§2's choices win where the two disagreed**, because they are grounded in measurements taken from
this project (the 0.02 s timestep, the tunneling budget at 40 u/s) and its tuning constants are
already calibrated to them. §1 keeps only what §2 never decided, plus one correction to §2: the
layer indices (**D7**).

The one substantive cost of that split is **D3**: five assemblies would have made "gameplay must not
reference UI" a compiler error, and one assembly makes it a convention. It is mitigated by §2's own
design — the order and patience systems are plain C# with no `MonoBehaviour` and no UI reference,
which is what actually makes them testable.

---

## Open decisions

| # | Question | Status |
| --- | --- | --- |
| O1 | Aiming feel: slingshot vs. swipe-through | **Resolved — D12.** |
| O2 | Is macOS a PC shipping target? | **Open.** Recommendation: Windows-only. macOS costs a signing/notarisation pipeline for no benefit here. Decide before the first public build. |
| O3 | Frame-rate policy: lock 60, or unlock to 120? | **Open.** Recommendation: unlock with a 60 floor. The physics step stays `0.02` either way, so a 120 Hz device gets smoother presentation, not a different simulation. |
| O4 | UI stack: uGUI or UI Toolkit? | **Resolved — D15.** |
| O5 | When do Addressables come in? | **Open.** §6, when restaurant themes multiply content. Premature before then. |

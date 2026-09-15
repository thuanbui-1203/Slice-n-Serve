# Section 2 — Core Mechanics: Implementation Plan

**Source:** *Game Concept Document: Slice & Serve*, section 2 (2.1 Ingredient Gathering / The Board, 2.2 Fulfilling Orders & Customer Patience).
**Deliverable owner:** this plan. Implementation happens in a later session.
**Status:** draft for sign-off — see [§11 Decisions requiring sign-off](#11-decisions-requiring-sign-off).

---

## 1. Scope

### In scope

| Ref | Requirement from the concept doc | Covered by |
| --- | --- | --- |
| 2.1 | Knife is launched into the board and bounces like a ping-pong ball | §5.2, §5.3 |
| 2.1 | Bouncing knife slices and collects the ingredients it hits | §5.4, §5.5 |
| 2.1 | Intuitive drag-and-release aiming on touch and mouse, with calculated velocity/angle | §5.2 |
| 2.2 | Customers arrive with specific ingredient requests | §6.1, §6.2 |
| 2.2 | Gathered ingredients automatically contribute to their dishes | §6.3 |
| 2.2 | Visible patience bar per customer | §6.4, §6.5 |
| 2.2 | Depleted patience → customer leaves angry → lost revenue or lost lives/reputation | §6.4, §6.6 |

### Explicitly out of scope (but the plan must not block them)

- **§3 Economy & Upgrades** — no upgrade UI, no cash sinks, no upgrade data. Section 2 must instead expose the *runtime-mutable tuning* seams those upgrades will write to (§3.1 below).
- **§4 Difficulty Scaling** — no time-based ramp. Section 2 exposes an `IDifficultyProvider` seam that section 4 drives.
- **§5 Multiplayer/PvP** — no networking, no shared queue, no sabotage. Section 2 keeps the order queue behind an interface and emits bounce-combo telemetry so both are addable later.
- **§6 Roadmap** — no themes, no knife classes, no VIP customers.

### Definition of done for section 2 (vertical slice)

One playable scene where: the player drags and releases → a knife launches, bounces off the board walls, and slices ingredients → the ingredients credit the oldest demanding customer's order → that customer's visible patience bar drains in real time → completing an order pays cash into a wallet value shown on screen → an order that times out makes the customer leave angry and decrements reputation. Both mouse and touch work in parity.

---

## 2. Current project state (verified)

Verified by inspecting the repository, not assumed:

| Fact | Value | Consequence for this plan |
| --- | --- | --- |
| Unity version | `6000.3.16f1` (`ProjectSettings/ProjectVersion.txt`) | Unity 6.3 LTS. `Rigidbody2D.velocity`/`drag`/`angularDrag` are **obsolete**; use `linearVelocity`/`linearDamping`/`angularDamping` ([upgrade notes](https://discussions.unity.com/t/june-doc-update/952463)). |
| Render pipeline | URP `17.3.0`, 2D renderer (`Assets/Settings/Renderer2D.asset`) | 2D lights/shaders available; keep to URP 2D shaders. |
| 2D packages | `com.unity.2d.sprite`, `2d.animation`, `2d.aseprite`, `2d.tilemap`, `2d.spriteshape` | Fully 2D workflow; sprites are the expected asset type. |
| Input | `com.unity.inputsystem 1.19.0`; **`activeInputHandler: 1`** = Input System package only | Legacy `Input.GetMouseButton` / `Input.touches` **do not work**. Everything goes through `InputAction`. |
| Input asset | `Assets/InputSystem_Actions.inputactions` with maps `Player` + `UI` only | Must add a board/gameplay action map (§5.2.1). |
| `Assets/Scenes/` | `SampleScene.unity` only | Need a dedicated gameplay scene. |
| Scripts | **None** — no `.cs` anywhere in `Assets/` | Greenfield; no migration concerns. |
| UI | `com.unity.ugui 2.0.0` + `uielements` module | uGUI recommended for HUD and world-space patience bars (§6.5). |
| Tests | `com.unity.test-framework 1.6.0` | EditMode/PlayMode tests are available and are the primary verification path (§10). |
| `Physics2D` settings | gravity `(0, -9.81)`, `m_SimulationMode: 0` (FixedUpdate), `m_VelocityIterations: 8`, `m_PositionIterations: 3`, `m_VelocityThreshold: 1`, `m_MaxTranslationSpeed: 100`, `m_DefaultMaterial: {fileID: 0}` (none), `m_QueriesStartInColliders: 1`, multithreading off | Requires several deliberate setting changes + a `PhysicsMaterial2D` (§4.5). |
| `TimeManager` | `Fixed Timestep: 0.02` (50 Hz), max timestep `0.3333` | At 40 u/s a body moves 0.8 u per step → tunneling is a real risk (§4.6). |
| Layers / Tags | `TagManager.asset` has **no** custom tags and only Default/TransparentFX/IgnoreRaycast/Water/UI named | Must define the layer scheme (§4.3). |
| Git | single commit `Initial check-in`; working tree has uncommitted URP-settings changes | Do not touch the existing uncommitted changes; this plan is doc-only. |

**Note on Unity 6.3 2D physics:** 6.3 ships a new low-level API (Box2D v3) under `UnityEngine.LowLevelPhysics2D`. It is **additive and separate** — it does not interact with `Rigidbody2D`/`Collider2D`, which are unchanged and not deprecated ([Unity manual](https://docs.unity3d.com/6000.3/Documentation//Manual/2d-physics-api/2d-physics-api-introduction.html)). **This plan uses the classic `Rigidbody2D` API**, because component-based authoring plus prefabs plus `Physics2D` queries is the right fit for a GameObject game, and because the low-level API's replacement components are not shipping ([roadmap](https://discussions.unity.com/t/low-level-2d-physics-in-unity-6-3/1683247/95)). Revisit only if ingredient counts reach the hundreds.

---

## 3. Cross-section architecture constraints

These are the seams section 2 must leave open. They are cheap now and expensive later.

### 3.1 Seams required by §3 (Economy & Upgrades)

Every §3 upgrade is a write to tuning that section 2 reads:

| §3 upgrade | Section 2 must read tuning at runtime, not bake it |
| --- | --- |
| Knife Speed | `launchMaxSpeed` / the launch power curve must come from a runtime-mutable tuning object, not constants. |
| Knife Split Chance | The knife must be **pooled and multi-instance from day one** (`KnifeManager` owning N active knives). MVP launches one; the architecture must not assume "the knife". |
| Ingredient Spawn Rate | `IngredientSpawner` must read target count / interval each tick from tuning; spawn weights must be data-driven. |
| Customer Patience | Patience must be computed as `base × customer × difficulty × upgrade` multipliers resolved at spawn time. |
| Sous-Chef Helpers | Launching must be callable **without input**: `KnifeManager.Launch(Vector2 velocity, LaunchSource source)` where `LaunchSource` is `Manual`/`Helper`. Auto-knives need no player involvement. |

### 3.2 Seams required by §4 (Difficulty Scaling)

- `IDifficultyProvider` with `float PatienceScale`, `float OrderSpawnIntervalScale`, `float IngredientDespawnPressure`. Ship `ConstantDifficultyProvider` (all 1.0) in section 2.
- All scaling must be resolvable **per order at spawn time** and **per knife at launch time**, so difficulty can change mid-run without retroactively rewriting live state.

### 3.3 Seams required by §5 (PvP)

- `IOrderQueue` interface; section 2 ships `LocalOrderQueue`. A 1v1/2v2 shared queue is a different implementation.
- Emit `KnifeBounced(knifeId, bounceIndex, position)` — the concept's sabotage trigger is "achieving specific bounce combos". Track it now, consume it later.
- Ingredient spawning must accept an external "force-spawn this ingredient/obstacle at this position" request, so sabotage can inject bad ingredients into an opponent's board.

---

## 4. Foundations (Phase 0 — do this first)

### 4.1 Folder & assembly layout

Assembly definitions matter here because they make the test suite runnable in isolation and prevent the "everything references everything" tangle:

```
Assets/
  Scripts/
    Runtime/
      SliceNServe.Runtime.asmdef          (references: Unity.InputSystem, UnityEngine.UI;
                                           add Unity.TextMeshPro only if TMP is used directly)
      Core/       GameEvents, Wallet, RunClock, TuningProvider, ServiceRegistry, GameBootstrap
      Board/      BoardBounds, KnifeManager, KnifeController, KnifeLauncher, AimInput,
                  TrajectoryPreview, IngredientSpawner, Ingredient, IngredientSlicer
      Orders/     IOrderQueue, LocalOrderQueue, OrderInstance, OrderSpawner,
                  CustomerController, CustomerView, PatienceSystem, DifficultyProvider
      Data/       IngredientDefinition, DishDefinition, CustomerDefinition, BoardConfig
    Tests/
      EditMode/   SliceNServe.Tests.EditMode.asmdef   (references: Runtime, TestRunner)
      PlayMode/   SliceNServe.Tests.PlayMode.asmdef   (references: Runtime, TestRunner)
  Prefabs/      Knife.prefab, Ingredient.prefab, Customer.prefab, OrderTicket.prefab
  ScriptableObjects/  Ingredients/, Dishes/, Customers/, BoardConfig.asset
  Art/Placeholders/   circle.png, square.png, knife.png  (see §4.7)
  Scenes/       Game.unity
docs/plans/     this file
```

### 4.2 Namespaces

`SliceNServe.Core`, `.Board`, `.Orders`, `.Data`. Data types are `[CreateAssetMenu]` ScriptableObjects so designers can author them without touching code.

### 4.3 Layer scheme

Tags are unnecessary; layers + the collision matrix carry the semantics. Add these to `ProjectSettings/TagManager.asset`:

| Layer | Index | Who |
| --- | --- | --- |
| `Knife` | 8 | Knife prefab (dynamic `Rigidbody2D`) |
| `Ingredient` | 9 | Ingredient prefabs (trigger colliders) |
| `BoardWall` | 10 | Static board boundary colliders |
| `Obstacle` | 11 | Future: §6 themes / §5 sabotage hazards |

> **Indices corrected from 6–9 to 8–11** during reconciliation with Section 1. `TagManager.asset`
> in this project shows indices 0–7 are Unity's builtin reserved slots (`Default`, `TransparentFX`,
> `Ignore Raycast`, `Water`, `UI` plus three unnamed); user layers conventionally start at 8.
> The layer *names* above are unchanged. See `README.md` decision D7.

Collision matrix:
- `Knife ↔ BoardWall`: **on** (this is the bounce).
- `Knife ↔ Ingredient`: **off** in the matrix. The knife collects ingredients by explicit sweep query (§5.5), not by trigger callbacks — this keeps collision callbacks cheap and makes collection speed-proof.
- `Ingredient ↔ everything`: **off**. Ingredients are non-physical pickups; they must never nudge the knife or each other.
- `Obstacle ↔ Knife`: **on** (reserved).

### 4.4 Scene setup

`Assets/Scenes/Game.unity`:
- `Main Camera`: Orthographic, `orthographicSize = 5.4` (⇒ 10.8 world units tall, 19.2 wide at 16:9, matching the 1920×1080 default). Linear color space is already set.
- `GameBootstrap` (single entry point) — constructs services, wires events, owns the run lifecycle.
- `BoardBounds` — parent with four static `BoxCollider2D` walls on layer `BoardWall`.
- `KnifeManager` + one `Knife.prefab` instance parked at the launcher.
- `IngredientSpawner` + `Ingredient.prefab`.
- `OrderSpawner` + `Customer.prefab` + a screen-space `Canvas` (HUD) and, per customer, a world-space `Canvas` for the patience bar.
- Background quad/SpriteRenderer at z = +1 (behind gameplay), gameplay at z = 0, HUD canvas far in front.

### 4.5 Physics 2D settings to change

| Setting | From | To | Why |
| --- | --- | --- | --- |
| Default Gravity | `(0, -9.81)` | `(0, 0)` | Top-down ping-pong board. See [decision D1](#11-decisions-requiring-sign-off). Keep `Rigidbody2D.gravityScale = 0` per body as belt-and-braces so a stray gravity change can't break the board. |
| Max Translation Speed | `100` | `200` | It is a hard global clamp on body speed; our 40 u/s cap must never be silently clipped, and the clamp interacts badly with CCD. |
| Velocity Threshold | `1` | keep `1` | Bounces below 1 u/s are treated as inelastic. Our minimum live speed is 3 u/s, so this never bites. |
| Simulation Mode | Fixed Update | keep | Determinism and stable 50 Hz stepping. |
| Velocity / Position Iterations | `8` / `3` | keep | Adequate; raise position iterations to `4` only if stacking artifacts appear. |
| Use Multithreading (Job Options) | off | leave off for now | Revisit if ingredient count exceeds ~200. |
| Default Material | none | leave none | Materials are assigned per-collider (§4.6) for explicit control. |

Create two `PhysicsMaterial2D` assets:
- `Assets/Settings/KnifeMaterial.physicsMaterial2D` — `friction = 0`, `bounciness = 1`.
- `Assets/Settings/WallMaterial.physicsMaterial2D` — `friction = 0`, `bounciness = 1`.

With friction 0 and bounciness 1 on both sides, the knife's trajectory is a clean mirror reflection and energy loss is **entirely under our explicit control** in code (§5.3), instead of being an emergent property of material mixing. Verify the material mixing rule in the Editor (Unity/Box2D takes the higher bounciness and the geometric mean of friction; the setting above makes it moot).

### 4.6 Tunneling — the single biggest technical risk

At the project's `Fixed Timestep = 0.02` and a knife cap of 40 u/s, the knife travels **0.8 world units per physics step**. Any collider thinner than that can be passed through in one step. A 0.5-unit wall *will* leak.

Three defences, all of them:

1. **Thick wall colliders.** Visual walls can be 0.25 u; the `BoxCollider2D` on `BoardWall` is **2.0 u thick**, offset outward so the *inner face* sits exactly on the play-area boundary. Thicker than the maximum per-step travel by 2.5×.
2. **CCD on the moving body.** `KnifeController` sets `rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous`. Continuous mode sweeps against **static** colliders, which is exactly our wall case. (Plain `Discrete` would tunnel; `ContinuousDynamic` is only needed once moving obstacles exist in §5/§6, and is more expensive.)
3. **Swept collection query for ingredients.** CCD does **not** apply to sensor/trigger overlaps, so a fast knife could pass an ingredient trigger between steps. Collection therefore uses an explicit sweep from the previous to the current position (§5.5).

Also set `rb.interpolation = RigidbodyInterpolation2D.Interpolate` so the knife renders smoothly between 50 Hz physics steps.

### 4.7 Placeholder art

Section 2 must not block on art. Commit three tiny PNGs (a 64×64 white circle, a 64×64 white square, and a simple knife silhouette) into `Assets/Art/Placeholders/`, tinted per ingredient via `SpriteRenderer.color`. Ingredients get a `CircleCollider2D` trigger matching the sprite radius. Replace with real art later without touching code.

---

## 5. 2.1 — Ingredient Gathering (The Board)

### 5.1 Board model

- The board is a closed rectangle: four seamless walls, **no internal corners** in MVP (internal geometry is a §6 theme concern).
- Play area (`BoardConfig.worldBounds`): `Rect(-9.6, -5.4, 19.2, 10.8)`. The `BoardWall` collider inner faces sit exactly on this rectangle.
- Ingredient zone (`BoardConfig.ingredientZone`): `Rect(-9.1, -3.3, 18.2, 8.2)` — inset 0.5 u on the left, right and top edges (keeping pickups clear of the screen edge) and **2.1 u on the bottom edge**, which reserves a clean launch corridor so no ingredient can spawn on top of the launcher.
- Launcher anchor (`BoardConfig.launcherPosition`): bottom-centre `(0, -4.4)`. Clearance to the bottom wall's inner face is 1.0 u (satisfying §5.6), and the nearest possible ingredient edge is 0.6 u away, so a fresh knife never starts overlapping a pickup.

### 5.2 Launch: drag-and-release aiming

#### 5.2.1 Input wiring

The Input System has **no built-in `Drag` or `Swipe` interaction** — the stock 1.19 set is Default, Press, Hold, Tap, SlowTap, MultiTap ([interactions namespace](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/api/UnityEngine.InputSystem.Interactions.html)). Drag-and-release is therefore implemented as **two actions with manual state tracking**, which is also the most portable approach:

Add a `Board` action map to `Assets/InputSystem_Actions.inputactions`:

| Action | Type | Bindings | Interaction |
| --- | --- | --- | --- |
| `Aim` | Value / Vector2 | `<Pointer>/position`, `<Touchscreen>/primaryTouch/position`, `<Pen>/position` | none — polled every frame |
| `Launch` | Button | `<Mouse>/leftButton`, `<Touchscreen>/primaryTouch/press`, `<Pen>/tip` | `Press` (`PressAndRelease`) |

`AimInput` then runs a tiny state machine driven by `Aim` + `Launch`:
`Idle → (Launch.started) Dragging → (Launch.canceled) Release` firing `OnLaunch(Vector2 dragStart, Vector2 dragEnd)`.
Because the pointer position is polled continuously, mouse and touch share one code path — no `#if UNITY_IOS` branches. Guard against multi-touch by binding only `primaryTouch` in MVP.

#### 5.2.2 Drag-to-velocity mapping

Wide dead-zone and generous max drag distance, since this is the core skill expression on both platforms:

```
dragVector  = dragEnd - dragStart                       // world units, y-up
power       = Mathf.Clamp01(dragVector.magnitude / maxDragLength)   // maxDragLength = 3.5 u
launchDir   = -dragVector.normalized                     // pull-back slingshot (see D4)
speed       = Mathf.Lerp(launchMinSpeed, launchMaxSpeed, powerEase(power))  // 8 → 22 u/s
velocity    = launchDir * speed
```

`powerEase` starts linear (`power`) and is a designer knob once feel is testable.
A **dead-zone** of 0.35 u means a tap/short drag does not launch.
The launch angle is also clamped so it is never within 4° of parallel to a wall (§5.6).

Three distinct speed values, deliberately named so they cannot be confused:

| Name | Value | Meaning |
| --- | --- | --- |
| `launchMinSpeed` / `launchMaxSpeed` | 8 / 22 u/s | The **design** envelope a player can produce. 22 u/s ⇒ 0.44 u of travel per 0.02 s step, comfortably inside the 2.0 u wall thickness even before CCD. |
| `speedHardCap` | 40 u/s | A **physics guard** applied after every bounce (§5.3). Unreachable by a single launch; it exists so a future §3 multiplier or §5 sabotage cannot push a body fast enough to defeat CCD. |
| `minLiveSpeed` | 3 u/s | Below this the knife is spent and returns to the launcher (§5.3). |

#### 5.2.3 Trajectory preview

`TrajectoryPreview` draws a `LineRenderer` showing the first **2** bounces while dragging:

- Pure geometric prediction: `Physics2D.Raycast` with `LayerMask` = `BoardWall` only, up to N reflections, `direction = Vector2.Reflect(direction, hit.normal)`, stopping at the wall-bounded segment budget.
- This is a *preview*, not a simulation — it must not use `Physics2D.Simulate`, and it must ignore ingredients (so it stays honest about walls only). Documented as such in the tooltip so nobody "fixes" it into a physics prediction.
- Fade the line with distance; hide it on release.

#### 5.2.4 API shape

```csharp
public enum LaunchSource { Manual, Helper }

public sealed class KnifeManager : MonoBehaviour
{
    public bool CanLaunch { get; }                       // true when a knife is idle at the launcher
    public KnifeController Launch(Vector2 velocity, LaunchSource source);
    public void ReturnAllKnives();
    public event Action<KnifeController> KnifeReturned;
}
```

`Launch` takes a **velocity, not a drag**, so §3's Sous-Chef helpers can call it with a computed aim vector and `LaunchSource.Helper` with zero input involvement.

### 5.3 Bounce, energy and lifetime

The knife prefab:

| Part | Setup |
| --- | --- |
| Root | `Rigidbody2D` — `Body Type = Dynamic`, `gravityScale = 0`, `linearDamping = 0`, `angularDamping = 0.4` (spin is cosmetic only), `collisionDetectionMode = Continuous`, `interpolation = Interpolate`, material `KnifeMaterial`. |
| Collider | **`CircleCollider2D`** on the root, `radius = 0.18`. A circle is chosen over a capsule **on purpose**: a bouncing body needs a *rotation-independent* silhouette, or spin makes the rebound direction unpredictable and the physics stop being learnable. |
| Visual | `SpriteRenderer` on a **separate child** transform that spins freely, so the knife's spin animation never touches the collider. |
| `KnifeController` | Owns bounce counting, energy retention, termination and the sweep query (§5.5). |

Energy decay is explicit and applied post-step, in `OnCollisionEnter2D`:

```csharp
/// Rotate a 2D vector by degrees, staying in Vector2 (no Vector3/Quaternion round-trip).
static Vector2 Rotate(Vector2 v, float degrees)
{
    float r = degrees * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
    return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
}

void OnCollisionEnter2D(Collision2D c)
{
    // Layer, not tag — this project defines no tags (§4.3).
    if (c.collider.gameObject.layer != _wallLayer) return;

    ContactPoint2D contact = c.GetContact(0);       // contactCount is always >= 1 here
    bounceCount++;
    OnBounced?.Invoke(bounceCount, contact.point, contact.normal);

    // OnCollisionEnter2D runs *after* the physics step, so the bounce has already
    // reflected the velocity; this only scales it. Deterministic and designer-tunable.
    Vector2 v = rb.linearVelocity * bounceRetention;            // 0.90
    if (v.sqrMagnitude > speedHardCap * speedHardCap)           // 40 u/s physics guard
        v = v.normalized * speedHardCap;                        // (never reached by one launch)

    // Anti-"wall-hugging": if the outgoing direction is nearly parallel to the wall,
    // nudge it off a few degrees so the knife cannot ride the boundary.
    if (Mathf.Abs(Vector2.Dot(v.normalized, contact.normal)) < 0.08f)
        v = Rotate(v, 6f * (Random.value < 0.5f ? -1f : 1f));

    rb.linearVelocity = v;
}
```

The `0.08` threshold is applied to the **outgoing** velocity, which is precisely the degenerate grazing case: the reflection is already near-tangent, so a few degrees is enough to break the loop without visibly kinking the trajectory.

Termination — the knife ends its life when **any** of:

| Condition | Value | Notes |
| --- | --- | --- |
| Speed below `minLiveSpeed` | `< 3 u/s` | Checked in `FixedUpdate`. |
| Lifetime exceeded | `> 15 s` | Hard anti-stall cap. |
| Bounce cap | `> 60` | Anti-degenerate-loop cap. |

On termination: `KnifeManager` plays a short fade, returns the knife to the launcher, and fires `KnifeReturned` → `CanLaunch = true`. `ReturnAllKnives()` exists for the §3 "wave clear" and for the §5 PvP round reset.

Starting numbers (~13 s of life from a full-power launch: `ln(3/22)/ln(0.9) ≈ 19` bounces, ~13–15 s with losses) are calibrated to feel like "one satisfying flurry" and are all `BoardConfig` fields.

### 5.4 Ingredients

`IngredientDefinition` (ScriptableObject):

| Field | Type | Purpose |
| --- | --- | --- |
| `id` | `string` | Stable key used by recipes and save data. Never the asset name. |
| `displayName` | `string` | UI. |
| `sprite`, `tint` | `Sprite`, `Color` | Placeholder-friendly rendering. |
| `radius` | `float` | Default `0.35`. Collider + spawn separation. |
| `spawnWeight` | `float` | Weighted random in the spawn table. `0` ⇒ never spawns naturally (quest/roadmap-only ingredients). |
| `slicesOnHit` | `bool` | Reserved: `false` = knocks the knife aside (obstacle behaviour, §6). Code path stubbed, unused in MVP. |

`IngredientSpawner`:
- Fields: `targetCount = 12`, `respawnInterval = 0.6 s`, `maxCount = 18`, `minSeparation = 0.6 u`, `spawnWeights` from the definition assets.
- Each tick: if `liveCount < targetCount`, spawn up to `maxSpawnsPerTick` (default 2).
- Placement: up to 8 rejection-sampling attempts at a random point in `ingredientZone` inset by `radius`, validated with `Physics2D.OverlapCircle(pos, radius + minSeparation, ingredientMask)` **before** instantiating the candidate. Failing all 8 attempts, defer to the next tick rather than force-placing — force-placing is exactly how you get stacks. (`Physics2D.queriesStartInColliders` is `1` in this project, but that setting only matters when a query filter includes the *querying object's own* layer. Our filter is the `Ingredient` layer exclusively and the candidate does not exist yet, so it is a non-issue here — and it is a non-issue for the knife's sweep in §5.5 for the same reason. It becomes an issue if somebody ever widens either filter to include the `Knife` layer.)
- Despawn: ingredients that are not collected expire after `lifetime = 12 s` with a 1 s fade, so the board self-refreshes and the player can never be starved of a specific ingredient permanently. Expiry is a `BoardConfig` field because §4 may want to tighten it.
- Reads all of the above from `RuntimeTuning` every tick (§3.1) — never caches the values in `Awake`.

### 5.5 Collection — swept, speed-proof

Because the collision matrix has `Knife ↔ Ingredient` **off** and ingredients are triggers (which CCD does not sweep), collection is done by the knife itself, in `FixedUpdate`, over the segment it is about to travel:

```csharp
void FixedUpdate()
{
    Vector2 v = rb.linearVelocity;
    if (v.sqrMagnitude < 1e-6f) return;   // a zero-length cast direction returns nothing

    // _ingredientFilter: ContactFilter2D with useTriggers = true, layerMask = Ingredient only.
    int hits = rb.Cast(v.normalized, _ingredientFilter, _results, v.magnitude * Time.fixedDeltaTime);
    for (int i = 0; i < hits; i++) TryCollect(_results[i].collider);   // dedupes within the step
}
```

Rationale: `Rigidbody2D.Cast` sweeps the knife's own colliders along the motion vector, so an ingredient anywhere on this step's path is found regardless of speed. This removes tunneling from the collection path entirely, and it is **testable headlessly** (§10) in a way that trigger callbacks are not.

On each hit: skip duplicates within the same step (a set keyed by collider instance id), then per ingredient —
1. `IngredientSliced` event (`ingredientId`, world position, knife id, bounceIndex),
2. combo counter `++` (reset on knife launch — this is the §5 sabotage telemetry),
3. destroy the ingredient with a slice VFX + SFX,
4. forward the ingredient id to the order system: `OrderQueueService.SubmitIngredient(ingredientId, count: 1)`.

Collection must never mutate the knife's velocity — ingredients are rewards, not obstacles. That keeps the skill expression purely about wall geometry and launch angle, and keeps §3's balance math linear.

### 5.6 Anti-stall rules (make these explicit, they will bite)

- Launch angle: clamp the aim so it is never within 4° of parallel to any wall, guaranteeing the first wall contact is never a graze.
- The launcher keeps 1.0 u of clearance to the nearest wall (§5.1), so a fresh knife never self-collides with the boundary before the player's launch has even begun.
- If the knife somehow ends up outside `worldBounds` (a physics failure), `KnifeController` detects it in `FixedUpdate` and force-returns — a safety net, not a mechanism.
- `KnifeManager` refuses a `Launch` while `!CanLaunch`, so a double-tap cannot spawn two knives before §3's split upgrade exists.

---

## 6. 2.2 — Fulfilling Orders & Customer Patience

### 6.1 Data model (ScriptableObjects)

**`DishDefinition`** — a recipe.
| Field | Type | Notes |
| --- | --- | --- |
| `id`, `displayName`, `sprite` | | UI + stable key. |
| `ingredients` | `List<IngredientRequirement>` | `{ IngredientDefinition ingredient; int count; }` — a dish may need 2 tomatoes. This is the *authored* shape; the runtime mirror with a mutable `fulfilled` counter is `OrderLine` (§6.2). |
| `baseCash` | `int` | Reward before §3 multipliers. |
| `baseOrderWeight` | `float` | Relative draw weight. |

**`CustomerDefinition`** — an archetype (the extension point for §6's VIP/"boss" customers and for future PvP AI opponents).
| Field | Type | Notes |
| --- | --- | --- |
| `id`, `displayName`, `spriteSet` | | |
| `patienceMultiplier` | `float` | e.g. patient `1.4`, average `1.0`, impatient `0.6`. |
| `tipMultiplier` | `float` | Reserved for §3. |
| `orderCount` | `int` | How many dishes this customer wants (MVP: `1`). |

**`BoardConfig`** — one asset holding every number referenced in §5, so a designer can retune the whole board in one place.

### 6.2 Order generation

`OrderSpawner`:
- Target concurrent customers `maxActiveOrders = 4`; spawn interval `6 s` (`× difficultyProvider.OrderSpawnIntervalScale`).
- Pick a `CustomerDefinition` by weight, then a `DishDefinition` by weight, then build an `OrderInstance`.
- Pick dishes **weighted towards ingredients currently abundant on the board** — a simple guard that keeps the game fair: weight each dish by `baseOrderWeight / (1 + missingIngredientPenalty)`. Without this, the RNG can hand out three dishes that need an ingredient the spawn table rarely produces, and the run dies to bad luck rather than bad play. Keep it as a small, tunable bias, not a hard rule.

`OrderInstance` (plain C# runtime object, **not** a MonoBehaviour — this is what makes the logic unit-testable):
```
Guid orderId; DishDefinition dish; CustomerDefinition customer;
OrderLine[] lines;            // { IngredientDefinition ingredient; int required; int fulfilled; }
float spawnTime; float patienceDuration; float remainingPatience;
PatienceState state;          // Normal | Hurry | Critical | Expired | Served
bool IsComplete => every line fulfilled >= required
```

### 6.3 Auto-credit rule

`LocalOrderQueue.SubmitIngredient(ingredientId, count)` — the concept doc's "ingredients automatically contribute to completing their dishes":

1. Candidates = active orders with a line for `ingredientId` where `fulfilled < required`.
2. Choose the target = **earliest `spawnTime`** (oldest customer first); tie-break on **fewest remaining units overall** (finish nearly-done dishes first).
3. Credit one unit. If the order `IsComplete` → serve it (§6.6).
4. If no candidate exists → fire `IngredientWasted(ingredientId, position)` for player feedback (a grey puff + soft "thud"). MVP discards it; see [D3](#11-decisions-requiring-sign-off) for the pantry alternative.

This is deliberately a pure function over a list of orders — the highest-value thing to unit-test in the whole section.

### 6.4 Patience

Resolved **once, at spawn**, so later tuning changes never retroactively rewrite a live customer (needed for §4 and for PvP fairness):

```
patienceDuration = basePatienceSeconds            // BoardConfig, default 25 s
                 * customer.patienceMultiplier     // CustomerDefinition
                 * difficultyProvider.PatienceScale // §4 seam, 1.0 in section 2
                 * tuning.patienceUpgradeMultiplier // §3 seam, 1.0 in section 2
```

Depletion is linear: `remainingPatience -= Time.deltaTime` in `Update`. A pure `PatienceSystem.Tick(remaining, delta)` helper does the math so it is testable without a frame loop.

Patience bands drive all feedback:

| State | Remaining | Feedback |
| --- | --- | --- |
| `Normal` | > 60% | Green bar, occasional idle animation. |
| `Hurry` | 30–60% | Amber bar, faster pulse, first "impatient" animation trigger. |
| `Critical` | < 30% | Red bar, shaking, ticking SFX, bar pulses in sync with SFX. |
| `Expired` | 0 | See §6.6. |

### 6.5 Views

**`OrderTicketView`** (screen-space uGUI, one per active order, left→right in spawn order): dish icon, ingredient checklist with `fulfilled/required` counters and a checkmark per completed line, customer portrait, and the patience bar. Bind with **plain C# events** from the services, not per-frame polling — the service layer must be usable with zero UI present (that is what the tests do).

`CustomerView` (world-space): the sprite, an animation state machine (`Idle / Impatient / Critical / Angry / Happy`) reserved for real art, plus a small world-space bar. MVP can render patience **only** on the ticket and skip the world-space bar — decide with [D5](#11-decisions-requiring-sign-off).

uGUI is recommended over UI Toolkit here: `com.unity.ugui 2.0.0` is present, world-space persistence bars are trivial in uGUI, and Ticket views are a natural prefab. (Note: in Unity 6, TextMeshPro ships inside `com.unity.ugui`, so `Window > TextMeshPro > Import TMP Essential Resources` is a one-time setup step before the first ticket renders text.)

### 6.6 Serving and failing

On completion (`OrderQueueService`):
1. Mark `Served`; stop patience depletion.
2. `wallet.Add(cash)` — cash = `dish.baseCash × customer.tipMultiplier × tuning.cashMultiplier`. `Wallet` here is a **minimal stub** (`int Amount`, `event Action<int> Changed`) that §3 will grow into the real economy; section 2 only needs the value and its change event to prove the loop closes.
3. Fire `OrderServed(orderId, cashEarned)` → `CustomerView` plays happy + walks off; ticket animates out and frees its slot.

On expiry:
1. Mark `Expired`; the order leaves the queue, freeing a slot for a new customer.
2. Fire `OrderExpired(orderId, customerId)`.
3. `ReputationService` decrements a run-level counter (`int Reputation`, default `3`), firing an event. MVP: at 0, log/flag a "game over" state and stop spawning — **no game-over screen**, that belongs to a later pass. The concept doc offers "lost revenue **or** lost lives/reputation" as alternatives; taking **reputation** is the recommendation because it makes failure legible and is reversible via §3 upgrades. Confirm with [D6](#11-decisions-requiring-sign-off).

---

## 7. Data flow

```
AimInput ──OnLaunch(dragStart, dragEnd)──► KnifeManager.Launch(velocity, Manual)
                                        │
                                        ▼
                                  KnifeController  ──OnCollisionEnter2D──► KnifeBounced (combo)
                                        │
                                  FixedUpdate: rb.Cast(Ingredient) ──► IngredientSliced
                                                                          │
                                         IngredientSpawner ◄── (board refill)
                                                                          ▼
                              LocalOrderQueue.SubmitIngredient(id, 1)
                                        │                 │
                                        │                 └──► IngredientWasted
                                        ▼
                              OrderInstance credit ──► OrderProgressed (UI)
                                        │
                        ┌───────────────┴───────────────┐
                        ▼                               ▼
                    OrderServed                     OrderExpired
                        │                               │
                   Wallet.Add(cash)                ReputationService.Dec
```

Every arrow is a plain C# event or a direct method call on an interface. No global singletons beyond the single `ServiceRegistry` created by `GameBootstrap`; no DI framework (adding one is a §3+ decision, not a section-2 need).

---

## 8. Implementation phases

Ordered so each phase is independently runnable and verifiable. Each phase ends playable — never mid-refactor.

| # | Phase | Deliverable | Acceptance criteria |
| --- | --- | --- | --- |
| 0 | **Foundations** | Folders, asmdefs, layers, `Physics2D` settings, materials, `Game.unity` with camera + walls, placeholder art | Play mode shows an empty walled board; an EditMode test assembly compiles and runs. |
| 1 | **Launch** | `Board` action map, `AimInput`, `TrajectoryPreview`, `KnifeManager.Launch` | Mouse drag-and-release and touch drag-and-release both launch the knife; preview shows 2 bounces and matches the actual first bounce; a tap does nothing; launching while a knife is live is refused. |
| 2 | **Bounce & lifetime** | `KnifeController` CCD, materials, retention, anti-stall, return | A 22 u/s launch never escapes the board over a 20 s headless simulation; the knife always terminates and `CanLaunch` returns true; bounce counter increments correctly. |
| 3 | **Ingredients** | `IngredientDefinition`, `IngredientSpawner`, `Ingredient`, swept collection, VFX/SFX hooks | Board maintains ~12 ingredients; a knife passing an ingredient at max speed always collects it (headless test); ingredients never deflect the knife; uncollected ingredients expire. |
| 4 | **Orders & patience** | `DishDefinition`/`CustomerDefinition`, `LocalOrderQueue`, `PatienceSystem`, `OrderTicketView`, `CustomerView` | Submitting ingredients credits the oldest demanding order; tickets update live; patience drains and bands change at 60%/30%; expiry removes the order and decrements reputation. |
| 5 | **Slice glue & polish** | `Wallet`, `ReputationService`, HUD, run start/stop, difficulty seams, tests | The §1 done-criteria loop plays end to end; `ConstantDifficultyProvider` is wired; full EditMode + PlayMode suites green. |

---

## 9. Tuning table (starting values, all in `BoardConfig` / data assets)

| Parameter | Start | Raise it if… | Lower it if… |
| --- | --- | --- | --- |
| Board size | 19.2 × 10.8 u | — | gameplay feels slow |
| Launcher position | `(0, -4.4)` | ingredients feel unreachable | knife has no room |
| `launchMinSpeed` / `launchMaxSpeed` | 8 / 22 u/s | bounces feel mushy | knife is uncontrollable |
| `speedHardCap` (physics guard) | 40 u/s | — | — |
| `maxDragLength` | 3.5 u | mobile aim feels cramped | aim feels imprecise |
| Dead-zone | 0.35 u | accidental launches | taps feel ignored |
| `bounceRetention` | 0.90 | knife dies too fast | knife never ends (±0.02 steps) |
| `minLiveSpeed` / lifetime / bounce cap | 3 u/s / 15 s / 60 | — | knife lingers |
| Ingredient target / max / interval | 12 / 18 / 0.6 s | board looks empty | board is cluttered |
| Ingredient radius / min separation / lifetime | 0.35 / 0.6 / 12 s | collection is fiddly | spawns fail often |
| `maxActiveOrders` | 4 | HUD has room and difficulty is low | HUD is crowded |
| Order spawn interval | 6 s | — | board can't keep up |
| `basePatience` | 25 s | too punishing | too easy |
| Multi-ingredient dish share | ≤ 40% for the first 2 min | — | early game is unfair |
| `Reputation` start | 3 | forgiving | tense |

---

## 10. Test strategy

`com.unity.test-framework 1.6.0` is present, so verification does **not** require a human in the Editor. Two tiers:

**EditMode — pure logic, no scene, fast.** This is where the bulk of the value is, because the order and patience systems are deliberately plain C# objects:
- `OrderCreditTests` — oldest-demander targeting; tie-break on fewest remaining; multi-unit recipes; no-match ⇒ `IngredientWasted` exactly once; a served order is never credited again; a completed order frees its slot.
- `PatienceTests` — `base × customer × difficulty × upgrade`; band transitions at exactly 60% and 30%; expiry at exactly 0; an expired order stops depleting and never serves.
- `DifficultyTests` — `ConstantDifficultyProvider` returns 1.0; a fake provider provably changes `patienceDuration` at spawn time and **not** on already-spawned orders.
- `SpawnTableTests` — seeded RNG produces the expected weighted distribution within tolerance; `spawnWeight = 0` never appears; dish-draw bias reduces the "unreachable ingredient" rate.
- `AimMathTests` — drag length → speed mapping and clamping; dead-zone; pull-back vs direct direction; the parallel-to-wall clamp.

**PlayMode/headless physics — the things a human would otherwise have to eyeball.** Use a dedicated scene plus `PhysicsScene2D` with `Physics2D.simulationMode = SimulationMode2D.Script` and manual `physicsScene.Simulate(0.02f)` stepping. This gives deterministic, editor-independent, fast tests — and a future, non-flaky home for CI:
- **Tunneling guard**: launch at 40 u/s (above the designed cap, on purpose) for 1,000 steps; assert the knife's position never leaves `worldBounds` by more than a collider radius.
- **Collection at speed**: place an ingredient trigger directly on a 40 u/s path; assert it is collected in the single step that crosses it (this is the test that fails if somebody replaces the swept query with `OnTriggerEnter2D`).
- **Energy math**: assert the speed after N synthetic bounces equals `v × retention^N` within epsilon.
- **Anti-stall**: assert a launched knife always reaches a terminal state within `lifetime + 1 s` of simulated time.

Editor-independent physics results are *not* guaranteed to be bit-identical across platforms (Box2D is deterministic for a given binary/platform/order, not universally), so **assert invariants and bounds, never exact trajectories.** This is the single most important guideline for keeping this suite green.

Run command for a future session:

```
"C:\Program Files\Unity\Hub\Editor\6000.3.16f1\Editor\Unity.exe" ^
  -batchmode -nographics -projectPath "<repo>" ^
  -runTests -testPlatform EditMode -testResults "%TEMP%\editmode.xml" -logFile -
```

Manual verification checklist for the phases that are genuinely about feel (must be done in the Editor, by a human):
1. Mouse drag from the launcher → preview appears and matches the real first bounce.
2. Same on a touch device/Device Simulator (`com.unity.device-simulator.devices` is installed) — aim is not occluded by the finger.
3. A full-power launch lasts roughly 10–15 s and never gets stuck on a wall or in a loop.
4. Ingredient slices feel responsive: no ingredient is ever visually passed through.
5. A ticket's patience bar, its band colour changes, and the customer's reaction all agree with what actually happened.

---

## 11. Decisions requiring sign-off

| # | Decision | Recommended | Alternative | Why it matters |
| --- | --- | --- | --- | --- |
| **D1** | Board gravity | **Zero-g `(0,0)`** — pure Pong; skill = wall geometry | Gravity `≈ -0.6 × gravityScale` — pinball/Peggle feel, arcing and drops | Changes the entire feel and every tuning constant. The doc says both "ping-pong" and "pinball". Zero-g is recommended because aiming is *predictable*, which the drag-and-release control scheme depends on. Keep `gravityScale` a `BoardConfig` field so the alternative is one asset edit, not a rewrite. |
| **D2** | Ingredients solid or non-solid | **Non-solid** (triggers, collected via sweep) | Solid dynamic bodies that knock the knife around | Solid is more chaotic and juicy; non-solid makes skill legible and §3 upgrade math linear. The `slicesOnHit` flag leaves the solid path open for §6 obstacles. |
| **D3** | Ingredient overflow | **Discard** + wasted feedback, per the doc's "automatically contribute" | A small pantry buffer (e.g. 5 per ingredient) usable by later orders | A pantry adds strategy and reduces frustration but complicates the auto-credit rule and the UI. MVP-discard is simpler and matches the doc literally. |
| **D4** | Aim direction convention | **Pull-back slingshot** (drag away from the target) | Direct (drag toward the target) | Slingshot keeps the finger off what you are aiming at — significant on mobile. Both are ~10 lines to swap, but pick one now so tuning and tutorials are written against it. |
| **D5** | Patience bar location | **Ticket only** for MVP | Ticket + world-space bar above each customer | The doc says "each customer has a visible patience bar" — a ticket bar satisfies it, but a world-space bar may be what "visible" means to a reader of the doc. |
| **D6** | Failure mode | **Reputation** (3 lives), decrement per expiry | Lost revenue only | The doc offers both. Reputation is legible and is where §3 upgrades get their value. |
| **D7** | Aim preview detail | **2 bounce reflections**, walls only | 0 (raw direction arrow) or unlimited | More bounces = more help. 2 is a good default; it is a single `BoardConfig` int. |

Additional open questions (not blockers, resolve during implementation):

- Should a returned knife auto-relaunch when §3 helpers exist, or always wait for input? (MVP: always wait — `LaunchSource` already distinguishes the paths.)
- Should the board's ingredient despawn pressure tighten over time in section 2, or is that strictly §4? (Plan assumes strictly §4 via `IDifficultyProvider`.)
- Do we need a pantry/order UI affordance for dishes with 3+ ingredient types at 1920×1080 with 4 concurrent orders? Resolve with a layout spike in Phase 4 before building the final ticket prefab.

---

## 12. Things to verify in the Editor before relying on them

Flagged honestly, because they could not be verified from the repository alone:

1. **`PhysicsMaterial2D` mixing** — that bounciness 1 on both the knife and the walls yields a fully elastic bounce in this Unity version. Mitigation: energy decay is applied explicitly in code, so even if mixing differs, feel is controllable.
2. **`Rigidbody2D.Cast` with `ContactFilter2D.useTriggers = true`** — confirm it returns trigger colliders on the `Ingredient` layer from a `Knife`-layer body. If it does not, fall back to `Collider2D.Cast` on the knife's own collider.
3. **`PhysicsScene2D.Simulate`** behaviour in EditMode tests on this version (particularly whether a `Rigidbody2D` in a scene created via `SceneManager.CreateScene` is picked up). If not, move the physics tests to PlayMode.
4. **Whether `CollisionDetectionMode2D.Continuous` alone is sufficient** at 40 u/s in this build, or whether the 2.0 u walls are also load-bearing. The tunneling test in §10 answers this empirically — that is why it launches *above* the design cap.

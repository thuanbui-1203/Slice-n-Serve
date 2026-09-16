# Section 2 — Core Mechanics: Plan

**Concept source:** [`../concept.md`](../concept.md), §2 — *2.1 Ingredient Gathering (The Board)*, *2.2 Fulfilling Orders & Customer Patience*.
**Runbook:** [`section-2-implementation-steps.md`](./section-2-implementation-steps.md) — the executable half. Where this plan and the runbook disagree, this plan wins.
**Sibling:** [`section-1-game-overview.md`](./section-1-game-overview.md) — owns platform, orientation, aspect and budgets.
**Decisions:** [`README.md`](./README.md) is the single decision log. This section owns **D3–D6** and **D10–D15**.

---

## 1. Scope

### In scope

| Ref | Concept-document requirement | Covered by |
| --- | --- | --- |
| 2.1 | Knife is launched into the board and bounces like a ping-pong ball | §5.2, §5.3 |
| 2.1 | Bouncing knife slices and collects the ingredients it hits | §5.4, §5.5 |
| 2.1 | Intuitive drag-and-release aiming on touch and mouse, with calculated velocity/angle | §5.2 |
| 2.2 | Customers arrive with specific ingredient requests | §6.1, §6.2 |
| 2.2 | Gathered ingredients automatically contribute to their dishes | §6.3 |
| 2.2 | Visible patience bar per customer | §6.4, §6.5 |
| 2.2 | Depleted patience → customer leaves angry → lost revenue or lost lives/reputation | §6.4, §6.6 |

### Out of scope — but the plan must not block it

- **§3 Economy & Upgrades** — no upgrade UI, no cash sinks, no upgrade data. §2 instead exposes the *runtime-mutable tuning* those upgrades will write to (§3.1).
- **§4 Difficulty Scaling** — no time-based ramp. §2 ships an `IDifficultyProvider` seam (§3.2).
- **§5 Multiplayer (PvP)** — no networking. §2 keeps the order queue behind an interface and emits bounce-combo telemetry (§3.3).
- **§6 Roadmap** — no themes, no knife classes, no VIP customers.

### Definition of done

One playable scene where: a drag-and-release gesture launches the knife; it bounces off the board walls and slices ingredients; sliced ingredients credit the oldest demanding customer's order; that customer's patience bar drains visibly in real time and bands at 60 % / 30 %; completing an order adds cash to a HUD total; letting an order expire makes the customer leave angry and decrements reputation. Mouse and touch behave identically because they are the *same* gesture.

---

## 2. Project state and the constraints it imposes

The full verified baseline is in [`README.md`](./README.md#baseline--verified-from-the-repository). Only the facts that shape §2's design are repeated here:

| Fact | Consequence for this plan |
| --- | --- |
| Unity `6000.3.16f1`. `Rigidbody2D.velocity` / `.drag` / `.angularDrag` are **obsolete** — use `linearVelocity` / `linearDamping` / `angularDamping` ([upgrade notes](https://discussions.unity.com/t/june-doc-update/952463)). | Every code snippet must use the Unity 6 names. |
| `activeInputHandler: 1` — Input System package only. | Legacy `Input.GetMouseButton` / `Input.touches` **do not work**. All input goes through `InputAction`. |
| `Assets/InputSystem_Actions.inputactions` holds only the template `Player` and `UI` maps. | §2 adds a `Board` map (§5.2.1). |
| `Fixed Timestep: 0.02` (50 Hz), `Maximum Allowed Timestep: 0.33333334`. | At 40 u/s a body travels **0.8 u per step** — tunneling is a real risk (§4.6), and the timestep is kept as-is (**D5**). |
| `Physics2D`: gravity `(0, -9.81)`, `maxTranslationSpeed: 100`, no default material. | §4.5 changes three settings and adds two materials. |
| **No custom layers** (`TagManager` shows only `Default`/`TransparentFX`/`Ignore Raycast`/`Water`/`UI`, indices 6–31 unnamed). | §4.3 creates four user layers at 8–11 (**D7**). |
| Only `Assets/Scenes/SampleScene.unity` exists and only it is in the build list. | §4.4 creates `Game.unity` and adds it to the build list. |
| The only script is `Assets/Scripts/GameManager.cs` — a 16-line empty stub, no namespace, no `.asmdef`, outside the planned `Scripts/Runtime/` tree. | Greenfield: no migration burden. The stub is **not** part of the design — it should be deleted or absorbed when `Scripts/Runtime/` is created (§4.1). |
| `com.unity.test-framework 1.6.0` is installed. | EditMode / PlayMode tests are the primary verification path (§10), not optional. |

**On Unity 6.3 2D physics.** 6.3 ships a new low-level API (Box2D v3) under `UnityEngine.LowLevelPhysics2D`. It is **additive and separate** — it does not interact with `Rigidbody2D`/`Collider2D`, which are unchanged and not deprecated ([Unity manual](https://docs.unity3d.com/6000.3/Documentation//Manual/2d-physics-api/2d-physics-api-introduction.html)). **This plan uses the classic `Rigidbody2D` API**, because component-based authoring plus prefabs plus `Physics2D` queries is the right fit for a GameObject game, and because the low-level API's replacement components are not shipping ([roadmap](https://discussions.unity.com/t/low-level-2d-physics-in-unity-6-3/1683247/95)). Revisit only if ingredient counts reach the hundreds.

---

## 3. Seams this plan must leave open

These are cheap now and expensive later.

### 3.1 Required by §3 (Economy & Upgrades)

Every §3 upgrade is a *write to tuning that §2 reads*. None of them may require a code change.

| §3 upgrade | What §2 must expose |
| --- | --- |
| Knife Speed | `launchMaxSpeed` and the launch power curve read through a runtime-mutable tuning object, never baked as constants. |
| Knife Split Chance | `KnifeManager` **pooled and multi-instance from day one**, owning N active knives. MVP launches one; nothing may assume "the knife". |
| Ingredient Spawn Rate | `IngredientSpawner` reads target count / interval / weights from config every tick, and never caches them in `Awake`. |
| Customer Patience | `patienceDuration = base × customer × difficulty × upgrade`, resolved **once at spawn**. |
| Sous-Chef Helpers | Launching callable **without input**: `KnifeManager.Launch(Vector2 velocity, LaunchSource source)` with `LaunchSource.Manual` / `.Helper`. |

### 3.2 Required by §4 (Difficulty Scaling)

- `IDifficultyProvider` exposing `PatienceScale` and `OrderSpawnIntervalScale`. §2 ships `ConstantDifficultyProvider` (all `1.0`).
- Scaling must resolve **per order at spawn** and **per knife at launch**, so difficulty can change mid-run without retroactively rewriting live state.

### 3.3 Required by §5 (PvP)

- `IOrderQueue` as an interface; §2 ships `LocalOrderQueue`. A shared 1v1 / 2v2 queue is a second implementation.
- Emit `KnifeBounced(knifeId, bounceIndex, position)` — the concept's sabotage trigger is "achieving specific bounce combos". Track it now, consume it later.
- `IngredientSpawner` must accept an external *"force-spawn this ingredient/obstacle here"* request, so sabotage can inject hazards into an opponent's board.

---

## 4. Foundations (Phase 0 — do this first)

### 4.1 Folder & assembly layout

Assembly definitions matter here because they make the test suite runnable in isolation and prevent the "everything references everything" tangle. One runtime assembly is **D3**, with its trade-off recorded in [`README.md`](./README.md#how-the-two-sections-divide-the-work).

```
Assets/
  Scripts/
    Runtime/
      SliceNServe.Runtime.asmdef      (references: Unity.InputSystem, UnityEngine.UI)
      Core/     GameEvents, Wallet, RunClock, RuntimeTuning, ServiceRegistry, GameBootstrap,
                ViewportMath
      Board/    BoardBounds, BoardViewportAdapter, KnifeManager, KnifeController, KnifeLauncher,
                AimInput, TrajectoryPreview, IngredientSpawner, Ingredient, IngredientSlicer
      Orders/   IOrderQueue, LocalOrderQueue, OrderInstance, OrderSpawner, PatienceSystem,
                ConstantDifficultyProvider, ReputationService
      UI/       SafeAreaFitter, OrderTicketHud, OrderTicketView, PatienceBar, CashReadout
      Data/     IngredientDefinition, DishDefinition, CustomerDefinition, BoardConfig
    Tests/
      EditMode/ SliceNServe.Tests.EditMode.asmdef
      PlayMode/ SliceNServe.Tests.PlayMode.asmdef
  Prefabs/           Knife.prefab, Ingredient.prefab, OrderTicket.prefab
  ScriptableObjects/ Ingredients/, Dishes/, Customers/, BoardConfig.asset
  Art/Placeholders/  circle.png, square.png, knife.png
  Scenes/            Game.unity
```

`Core/ViewportMath.cs`, `Board/BoardViewportAdapter.cs`, `UI/SafeAreaFitter.cs` and the
`Scripts/Runtime/UI/` folder are Section 1's three additions (§1 plan §4); they are listed here so
the tree is complete in one place.

### 4.2 Namespaces

`SliceNServe.Core`, `.Board`, `.Orders`, `.UI`, `.Data`. Data types are `[CreateAssetMenu]` ScriptableObjects so designers can author them without touching code.

### 4.3 Layer scheme

Tags are unnecessary; layers plus the collision matrix carry the semantics. Add to `ProjectSettings/TagManager.asset`:

| Layer | Index | Who |
| --- | --- | --- |
| `Knife` | 8 | The knife prefab (dynamic `Rigidbody2D`) |
| `Ingredient` | 9 | Ingredient prefabs (trigger colliders) |
| `BoardWall` | 10 | Static board boundary colliders |
| `Obstacle` | 11 | Reserved: §6 themes / §5 sabotage hazards |

> Indices 0–5 are Unity's builtin slots, so user layers start at 6 at the earliest; 8–11 leaves
> room and follows convention. See **D7**.

Collision matrix — set **exactly** these pairs and untick everything else:

- `Knife ↔ BoardWall`: **on**. This is the bounce.
- `Knife ↔ Ingredient`: **off**. The knife collects by explicit sweep query (§5.5), not by collision — this keeps collision callbacks cheap and makes collection speed-proof (**D6**).
- `Ingredient ↔ everything`: **off**. Ingredients are non-physical pickups; they must never nudge the knife or each other.
- `Knife ↔ Obstacle`: **on** (reserved).

### 4.4 Scene setup

`Assets/Scenes/Game.unity`:

- `Main Camera`: Orthographic, `orthographicSize = 5.4` (⇒ 10.8 u tall, 19.2 u wide at 16:9, matching the 1920×1080 default). Linear colour space is already set. **Section 1's `BoardViewportAdapter` replaces this hardcoded value with the contain-fit policy (D8)** — which evaluates to exactly `5.4` at 16:9, so nothing here changes.
- `GameBootstrap` — the single entry point: constructs the services, wires the events, owns the run lifecycle.
- `BoardBounds` — a parent holding four static `BoxCollider2D` walls on layer `BoardWall`.
- `KnifeManager` + a pooled `Knife.prefab`, parked at the launcher.
- `IngredientSpawner` + `Ingredient.prefab`.
- `OrderSpawner` + a screen-space `Canvas` (HUD).
- A backdrop `SpriteRenderer` at `z = +1`; gameplay at `z = 0`; HUD canvas far in front.

### 4.5 Physics 2D settings and materials

| Setting | From | To | Why |
| --- | --- | --- | --- |
| Default Gravity | `(0, -9.81)` | **`(0, 0)`** | Top-down ping-pong board (**D10**). Keep `Rigidbody2D.gravityScale = 0` per body as belt-and-braces so a stray gravity change cannot break the board. |
| Max Translation Speed | `100` | `200` | A hard global clamp on body speed; the 40 u/s cap must never be silently clipped, and the clamp interacts badly with CCD. |
| Velocity Threshold | `1` | keep `1` | Bounces below 1 u/s are treated as inelastic. The minimum live speed is 3 u/s, so this never bites. |
| Simulation Mode | Fixed Update | keep | Determinism and stable 50 Hz stepping. |
| Velocity / Position Iterations | `8` / `3` | keep | Raise position iterations to `4` only if stacking artefacts appear. |
| Use Multithreading (Job Options) | off | leave off | Revisit past ~200 ingredients. |
| Default Material | none | leave none | Materials are assigned per-collider for explicit control. |

Create two `PhysicsMaterial2D` assets, each `friction = 0`, `bounciness = 1`:
`Assets/Settings/KnifeMaterial.physicsMaterial2D` and `Assets/Settings/WallMaterial.physicsMaterial2D`.

With friction 0 and bounciness 1 on *both* sides the reflection is a clean mirror, so energy loss is
entirely under our explicit control in code (§5.3) rather than an emergent property of Unity's
material mixing. Confirm the mixing rule in the Editor once (Unity/Box2D takes the higher
bounciness and the geometric mean of friction; the settings above make it moot).

### 4.6 Tunneling — the single biggest technical risk

At `Fixed Timestep = 0.02` and a 40 u/s cap, the knife travels **0.8 world units per physics step**.
Any collider thinner than that can be passed through in one step; a 0.5 u wall *will* leak.

Three defences, all of them:

1. **Thick wall colliders.** Visual walls may be 0.25 u; the `BoxCollider2D` on `BoardWall` is **2.0 u thick**, offset outward so its *inner face* sits exactly on the play-area boundary — 2.5× the worst-case per-step travel.
2. **CCD on the moving body.** `KnifeController` sets `collisionDetectionMode = CollisionDetectionMode2D.Continuous`. Continuous mode sweeps against **static** colliders, which is exactly the wall case. `Discrete` would tunnel; `ContinuousDynamic` is only needed once moving obstacles exist (§5/§6) and costs more.
3. **Swept collection query for ingredients.** CCD does **not** apply to sensor/trigger overlaps, so a fast knife could pass an ingredient trigger between steps. Collection therefore uses an explicit sweep (§5.5).

Also set `interpolation = RigidbodyInterpolation2D.Interpolate`, so the knife renders smoothly between 50 Hz steps on a 60 FPS display.

### 4.7 Placeholder art

§2 must not block on art. Commit three 64 × 64 PNGs (white circle, white square, knife silhouette) to
`Assets/Art/Placeholders/`, imported as `Sprite Mode: Single`, tinted per ingredient via
`SpriteRenderer.color`. Replace with real art later without touching code. Decide the Git LFS policy
before importing anything binary — `.gitattributes` defines an `lfs` attribute but no path uses it.

---

## 5. §2.1 — Ingredient Gathering (The Board)

### 5.1 Board model

- The board is a closed rectangle: four seamless walls, **no internal corners** in MVP (internal geometry is a §6 theme concern).
- Play area (`BoardConfig.worldBounds`): `Rect(-9.6, -5.4, 19.2, 10.8)` (**D4**). The `BoardWall` collider inner faces sit exactly on this rectangle.
- Ingredient zone (`BoardConfig.ingredientZone`): `Rect(-9.1, -3.3, 18.2, 8.2)` — inset 0.5 u left, right and top, and **2.1 u at the bottom**, reserving a clean launch corridor so no ingredient can spawn on top of the launcher.
- Launcher anchor (`BoardConfig.launcherPosition`): bottom-centre `(0, -4.4)`. Clearance to the bottom wall is 1.0 u (§5.6) and the nearest possible ingredient edge is 0.6 u away, so a fresh knife never starts overlapping a pickup.

### 5.2 Launch: drag-and-release aiming

#### 5.2.1 Input wiring

The Input System has **no built-in `Drag` or `Swipe` interaction** — 1.19 ships only Default, Press,
Hold, Tap, SlowTap and MultiTap ([interactions namespace](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/api/UnityEngine.InputSystem.Interactions.html)).
Drag-and-release is therefore **two actions plus manual state tracking**, which is also the most
portable approach:

Add a `Board` action map to `Assets/InputSystem_Actions.inputactions`:

| Action | Type | Bindings | Interaction |
| --- | --- | --- | --- |
| `Aim` | Value / Vector2 | `<Pointer>/position`, `<Touchscreen>/primaryTouch/position`, `<Pen>/position` | none — polled every frame |
| `Launch` | Button | `<Mouse>/leftButton`, `<Touchscreen>/primaryTouch/press`, `<Pen>/tip` | `Press` (`PressAndRelease`) |

`AimInput` runs a small state machine over them:
`Idle → (Launch.started) Dragging → (Launch.canceled) Release`, firing `OnLaunch(Vector2 dragStart, Vector2 dragEnd)`.

Because the pointer position is polled continuously, mouse, touch and pen share one code path — no
`#if UNITY_IOS` branches. Binding only `primaryTouch` is the multi-touch guard; it is an *input-shape*
guard, never a gameplay one (**D9**).

#### 5.2.2 Drag-to-velocity mapping

```
dragVector = dragEnd - dragStart                                     // world units, y-up
power      = Mathf.Clamp01(dragVector.magnitude / maxDragLength)     // maxDragLength = 3.5 u
launchDir  = -dragVector.normalized                                  // pull-back slingshot (D12)
speed      = Mathf.Lerp(launchMinSpeed, launchMaxSpeed, powerEase(power))   // 8 → 22 u/s
velocity   = launchDir * speed
```

`powerEase` starts linear and is a designer knob once feel is testable. A dead-zone of `0.35 u` means
a tap or a short drag launches nothing. The launch angle is clamped so it is never within 4° of
parallel to a wall (§5.6).

Three deliberately distinct speed values:

| Name | Value | Meaning |
| --- | --- | --- |
| `launchMinSpeed` / `launchMaxSpeed` | 8 / 22 u/s | The **design envelope** a player can produce. At 22 u/s the knife travels 0.44 u per 0.02 s step — well inside the 2.0 u wall thickness, even before CCD. |
| `speedHardCap` | 40 u/s | A **physics guard** applied after every bounce (§5.3). Unreachable from a single launch; it exists so a future §3 multiplier or §5 sabotage cannot push a body fast enough to defeat CCD. |
| `minLiveSpeed` | 3 u/s | Below this the knife is spent and returns (§5.3). |

#### 5.2.3 Trajectory preview

`TrajectoryPreview` draws a `LineRenderer` showing the first **2** bounces while dragging:

- Pure geometric prediction: `Physics2D.Raycast` with `layerMask` = `BoardWall` only, up to N reflections, `direction = Vector2.Reflect(direction, hit.normal)`.
- This is a *preview*, not a simulation: it must not call `Physics2D.Simulate`, and it deliberately ignores ingredients so it never lies about what the knife will hit. Say so in the tooltip, so nobody "fixes" it into a physics prediction.
- Fade with distance; hide on release.

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

`Launch` takes a **velocity, not a drag**, so §3's Sous-Chef helpers can call it with a computed aim
vector and `LaunchSource.Helper` with zero input involvement.

### 5.3 Bounce, energy and lifetime

| Part | Setup |
| --- | --- |
| Root | `Rigidbody2D` — `Dynamic`, `gravityScale = 0`, `linearDamping = 0`, `angularDamping = 0.4` (spin is cosmetic), `collisionDetectionMode = Continuous`, `interpolation = Interpolate`, layer `Knife`, material `KnifeMaterial`. |
| Collider | **`CircleCollider2D`**, radius `0.18`. A circle, not a capsule, **on purpose**: a bouncing body needs a rotation-independent silhouette, or spin makes the rebound unpredictable and the physics stop being learnable. |
| Visual | A `SpriteRenderer` on a **separate child** that spins freely, so the spin animation never touches the collider. |
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

The `0.08` test is applied to the **outgoing** velocity, which is exactly the degenerate grazing case:
the reflection is already near-tangent, so a few degrees breaks the loop without visibly kinking the
trajectory.

Termination — the knife ends its life when **any** of:

| Condition | Value | Notes |
| --- | --- | --- |
| Speed below `minLiveSpeed` | `< 3 u/s` | Checked in `FixedUpdate`. |
| Lifetime exceeded | `> 15 s` | Hard anti-stall cap. |
| Bounce cap | `> 60` | Anti-degenerate-loop cap. |

On termination `KnifeManager` returns the knife to the launcher and fires `KnifeReturned`, setting
`CanLaunch = true`. `ReturnAllKnives()` exists for §3's wave clear and §5's round reset.

With `bounceRetention = 0.90` a full-power launch decays from 22 → 3 u/s in `ln(3/22)/ln(0.9) ≈ 19`
bounces; with losses that is roughly 13–15 s of life. One satisfying flurry, and every number is a
`BoardConfig` field.

### 5.4 Ingredients

`IngredientDefinition` (ScriptableObject):

| Field | Type | Purpose |
| --- | --- | --- |
| `id` | `string` | Stable key for recipes and save data. Never the asset name. |
| `displayName` | `string` | UI. |
| `sprite`, `tint` | `Sprite`, `Color` | Placeholder-friendly rendering. |
| `radius` | `float` | Default `0.35`. Collider size and spawn separation. |
| `spawnWeight` | `float` | Weighted-random weight. `0` ⇒ never spawns naturally (roadmap-only ingredients). |
| `slicesOnHit` | `bool` | Reserved: `false` knocks the knife aside (obstacle behaviour, §6). Path stubbed, unused in MVP. |

`Ingredient` prefab: `CircleCollider2D` with `isTrigger = true`, layer `Ingredient`, **no `Rigidbody2D`**,
plus a `SpriteRenderer` **child** that bobs. The collider deliberately never moves — a static collider
that moves forces broadphase rebuilds every frame, and a `Rigidbody2D` would buy nothing when nothing
can push anything. This is the cheapest arrangement that still gives the swept cast something to hit.

`IngredientSpawner`:

- Reads `targetCount = 12`, `respawnInterval = 0.6 s`, `maxCount = 18`, `minSeparation = 0.6 u` and the spawn weights from config **every tick** — never cached in `Awake`, so §3's spawn-rate upgrade is a config write (§3.1).
- Each tick: if `liveCount < targetCount`, spawn up to `maxSpawnsPerTick` (default 2).
- Placement: up to 8 rejection-sampling attempts at a random point in `ingredientZone` inset by `radius`, validated with `Physics2D.OverlapCircle(pos, radius + minSeparation, ingredientMask)` **before** instantiating the candidate. Failing all 8, defer to the next tick rather than force-placing — force-placing is exactly how you get stacks.
  (`Physics2D.queriesStartInColliders` is `1` here, but that only matters when a query filter includes the *querying object's own* layer. This filter is `Ingredient` alone and the candidate does not exist yet, so it is a non-issue — as it is for the knife's sweep in §5.5, for the same reason. It becomes an issue only if a filter is widened to include `Knife`.)
- Despawn: uncollected ingredients expire after `lifetime = 12 s` with a 1 s fade, so the board self-refreshes and a scarce ingredient can never become permanently unobtainable.

### 5.5 Collection — swept, speed-proof

The knife collects, because the matrix deliberately does not connect it to ingredients (**D6**). Per
`FixedUpdate`, over the segment it is about to travel:

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

`Rigidbody2D.Cast` sweeps the knife's own colliders along the motion vector, so an ingredient anywhere
on the step's path is found regardless of speed. That removes tunneling from the collection path
entirely, and it is **testable headlessly** (§10) in a way trigger callbacks are not.

Implementation notes: `_results` is a preallocated `RaycastHit2D[]`; dedupe within the step with a
preallocated set keyed on the collider's instance id; iterate with `for` over arrays, never
`foreach` over a `List<T>`.

On each collected ingredient, in order:

1. Publish `IngredientSliced(ingredientId, position)`.
2. Increment the combo counter (reset per launch) — §5's sabotage telemetry.
3. Release the ingredient to its pool with a slice VFX + SFX.
4. Forward to the order system: `IOrderQueue.SubmitIngredient(ingredientId, 1)`.

Collection must **never** mutate the knife's velocity. Ingredients are rewards, not obstacles — that
keeps the skill expression purely about wall geometry and launch angle, and keeps §3's balance maths
linear.

### 5.6 Anti-stall rules

These will bite. Make them explicit.

- **Angle clamp.** Keep the launch direction at least 4° off parallel to any wall, so the first contact is never a graze.
- **Clear spawn.** The launcher keeps 1.0 u to the nearest wall and 0.6 u to the nearest possible ingredient edge, so a fresh knife never starts overlapping anything.
- **Outside-the-board failsafe.** If the knife ever ends up outside `worldBounds` (a physics failure), `KnifeController` detects it in `FixedUpdate` and force-returns it. A safety net, not a mechanism.
- **Single live knife.** `KnifeManager` refuses `Launch` while `!CanLaunch`, so a double-tap cannot spawn two knives before §3's split upgrade exists.
- **Zero-velocity guard.** If `linearVelocity` is ~zero, skip the cast — a zero-length cast direction silently returns nothing.

---

## 6. §2.2 — Fulfilling Orders & Customer Patience

### 6.1 Data model

**`DishDefinition`** — a recipe.

| Field | Type | Notes |
| --- | --- | --- |
| `id`, `displayName`, `sprite` | | UI and stable key. |
| `ingredients` | `List<IngredientRequirement>` | `{ IngredientDefinition ingredient; int count; }` — a dish may need 2 tomatoes. The *authored* shape; the runtime mirror with a mutable counter is `OrderLine`. |
| `baseCash` | `int` | Reward before §3 multipliers. |
| `baseOrderWeight` | `float` | Relative draw weight. |

**`CustomerDefinition`** — an archetype, and the extension point for §6's VIP / "boss" customers.

| Field | Type | Notes |
| --- | --- | --- |
| `id`, `displayName`, `spriteSet` | | |
| `patienceMultiplier` | `float` | Patient `1.4`, average `1.0`, impatient `0.6`. |
| `tipMultiplier` | `float` | Reserved for §3. |
| `orderCount` | `int` | Dishes this customer wants (MVP: `1`). |

**`BoardConfig`** — one asset holding every number §5 and §6 reference, so a designer can retune the
whole board in one place.

### 6.2 Order generation

`OrderSpawner`:

- Target `maxActiveOrders = 4` concurrent; spawn every `6 s` × `difficultyProvider.OrderSpawnIntervalScale`.
- Draw a `CustomerDefinition` by weight, then a `DishDefinition` by weight, then build the order.
- **Fairness bias:** weight each dish by `baseOrderWeight / (1 + missingIngredientPenalty)`, where the penalty reflects how thinly the dish's ingredients are represented on the live board. Without it, the RNG can hand out three dishes needing an ingredient the spawn table rarely produces, and the run dies to bad luck rather than bad play. Keep it a small, tunable bias — never a hard rule.

`OrderInstance` — plain C#, **not** a MonoBehaviour. That is what makes the whole of §2.2 unit-testable
without a scene.

```
Guid orderId; DishDefinition dish; CustomerDefinition customer;
OrderLine[] lines;            // { IngredientDefinition ingredient; int required; int fulfilled; }
float spawnTime; float patienceDuration; float remainingPatience;
PatienceState state;          // Normal | Hurry | Critical | Expired | Served
bool IsComplete => every line fulfilled >= required
```

### 6.3 Auto-credit rule

`LocalOrderQueue.SubmitIngredient(ingredientId, count)` — the concept doc's "ingredients automatically
contribute to completing their dishes":

1. Candidates = active orders with a line for `ingredientId` where `fulfilled < required`.
2. Target = the candidate with the **earliest `spawnTime`** (oldest customer first); tie-break on **fewest remaining units overall** (finish nearly-done dishes first).
3. Credit one unit. If the order `IsComplete` → serve it (§6.6).
4. No candidate ⇒ fire `IngredientWasted(ingredientId, position)` for feedback (a grey puff and a soft thud) and discard (**D11**).

This is deliberately a pure function over a list of orders — the highest-value thing in the whole
section to unit-test.

### 6.4 Patience

Resolved **once, at spawn**, so a later tuning change never retroactively rewrites a live customer —
required by §4 and for PvP fairness:

```
patienceDuration = basePatienceSeconds               // BoardConfig, default 25 s
                 * customer.patienceMultiplier        // CustomerDefinition
                 * difficultyProvider.PatienceScale   // §4 seam, 1.0 in §2
                 * tuning.patienceUpgradeMultiplier   // §3 seam, 1.0 in §2
```

Depletion is linear — `remainingPatience -= delta` — with a pure `PatienceSystem.Tick(order, delta)`
doing the arithmetic so it is testable without a frame loop.

| State | Remaining | Feedback |
| --- | --- | --- |
| `Normal` | > 60 % | Green bar, occasional idle animation. |
| `Hurry` | 30–60 % | Amber bar, faster pulse, first "impatient" animation trigger. |
| `Critical` | < 30 % | Red bar, shaking, ticking SFX, bar pulsing in sync. |
| `Expired` | 0 | See §6.6. |

### 6.5 Views

**`OrderTicketView`** — screen-space uGUI, one per active order, left→right in spawn order: dish icon,
ingredient checklist with `fulfilled/required` and a checkmark per satisfied line, customer portrait,
and the patience bar. Bind with **plain C# events** from the services, never per-frame polling — the
service layer must be usable with zero UI present, which is what the tests do.

uGUI is the choice (**D15**) because `com.unity.ugui 2.0.0` is present, world-space bars are trivial,
and tickets are a natural prefab. In Unity 6 TextMeshPro ships inside `com.unity.ugui`, so
`Window → TextMeshPro → Import TMP Essential Resources` is a one-time setup step.

`CustomerView` (world-space) — a sprite plus an animation state machine
(`Idle / Impatient / Critical / Angry / Happy`), with no art dependency in §2. MVP renders patience
**only** on the ticket (**D14**); a world-space bar is optional polish.

### 6.6 Serving and failing

On completion:

1. Mark `Served`; stop depleting.
2. `wallet.Add(cash)` where cash = `dish.baseCash × customer.tipMultiplier × tuning.cashMultiplier`. `Wallet` is a **minimal stub** (`int Amount`, `event Action<int> Changed`) that §3 grows into the real economy; §2 only needs the value and its change event to prove the loop closes.
3. Fire `OrderServed(orderId, cashEarned)` → the customer reacts, the ticket animates out and frees its slot.

On expiry:

1. Mark `Expired`; the order leaves the queue, freeing a slot.
2. Fire `OrderExpired(orderId, customerId)`.
3. `ReputationService` decrements a run-level counter (`Reputation`, default `3`) and fires an event. At 0, stop spawning and flag game-over — **no game-over screen in §2** (**D13**).

The concept doc offers "lost revenue **or** lost lives/reputation". Reputation is chosen because it is
legible, visible on the HUD, and is where §3's upgrades acquire their value.

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

Every arrow is a plain C# event or a direct call on an interface. The only container is the single
`ServiceRegistry` created by `GameBootstrap`; no DI framework — adding one is a §3+ decision, not a §2
need.

---

## 8. Implementation phases

Ordered so each phase is independently runnable and verifiable, and each ends playable — never
mid-refactor. The runbook's steps map one-to-one onto these.

| # | Phase | Deliverable | Acceptance criteria |
| --- | --- | --- | --- |
| 0 | **Foundations** | Folders, asmdefs, layers, `Physics2D` settings, materials, `Game.unity` with camera + walls, placeholder art | Play mode shows an empty walled board; an EditMode test assembly compiles and runs. |
| 1 | **Launch** | `Board` action map, `AimInput`, `TrajectoryPreview`, `KnifeManager.Launch` | Mouse and touch drag-and-release both launch the knife; the preview shows 2 bounces and matches the actual first bounce; a tap does nothing; launching while a knife is live is refused. |
| 2 | **Bounce & lifetime** | `KnifeController` CCD, materials, retention, anti-stall, return | A 22 u/s launch never escapes the board over a 20 s headless simulation; the knife always terminates and `CanLaunch` returns true; the bounce counter is correct. |
| 3 | **Ingredients** | `IngredientDefinition`, `IngredientSpawner`, `Ingredient`, swept collection, VFX/SFX hooks | The board maintains ~12 ingredients; a knife passing an ingredient at max speed always collects it (headless test); ingredients never deflect the knife; uncollected ones expire. |
| 4 | **Orders & patience** | `DishDefinition` / `CustomerDefinition`, `LocalOrderQueue`, `PatienceSystem`, `OrderTicketView` | Submitting ingredients credits the oldest demanding order; tickets update live; patience drains and bands change at 60 %/30 %; expiry removes the order and decrements reputation. |
| 5 | **Slice glue & polish** | `Wallet`, `ReputationService`, HUD, run start/stop, difficulty seams, tests | The §1 done-criteria loop plays end to end; `ConstantDifficultyProvider` is wired; the full EditMode + PlayMode suites are green. |

---

## 9. Tuning table

Starting values, all in `BoardConfig` or data assets. Nothing here is a code constant.

| Parameter | Start | Raise it if… | Lower it if… |
| --- | --- | --- | --- |
| Board size | 19.2 × 10.8 u | — | gameplay feels slow |
| Launcher position | `(0, -4.4)` | ingredients feel unreachable | the knife has no room |
| `launchMinSpeed` / `launchMaxSpeed` | 8 / 22 u/s | bounces feel mushy | the knife is uncontrollable |
| `speedHardCap` | 40 u/s | — | — |
| `maxDragLength` | 3.5 u | mobile aim feels cramped | aim feels imprecise |
| Dead-zone | 0.35 u | accidental launches | taps feel ignored |
| `bounceRetention` | 0.90 | the knife dies too fast | the knife never ends (±0.02 steps) |
| `minLiveSpeed` / lifetime / bounce cap | 3 u/s / 15 s / 60 | — | the knife lingers |
| Ingredient target / max / interval | 12 / 18 / 0.6 s | the board looks empty | the board is cluttered |
| Ingredient radius / min separation / lifetime | 0.35 / 0.6 / 12 s | collection is fiddly | spawns fail often |
| `maxActiveOrders` | 4 | the HUD has room and difficulty is low | the HUD is crowded |
| Order spawn interval | 6 s | — | the board can't keep up |
| `basePatience` | 25 s | too punishing | too easy |
| Multi-ingredient dish share | ≤ 40 % for the first 2 min | — | early game is unfair |
| `Reputation` start | 3 | forgiving | tense |

---

## 10. Test strategy

`com.unity.test-framework 1.6.0` is installed, so verification does **not** require a human in the
Editor. Two tiers.

**EditMode — pure logic, no scene, fast.** The bulk of the value, because the order and patience
systems are deliberately plain C# objects.

| Fixture | Asserts |
| --- | --- |
| `OrderCreditTests` | oldest-demander targeting; tie-break on fewest remaining; multi-unit recipes; no-match ⇒ exactly one `IngredientWasted`; a served order is never credited again; a completed order frees its slot. |
| `PatienceTests` | `base × customer × difficulty × upgrade`; band transitions at exactly 60 % and 30 %; expiry at exactly 0; an expired order stops depleting and never serves. |
| `DifficultyTests` | `ConstantDifficultyProvider` returns 1.0; a fake provider provably changes `patienceDuration` at spawn time and **not** for already-spawned orders. |
| `SpawnTableTests` | seeded RNG produces the expected weighted distribution within tolerance; `spawnWeight = 0` never appears; the fairness bias reduces the "unreachable ingredient" rate. |
| `AimMathTests` | drag length → speed mapping and clamping; dead-zone; slingshot direction; the parallel-to-wall clamp. |
| `ViewportMathTests` | the §1 aspect table (Section 1's fixture, listed here because both sections run in one suite). |

**PlayMode — physics, deterministically stepped.** A dedicated scene with
`Physics2D.simulationMode = SimulationMode2D.Script` and manual `physicsScene.Simulate(0.02f)`
stepping, so results are editor-independent and fast — and a future, non-flaky home for CI.

| Test | Asserts |
| --- | --- |
| Tunneling guard | a launch at 40 u/s (above the design cap, on purpose) over 1,000 steps never leaves `worldBounds` by more than a collider radius. |
| Collection at speed | an ingredient trigger on a 40 u/s path is collected **in the single step that crosses it** — the test that fails the moment somebody replaces the swept cast with `OnTriggerEnter2D`. |
| Energy maths | the speed after N synthetic bounces equals `v × 0.90^N` within epsilon. |
| Anti-stall | a launched knife always reaches a terminal state within `lifetime + 1 s` of simulated time. |
| No-GC steady state | no GC allocation across a scripted 5 s run, enforcing the §1 budget. |

> Assert **invariants and bounds, never exact trajectories.** Box2D is deterministic for a given
> binary and platform, not universally, so exact-position assertions are how this suite rots.

Headless run:

```
"C:\Program Files\Unity\Hub\Editor\6000.3.16f1\Editor\Unity.exe" ^
  -batchmode -nographics -projectPath "<repo>" ^
  -runTests -testPlatform EditMode -testResults "%TEMP%\editmode.xml" -logFile -
```

**Manual checks** for the parts that are genuinely about feel, done in the Editor by a human:

1. Mouse drag from the launcher → the preview appears and matches the real first bounce.
2. The same drag on the Device Simulator's touch path — the aim is not occluded by the finger.
3. A full-power launch lasts ~10–15 s and never gets stuck on a wall or in a loop.
4. Ingredient slices feel responsive: nothing is ever visually passed through.
5. A ticket's patience bar, its band colour changes, and the customer's reaction agree with what actually happened.

---

## 11. Decisions

The rationale for the decisions this section owns. The decisions themselves are in
[`README.md`](./README.md#locked-decisions); nothing is decided here.

| # | Question | Choice, and why |
| --- | --- | --- |
| **D10** | Board gravity | **Zero-g.** The doc says both "ping-pong" and "pinball"; zero-g is chosen because aiming must be *predictable*, and the drag-and-release scheme depends on that. It also removes a whole class of device-dependent variance. *Alternative: gravity ≈ −0.6 × gravityScale for an arcing, Peggle-like feel — one asset edit away, which is why `gravityScale` stays a config field.* |
| **D11** | Ingredient overflow | **Discard.** The doc's "automatically contribute to completing their dishes" is imperative, so there is no player choice to make. A pantry adds strategy and reduces frustration but complicates both the credit rule and the UI. *Revisit in §3.* |
| **D12** | Aim direction | **Pull-back slingshot.** It keeps the finger off what you are aiming at, which matters most on mobile. Both conventions are ~10 lines, but pick one now so tuning and tutorials are written against it. |
| **D13** | Failure mode | **Reputation**, 3 lives. The doc offers lost revenue *or* reputation; reputation is legible, visible on the HUD, and gives §3's upgrades something to protect. |
| **D14** | Patience bar location | **Ticket only.** It satisfies "each customer has a visible patience bar"; a world-space bar is polish, not a requirement. |
| **D15** | UI stack | **uGUI.** Present in the project, prefab-able tickets, trivial world-space bars. The escape hatch is that the service layer must run with zero UI present, so a later swap is confined to one folder. |

### Open questions, not blockers

- Should a returned knife auto-relaunch once §3's helpers exist, or always wait for input? (MVP: always wait — `LaunchSource` already distinguishes the paths.)
- Should ingredient despawn pressure tighten over time in §2, or is that strictly §4? (This plan assumes strictly §4, via `IDifficultyProvider`.)
- Is a pantry / order UI affordance needed for dishes with 3+ ingredient types at 1920×1080 with 4 concurrent orders? Resolve with a layout spike in Phase 4, before building the final ticket prefab.
- O3 (frame-rate cap) is still open — see [`README.md`](./README.md#open-decisions).

---

## 12. Verify in the Editor before relying on it

Flagged honestly, because none of it could be verified from the repository alone:

1. **`PhysicsMaterial2D` mixing** — that bounciness 1 on both surfaces yields a fully elastic bounce in this version. Mitigation: energy decay is applied explicitly in code, so feel stays controllable either way.
2. **`Rigidbody2D.Cast` with `ContactFilter2D.useTriggers = true`** — that it returns trigger colliders on the `Ingredient` layer from a `Knife`-layer body. If it does not, fall back to `Collider2D.Cast` on the knife's own collider.
3. **`PhysicsScene2D.Simulate`** in EditMode tests (whether a `Rigidbody2D` in a scene created via `SceneManager.CreateScene` is picked up). If not, move the physics tests to PlayMode.
4. **Whether `CollisionDetectionMode2D.Continuous` alone is sufficient** at 40 u/s in this build, or whether the 2.0 u walls are also load-bearing. The tunneling test in §10 answers this empirically — that is why it launches *above* the design cap.

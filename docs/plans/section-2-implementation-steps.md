# Section 2 — Core Mechanics — Implementation Runbook

> Plan: [`section-2-core-mechanics.md`](./section-2-core-mechanics.md). This file is the *how*.
>
> **Read this first.** Every step is meant to be performed by hand in the Unity Editor or a
> shell. Nothing here has been applied to the project yet. Steps are ordered so that each one
> leaves the project in a compiling, playable state — if a step breaks something, the cause is
> in that step.
>
> The steps follow the plan's §8 phases one-to-one: Steps 1–6 are **Phase 0 (Foundations)**,
> Steps 7–8 are **Phases 1–3**, Steps 9–10 are **Phases 4–5**, Step 11 is the test suite, and
> Step 12 verifies and commits.
>
> Unity Editor version: **6000.3.16f1**. Paths use `Project Settings → …` for Editor menus and
> `/` for asset paths. **The code below compiles as given** — type it in as-is rather than
> paraphrasing it.
>
> This runbook owns the foundation that Section 1 builds on, so it may be executed **before or
> after** [`section-1-implementation-steps.md`](./section-1-implementation-steps.md). The only
> shared artifact is `BoardConfig.worldBounds`; Step 4 extends that class rather than creating a
> second config type.

---

## Step 0 — Preflight

**Goal:** know the tooling and the working tree before changing anything.

1. Confirm the Editor version matches `ProjectSettings/ProjectVersion.txt` → `6000.3.16f1`.
   Opening the project with a different version silently rewrites `ProjectSettings/`, which
   produces a large, meaningless diff.
2. Confirm `ProjectSettings/Physics2DSettings.asset` currently reads `m_Gravity: {x: 0, y: -9.81}`,
   `m_MaxTranslationSpeed: 100`, `m_VelocityThreshold: 1` — Step 2 deliberately changes some of
   these, and you want to know you started from the template values.
3. Confirm `ProjectSettings/TagManager.asset` shows indices 0–7 as Unity's builtin slots
   (`Default`, `TransparentFX`, `Ignore Raycast`, `Water`, `UI`, plus three unnamed). User layers
   start at 8 — that is why the plan's §4.3 uses 8–11.
4. The working tree has uncommitted template-import changes. **Commit them as their own commit
   first**, so that every later diff contains only real work:

```bash
git add -A
git commit -m "Import URP 2D template baseline (Unity 6000.3.16f1)"
```

> If `section-1-implementation-steps.md`'s Step 0 has already done this, skip it — do not create
> an empty commit.

**Verify:** `git status --short` is empty.

---

## Step 1 — Project skeleton: folders and assemblies

**Goal:** the folder and assembly structure every later step drops code into. (Phase 0)

1. Create the tree. Unity does not track empty folders, so create the `.gitkeep` files too:

```bash
mkdir -p Assets/Scripts/Runtime/{Core,Board,Orders,Data}
mkdir -p Assets/Scripts/Tests/{EditMode,PlayMode}
mkdir -p Assets/Prefabs Assets/ScriptableObjects/{Ingredients,Dishes,Customers}
mkdir -p Assets/Art/Placeholders Assets/Scenes
```

2. `Project Settings → Editor` → set **Root Namespace** to `SliceNServe`, **Serialization Mode**
   to `Force Text`, **Version Control Mode** to `Visible Meta Files`. Leave *Enter Play Mode
   Options* off.
3. Create `Assets/Scripts/Runtime/SliceNServe.Runtime.asmdef` (right-click → Create → Assembly
   Definition; name it `SliceNServe.Runtime`):

```json
{
    "name": "SliceNServe.Runtime",
    "rootNamespace": "SliceNServe",
    "references": [
        "Unity.InputSystem",
        "UnityEngine.UI"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

4. Create `Assets/Scripts/Tests/EditMode/SliceNServe.Tests.EditMode.asmdef` and
   `Assets/Scripts/Tests/PlayMode/SliceNServe.Tests.PlayMode.asmdef`:

```json
{
    "name": "SliceNServe.Tests.EditMode",
    "rootNamespace": "SliceNServe.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "SliceNServe.Runtime"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

```json
{
    "name": "SliceNServe.Tests.PlayMode",
    "rootNamespace": "SliceNServe.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "SliceNServe.Runtime"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

5. Namespaces are `SliceNServe.Core`, `SliceNServe.Board`, `SliceNServe.Orders`, `SliceNServe.Data`
   (plan §4.2). The single runtime assembly is a deliberate trade-off recorded in the plan: the
   namespace split is a convention, not a compiler-enforced boundary.

**Verify:** the Console is clean after a recompile, and *Window → General → Test Runner* shows
both test assemblies with zero tests.

---

## Step 2 — Layers, collision matrix, physics settings, materials

**Goal:** the physics seams §2 plugs into, fixed once. (Phase 0, plan §4.3–4.6)

1. `Project Settings → Tags and Layers → Layers` → add user layers, starting at slot 8:

| Slot | Layer |
| --- | --- |
| 8 | `Knife` |
| 9 | `Ingredient` |
| 10 | `BoardWall` |
| 11 | `Obstacle` |

2. `Project Settings → Physics 2D → Layer Collision Matrix` → leave **exactly** these pairs
   enabled and uncheck everything else:

| Pair | Enabled | Why |
| --- | --- | --- |
| `Knife` ↔ `BoardWall` | ✔ | this is the bounce |
| `Knife` ↔ `Obstacle` | ✔ | reserved for §5 sabotage / §6 themes |
| `Knife` ↔ `Ingredient` | ✘ | **the knife collects by sweep query, not by collision** (plan §4.3, §5.5) |
| `Ingredient` ↔ anything | ✘ | ingredients are non-physical pickups; they must never nudge the knife or each other |
| `Knife` ↔ `Knife` | ✘ | pooled knives never touch |

> The `Knife ↔ Ingredient` pair being **off** is load-bearing, not cosmetic. It is what keeps the
> knife's trajectory pure geometry, and Step 8's collection path assumes it. If someone turns it
> on later, the knife will physically deflect off pickups and the feel will change with speed.

3. `Project Settings → Physics 2D`:

| Setting | From | To | Why |
| --- | --- | --- | --- |
| Gravity | `(0, -9.81)` | **(0, 0)** | top-down ping-pong board (plan decision D1) |
| Max Translation Speed | `100` | `200` | it is a hard global clamp on body speed; the 40 u/s cap must never be silently clipped |
| Velocity Threshold | `1` | keep `1` | bounces below 1 u/s are inelastic; the minimum live speed is 3 u/s so this never bites |
| Simulation Mode | Fixed Update | keep | determinism and stable 50 Hz stepping |
| Velocity / Position Iterations | `8` / `3` | keep | raise position iterations to `4` only if stacking artifacts appear |
| Use Multithreading (Job Options) | off | leave off | revisit if ingredient count exceeds ~200 |
| Default Material | none | leave none | materials are assigned per-collider for explicit control |

4. `Project Settings → Time` → **leave `Fixed Timestep` at `0.02` (50 Hz)**. This is deliberately
   *not* changed to 1/60: the plan's tunneling budget (§4.6) is computed against 0.02, and the
   reconciled Section 1 no longer overrides it.
5. Create two `PhysicsMaterial2D` assets (right-click → Create → 2D → Physics Material 2D):
   - `Assets/Settings/KnifeMaterial.physicsMaterial2D` — `Friction 0`, `Bounciness 1`
   - `Assets/Settings/WallMaterial.physicsMaterial2D` — `Friction 0`, `Bounciness 1`

   With friction 0 and bounciness 1 on both sides the reflection is a clean mirror, so **all**
   energy loss is explicit and under our control in code (Step 7) instead of being an emergent
   property of Unity's material mixing.

**Verify:** the matrix shows only the two ticked pairs; Gravity reads `(0, 0)`; Time reads `0.02`;
both materials exist with Friction 0 / Bounciness 1.

---

## Step 3 — Placeholder art

**Goal:** §2 must not block on art. (Phase 0, plan §4.7)

1. Add three 64 × 64 PNGs (white on transparent is fine — they are tinted in code):
   - `Assets/Art/Placeholders/circle.png`
   - `Assets/Art/Placeholders/square.png`
   - `Assets/Art/Placeholders/knife.png`
2. Select each and set **Sprite Mode `Single`**, **Pixels Per Unit `100`**, then **Apply**.
   Leave Mesh Type at `Tight` — nothing in this runbook uses `SpriteDrawMode.Sliced`, which is
   the only thing that would require `Full Rect`.

**Verify:** all three import with no Console warnings and appear in the Project window as sprites.

---

## Step 4 — `BoardConfig` and `RuntimeTuning`

**Goal:** every balance number lives in one asset, and every system reads it through a
runtime-mutable indirection so §3's upgrades become asset writes rather than code changes.
(Phase 0 + the §3 seam from plan §3.1)

1. Create `Assets/Scripts/Runtime/Data/BoardConfig.cs`.

> **If Section 1's runbook ran first**, this file already exists with only `worldBounds` in it.
> **Extend that file** — do not create a second config type. The namespace (`SliceNServe.Data`),
> the class name and the `worldBounds` field name are all already fixed by Section 1 and by
> `BoardViewportAdapter`; changing any of them breaks the camera adapter.

```csharp
using UnityEngine;

namespace SliceNServe.Data
{
    /// <summary>
    /// Every balance number Section 2 references, in one asset, so a designer can retune the
    /// whole board without touching code. Section 1 reads <see cref="worldBounds"/> for the
    /// camera; Section 2 reads the rest; Sections 3 and 4 retune it at runtime through
    /// <c>RuntimeTuning</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Slice & Serve/Board Config", fileName = "BoardConfig")]
    public sealed class BoardConfig : ScriptableObject
    {
        [Header("Board (plan §5.1)")]
        [Tooltip("Play area in world units, centred on the origin. Section 1's camera adapter reads this.")]
        public Rect worldBounds = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);

        [Tooltip("Where a new knife starts. Must keep ~1 u of clearance to the nearest wall (§5.6).")]
        public Vector2 launcherPosition = new Vector2(0f, -4.4f);

        [Tooltip("Where ingredients may spawn: the board minus edge margins, minus a reserved "
               + "launch corridor at the bottom, so nothing can spawn on top of the launcher.")]
        public Rect ingredientZone = new Rect(-9.1f, -3.3f, 18.2f, 8.2f);

        [Header("Knife (plan §5.2, §5.3)")]
        [Tooltip("Board units of drag that produce full power. Independently of pixels, so mouse "
               + "and touch feel identical.")]
        [Min(0.1f)] public float maxDragLength = 3.5f;

        [Tooltip("Drags shorter than this are taps and are discarded.")]
        [Min(0f)] public float deadZone = 0.35f;

        [Min(0.1f)] public float launchMinSpeed = 8f;
        [Min(0.1f)] public float launchMaxSpeed = 22f;

        [Tooltip("Physics guard applied after every bounce. Never reachable from one launch; it "
               + "exists so a future §3 multiplier cannot defeat CCD.")]
        [Min(0.1f)] public float speedHardCap = 40f;

        [Tooltip("Below this the knife is spent and returns to the launcher.")]
        [Min(0f)] public float minLiveSpeed = 3f;

        [Tooltip("Speed kept after each wall bounce.")]
        [Range(0.5f, 1f)] public float bounceRetention = 0.9f;

        [Min(1f)] public float maxKnifeLifetime = 15f;
        [Min(1)] public int maxKnifeBounces = 60;

        [Header("Ingredients (plan §5.4)")]
        [Min(1)] public int targetIngredientCount = 12;
        [Min(1)] public int workingMaxIngredientCount = 18;
        [Min(0.05f)] public float ingredientRespawnInterval = 0.6f;
        [Min(1f)] public float ingredientLifetime = 12f;
        [Min(0f)] public float ingredientMinSeparation = 0.6f;
        [Min(1)] public int maxSpawnsPerTick = 2;

        [Header("Orders (plan §6.2, §6.4)")]
        [Min(1)] public int maxActiveOrders = 4;
        [Min(0.5f)] public float orderSpawnInterval = 6f;
        [Min(1f)] public float basePatienceSeconds = 25f;
        [Min(0.1f)] public float patienceHurryThreshold = 0.6f;
        [Min(0.05f)] public float patienceCriticalThreshold = 0.3f;
        [Min(0)] public int startReputation = 3;

        [Header("Scaling seams (§3, §4)")]
        [Min(0f)] public float cashMultiplier = 1f;
        [Min(0f)] public float patienceUpgradeMultiplier = 1f;
    }
}
```

2. Create the asset: right-click in `Assets/ScriptableObjects/` → **Create → Slice & Serve →
   Board Config**, name it `BoardConfig`, and leave every default.
3. Create `Assets/Scripts/Runtime/Core/RuntimeTuning.cs` — the indirection §3's upgrades write to,
   and the reason no system may cache config values in `Awake`:

```csharp
using SliceNServe.Data;

namespace SliceNServe.Core
{
    /// <summary>
    /// The mutable view of <see cref="BoardConfig"/> that gameplay reads every tick. Section 3's
    /// upgrades and Section 4's difficulty ramp write here; nothing writes to the asset at
    /// runtime, so a play session can never corrupt the authored defaults.
    ///
    /// This exists so that "increase knife speed by 15%" is an assignment, not a code change.
    /// </summary>
    public sealed class RuntimeTuning
    {
        private readonly BoardConfig _config;

        public RuntimeTuning(BoardConfig config) => _config = config;

        /// <summary>Authoring defaults, for a reset button or a new run.</summary>
        public BoardConfig Config => _config;

        // §3 seams — 1.0 until an upgrade says otherwise.
        public float LaunchSpeedMultiplier { get; set; } = 1f;
        public float IngredientSpawnRateMultiplier { get; set; } = 1f;
        public float PatienceMultiplier { get; set; } = 1f;
        public float CashMultiplier { get; set; } = 1f;

        public float LaunchMinSpeed => _config.launchMinSpeed * LaunchSpeedMultiplier;
        public float LaunchMaxSpeed => _config.launchMaxSpeed * LaunchSpeedMultiplier;
        public float SpeedHardCap => _config.speedHardCap;
        public float MinLiveSpeed => _config.minLiveSpeed;
        public float BounceRetention => _config.bounceRetention;
        public float MaxKnifeLifetime => _config.maxKnifeLifetime;
        public int MaxKnifeBounces => _config.maxKnifeBounces;

        public float MaxDragLength => _config.maxDragLength;
        public float DeadZone => _config.deadZone;
        public float IngredientMinSeparation => _config.ingredientMinSeparation;
        public float IngredientRespawnInterval => _config.ingredientRespawnInterval / IngredientSpawnRateMultiplier;
        public float IngredientLifetime => _config.ingredientLifetime;
        public int TargetIngredientCount => _config.targetIngredientCount;
        public int WorkingMaxIngredientCount => _config.workingMaxIngredientCount;
        public int MaxSpawnsPerTick => _config.maxSpawnsPerTick;

        public int MaxActiveOrders => _config.maxActiveOrders;
        public float OrderSpawnInterval => _config.orderSpawnInterval;
        public float BasePatienceSeconds => _config.basePatienceSeconds * PatienceMultiplier;
        public float PatienceHurryThreshold => _config.patienceHurryThreshold;
        public float PatienceCriticalThreshold => _config.patienceCriticalThreshold;
        public int StartReputation => _config.startReputation;
        public float TotalCashMultiplier => _config.cashMultiplier * CashMultiplier;

        public Rect WorldBounds => _config.worldBounds;
        public Rect IngredientZone => _config.ingredientZone;
        public Vector2 LauncherPosition => _config.launcherPosition;
    }
}
```

**Verify:** the asset exists, the Inspector shows five header groups (`Board`, `Knife`,
`Ingredients`, `Orders`, `Scaling seams`), and `worldBounds` reads `(-9.6, -5.4, 19.2, 10.8)` so
`BoardViewportAdapter` still frames the board correctly.

---

## Step 5 — Core scaffolding: events, wallet, clock, registry, bootstrap

**Goal:** the plumbing §2's systems talk through, with no framework. (Phase 0)

1. Create `Assets/Scripts/Runtime/Core/GameEvents.cs` — every gameplay event, as a `readonly struct`
   so publishing never allocates a heap object:

```csharp
using UnityEngine;

namespace SliceNServe.Core
{
    public enum LaunchSource { Manual = 0, Helper = 1 }
    public enum GameOverReason { ReputationDepleted = 0, Quit = 1 }

    public readonly struct KnifeLaunched
    {
        public readonly int KnifeId;
        public readonly int Source;
        public KnifeLaunched(int knifeId, int source) { KnifeId = knifeId; Source = source; }
    }

    public readonly struct KnifeBounced
    {
        public readonly int KnifeId;
        public readonly int BounceIndex;
        public readonly Vector2 Position;
        public KnifeBounced(int knifeId, int bounceIndex, Vector2 position)
        { KnifeId = knifeId; BounceIndex = bounceIndex; Position = position; }
    }

    public readonly struct KnifeReturned
    {
        public readonly int KnifeId;
        public KnifeReturned(int knifeId) { KnifeId = knifeId; }
    }

    /// <summary>Fired once per sliced ingredient. Also the §5 sabotage telemetry source.</summary>
    public readonly struct IngredientSliced
    {
        public readonly string IngredientId;
        public readonly Vector2 Position;
        public readonly int KnifeId;
        public readonly int BounceIndex;
        public IngredientSliced(string ingredientId, Vector2 position, int knifeId, int bounceIndex)
        { IngredientId = ingredientId; Position = position; KnifeId = knifeId; BounceIndex = bounceIndex; }
    }

    public readonly struct IngredientWasted
    {
        public readonly string IngredientId;
        public readonly Vector2 Position;
        public IngredientWasted(string ingredientId, Vector2 position)
        { IngredientId = ingredientId; Position = position; }
    }

    public readonly struct OrderCreated
    {
        public readonly int OrderId;
        public readonly string DishId;
        public readonly string CustomerId;
        public OrderCreated(int orderId, string dishId, string customerId)
        { OrderId = orderId; DishId = dishId; CustomerId = customerId; }
    }

    public readonly struct OrderProgressed
    {
        public readonly int OrderId;
        public readonly string IngredientId;
        public readonly int Fulfilled;
        public readonly int Required;
        public OrderProgressed(int orderId, string ingredientId, int fulfilled, int required)
        { OrderId = orderId; IngredientId = ingredientId; Fulfilled = fulfilled; Required = required; }
    }

    public readonly struct OrderServed
    {
        public readonly int OrderId;
        public readonly int Cash;
        public OrderServed(int orderId, int cash) { OrderId = orderId; Cash = cash; }
    }

    public readonly struct OrderExpired
    {
        public readonly int OrderId;
        public readonly string CustomerId;
        public OrderExpired(int orderId, string customerId) { OrderId = orderId; CustomerId = customerId; }
    }

    public readonly struct ReputationChanged
    {
        public readonly int Reputation;
        public ReputationChanged(int reputation) { Reputation = reputation; }
    }

    public readonly struct GameOver
    {
        public readonly int Reason;
        public GameOver(int reason) { Reason = reason; }
    }
}
```

> `IngredientSliced` carries `string` ids because `IngredientDefinition.id` is a string (plan §5.4).
> If a later pass needs the slice path to be allocation-free, add a cached `int` key to the
> definition and switch the event to it — do not change the field's meaning.

2. Create `Assets/Scripts/Runtime/Core/EventBus.cs` and `Assets/Scripts/Runtime/Core/Wallet.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace SliceNServe.Core
{
    /// <summary>Typed publish/subscribe over struct events. Deliberately tiny — no DI framework
    /// is needed for Section 2 (plan §7).</summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            _handlers.TryGetValue(typeof(T), out Delegate existing);
            _handlers[typeof(T)] = Delegate.Combine(existing, handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (!_handlers.TryGetValue(typeof(T), out Delegate existing)) return;

            Delegate remaining = Delegate.Remove(existing, handler);
            if (remaining == null) _handlers.Remove(typeof(T));
            else _handlers[typeof(T)] = remaining;
        }

        public void Publish<T>(in T evt) where T : struct
        {
            if (!_handlers.TryGetValue(typeof(T), out Delegate existing)) return;
            (existing as Action<T>)?.Invoke(evt);
        }
    }
}
```

```csharp
using System;

namespace SliceNServe.Core
{
    /// <summary>
    /// Minimal cash holder. Section 3 grows this into the real economy — Section 2 only needs
    /// the value and its change event to prove the loop closes (plan §6.6). Keep the surface
    /// this small so replacing it stays cheap.
    /// </summary>
    public sealed class Wallet
    {
        public int Amount { get; private set; }

        public event Action<int> Changed;

        public void Add(int cash)
        {
            if (cash == 0) return;

            Amount += cash;
            Changed?.Invoke(Amount);
        }
    }
}
```

3. Create `Assets/Scripts/Runtime/Core/RunClock.cs`. The plan's §6.4 writes patience depletion as
   `Time.deltaTime`; this class is what that expression becomes once §4 needs to scale or pause it
   (plan §3.2), without every system reaching for `Time` directly:

```csharp
using System;

namespace SliceNServe.Core
{
    /// <summary>Pause-aware, independently scalable frame delta. Defaults to exactly
    /// <c>Time.deltaTime</c>, so Section 2's behaviour is unchanged until something scales it.</summary>
    public sealed class RunClock
    {
        private float _timeScale = 1f;

        public bool IsPaused { get; private set; }

        public float TimeScale
        {
            get => _timeScale;
            set => _timeScale = UnityEngine.Mathf.Clamp(value, 0f, 8f);
        }

        public float DeltaTime => IsPaused ? 0f : UnityEngine.Time.deltaTime * _timeScale;
        public float UnscaledDeltaTime => UnityEngine.Time.deltaTime;

        public event Action<bool> PauseChanged;

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;

            IsPaused = paused;
            PauseChanged?.Invoke(paused);
        }

        public void Toggle() => SetPaused(!IsPaused);
    }
}
```

4. Create `Assets/Scripts/Runtime/Core/ServiceRegistry.cs` and
   `Assets/Scripts/Runtime/Core/GameBootstrap.cs`:

```csharp
namespace SliceNServe.Core
{
    /// <summary>
    /// The one container in the project (plan §7). Created by <see cref="GameBootstrap"/>; there
    /// are no other singletons. Sections 3+ add to this rather than inventing their own locator.
    /// </summary>
    public sealed class ServiceRegistry
    {
        public ServiceRegistry(EventBus events, RunClock clock, RuntimeTuning tuning, Wallet wallet)
        {
            Events = events;
            Clock = clock;
            Tuning = tuning;
            Wallet = wallet;
        }

        public EventBus Events { get; }
        public RunClock Clock { get; }
        public RuntimeTuning Tuning { get; }
        public Wallet Wallet { get; }

        public static ServiceRegistry Current { get; private set; }

        public static ServiceRegistry Install(ServiceRegistry registry)
        {
            Current = registry;
            return registry;
        }

        public static void Clear() => Current = null;
    }
}
```

```csharp
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Core
{
    /// <summary>
    /// Single entry point for the scene (plan §4.4): builds the services, wires the events and
    /// owns the run lifecycle. Runs before anything that consumes the registry.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private BoardConfig boardConfig;

        public ServiceRegistry Services { get; private set; }

        private void Awake()
        {
            if (boardConfig == null)
            {
                Debug.LogError("GameBootstrap: no BoardConfig assigned.", this);
                enabled = false;
                return;
            }

            var tuning = new RuntimeTuning(boardConfig);
            Services = ServiceRegistry.Install(new ServiceRegistry(
                new EventBus(), new RunClock(), tuning, new Wallet()));

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
        }

        private void OnDestroy() => ServiceRegistry.Clear();
    }
}
```

**Verify:** the project recompiles with a clean Console.

---

## Step 6 — `Game.unity`: camera, bounds and walls

**Goal:** an empty walled 19.2 × 10.8 board that bounces a knife. (Phase 0, plan §4.4)

1. Create the scene `Assets/Scenes/Game.unity` (**File → New Scene → Basic 2D**), then **File →
   Save As** → `Assets/Scenes/Game.unity`.
2. Add it to `File → Build Settings` as the **only** entry (index 0), and remove
   `SampleScene.unity` if the template left it listed.
3. Set the `Main Camera`: **Projection Orthographic**, `Size 5.4` (⇒ 10.8 world units tall, 19.2
   wide at 16:9), `Position (0, 0, -10)`, clear flags **Solid Color**. Linear color space is
   already set project-wide.
   `BoardViewportAdapter` will take ownership of the camera size at runtime once Section 1's runbook
   adds it; the authored `5.4` is what makes it a no-op at 16:9 (plan decision D8).
4. Create `Assets/Scripts/Runtime/Board/BoardBounds.cs`:

```csharp
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Builds the four board walls from <see cref="BoardConfig.worldBounds"/> so the collider
    /// inner faces sit exactly on the boundary.
    ///
    /// The 2.0 u collider thickness is load-bearing, not decoration: at the project's 0.02 s
    /// fixed step the knife travels 0.8 u per tick at the 40 u/s cap (plan §4.6), so anything
    /// thinner than that can be tunnelled through in a single step.
    /// </summary>
    public sealed class BoardBounds : MonoBehaviour
    {
        [SerializeField] private Sprite wallSprite;
        [SerializeField] private float colliderThickness = 2f;
        [SerializeField] private float visualThickness = 0.25f;

        public Rect PlayArea { get; private set; }
        public Vector2 LauncherPosition { get; private set; }

        private BoardConfig _config;

        public void Initialize(BoardConfig config)
        {
            _config = config;
            PlayArea = config.worldBounds;
            LauncherPosition = config.launcherPosition;
            Build();
        }

        private void Build()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            Rect b = _config.worldBounds;
            float hw = b.width * 0.5f, hh = b.height * 0.5f;
            float t = colliderThickness, v = visualThickness;

            // Horizontal walls span the corners; vertical walls fit between them, so there are no
            // internal corners for the knife to wedge into (plan §5.1).
            AddWall("WallTop", new Vector2(0f, hh + t * 0.5f), new Vector2(b.width + t * 2f, t), new Vector2(b.width, v));
            AddWall("WallBottom", new Vector2(0f, -hh - t * 0.5f), new Vector2(b.width + t * 2f, t), new Vector2(b.width, v));
            AddWall("WallLeft", new Vector2(-hw - t * 0.5f, 0f), new Vector2(t, hh * 2f), new Vector2(v, hh * 2f));
            AddWall("WallRight", new Vector2(hw + t * 0.5f, 0f), new Vector2(t, hh * 2f), new Vector2(v, hh * 2f));
        }

        private void AddWall(string wallName, Vector2 center, Vector2 colliderSize, Vector2 visualSize)
        {
            var go = new GameObject(wallName);
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            go.layer = LayerMask.NameToLayer("BoardWall");

            var box = go.AddComponent<BoxCollider2D>();
            box.size = colliderSize;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = wallSprite;
            sr.sortingOrder = 1;

            // Scale the transform rather than using SpriteDrawMode.Sliced, which would require the
            // placeholder texture to be imported with Mesh Type "Full Rect".
            if (wallSprite != null)
            {
                Vector2 native = wallSprite.bounds.size;
                if (native.x > 0f && native.y > 0f)
                    sr.transform.localScale = new Vector3(visualSize.x / native.x, visualSize.y / native.y, 1f);
            }
        }
    }
}
```

5. In `Game.unity`: create `Bootstrap` (add `GameBootstrap`, assign `BoardConfig`), and
   `BoardBounds` (add `BoardBounds`, assign `wallSprite` = `square`). Add a `Backdrop` sprite at
   `z = +1` behind gameplay if you want contrast; gameplay sits at `z = 0`.

**Verify:** Play mode shows a walled, empty board filling a 16:9 game view, with the four walls'
colliders sitting *outside* the visible play area.

---

## Step 7 — The knife: launch, bounce, lifetime

**Goal:** drag-and-release launches a knife that bounces with explicit energy decay and always
ends. (Phases 1–2, plan §5.2–5.3, §5.6)

1. `Project Settings → Input System Package` → confirm `Assets/InputSystem_Actions.inputactions`
   is the project-wide asset. Open it and add a **`Board`** action map with:

| Action | Action Type | Control Type | Bindings | Interactions |
| --- | --- | --- | --- | --- |
| `Aim` | Value | Vector2 | `<Pointer>/position`, `<Touchscreen>/primaryTouch/position`, `<Pen>/position` | — |
| `Launch` | Button | — | `<Mouse>/leftButton`, `<Touchscreen>/primaryTouch/press`, `<Pen>/tip` | `Press` (`PressAndRelease`) |

   `<Pointer>` covers `Mouse`, `Touchscreen` and `Pen` with one binding, so there is no device
   branching. In MVP bind only `primaryTouch` — that is the multi-touch guard.
2. Create `Assets/Scripts/Runtime/Board/KnifeLauncher.cs` — the launch maths, kept pure so
   `AimMathTests` (Step 11) can test it with no scene:

```csharp
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Gesture → velocity. Pure and static so it is unit-testable and so the aim preview and the
    /// actual launch can never disagree: both call <see cref="ResolveVelocity"/>.
    /// </summary>
    public static class KnifeLauncher
    {
        /// <summary>Gestures closer to parallel than this to any wall are pushed away, so the first
        /// contact is never a graze (plan §5.6).</summary>
        public const float MinWallAngleDegrees = 4f;

        /// <summary>
        /// The gesture is a screen-space drag. Power is the drag length in <em>world units</em>
        /// divided by the reference length, so a desktop drag and a phone thumb that travel the
        /// same board distance produce the same launch.
        /// </summary>
        public static float ResolvePower(Vector2 dragWorld, RuntimeTuning tuning)
            => Mathf.Clamp01(dragWorld.magnitude / tuning.MaxDragLength);

        /// <summary>Slingshot: the launch direction is opposite the drag (plan decision D4).</summary>
        public static Vector2 ResolveDirection(Vector2 dragWorld)
            => ClampAwayFromParallelWalls(-dragWorld.normalized);

        public static Vector2 ResolveVelocity(Vector2 dragWorld, RuntimeTuning tuning)
            => ResolveDirection(dragWorld) * Mathf.Lerp(tuning.LaunchMinSpeed, tuning.LaunchMaxSpeed, ResolvePower(dragWorld, tuning));

        public static bool IsValidDrag(Vector2 dragWorld, RuntimeTuning tuning)
            => dragWorld.magnitude >= tuning.DeadZone;

        public static Vector2 ClampAwayFromParallelWalls(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return Vector2.up;

            Vector2 d = dir.normalized;
            float sin = Mathf.Sin(MinWallAngleDegrees * Mathf.Deg2Rad);
            float dx = Mathf.Abs(d.x), dy = Mathf.Abs(d.y);

            // Near-horizontal travel would graze a horizontal wall; near-vertical travel would
            // graze a vertical one. Lift the smaller component up to the sine floor.
            if (dy < sin)
                d = new Vector2(Mathf.Sign(d.x == 0f ? 1f : d.x), Mathf.Sign(d.y == 0f ? 1f : d.y) * sin);
            else if (dx < sin)
                d = new Vector2(Mathf.Sign(d.x == 0f ? 1f : d.x) * sin, Mathf.Sign(d.y == 0f ? 1f : d.y));

            return d.normalized;
        }
    }
}
```

3. Create `Assets/Scripts/Runtime/Board/AimInput.cs` — the drag state machine. The Input System
   has **no built-in `Drag`/`Swipe` interaction** (1.19 ships only Default, Press, Hold, Tap,
   SlowTap, MultiTap), so the release is read from `Launch`'s *canceled* callback:

```csharp
using System;
using SliceNServe.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SliceNServe.Board
{
    /// <summary>
    /// Drag start → drag → release, on one code path for mouse, touch and pen.
    /// Raises <see cref="OnLaunch"/> with the drag in world units.
    /// </summary>
    public sealed class AimInput : MonoBehaviour
    {
        [SerializeField] private Camera boardCamera;

        private InputAction _aim;
        private InputAction _launch;
        private Vector2 _dragStartScreen;
        private bool _dragging;

        /// <summary>Live drag in world units, or zero when not dragging. For aim-guide rendering.</summary>
        public Vector2 CurrentDragWorld { get; private set; }

        public event Action<Vector2> OnLaunch;

        public void Initialize(RuntimeTuning tuning)
        {
            _tuning = tuning;

            InputActionMap board = InputSystem.actions.FindActionMap("Board", throwIfNotFound: true);
            _aim = board.FindAction("Aim", throwIfNotFound: true);
            _launch = board.FindAction("Launch", throwIfNotFound: true);

            _launch.started += OnPressStarted;
            _launch.canceled += OnPressReleased;
            board.Enable();
        }

        private RuntimeTuning _tuning;

        private void OnDestroy()
        {
            if (_launch == null) return;

            _launch.started -= OnPressStarted;
            _launch.canceled -= OnPressReleased;
        }

        private void OnPressStarted(InputAction.CallbackContext _)
        {
            Vector2 screen = _aim.ReadValue<Vector2>();
            if (boardCamera != null && !boardCamera.pixelRect.Contains(screen)) return;

            _dragStartScreen = screen;
            _dragging = true;
            CurrentDragWorld = Vector2.zero;
        }

        private void OnPressReleased(InputAction.CallbackContext _)
        {
            if (!_dragging) return;

            _dragging = false;
            Vector2 dragWorld = CurrentDragWorld;
            CurrentDragWorld = Vector2.zero;

            // A tap, or a drag inside the dead zone, must not fire a knife.
            if (!KnifeLauncher.IsValidDrag(dragWorld, _tuning)) return;

            OnLaunch?.Invoke(dragWorld);
        }

        private void Update()
        {
            if (!_dragging) return;

            Vector2 current = _aim.ReadValue<Vector2>();
            CurrentDragWorld = ScreenToWorldDelta(current - _dragStartScreen);
        }

        private Vector2 ScreenToWorldDelta(Vector2 screenDelta)
            => boardCamera == null
                ? screenDelta
                : (Vector2)boardCamera.ScreenToWorldPoint(screenDelta) - (Vector2)boardCamera.ScreenToWorldPoint(Vector2.zero);
    }
}
```

4. Create `Assets/Scripts/Runtime/Board/TrajectoryPreview.cs`:

```csharp
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Draws the first N wall bounces while the player is dragging.
    ///
    /// This is a *preview*, not a simulation: it must never call Physics2D.Simulate, and it
    /// deliberately ignores ingredients so it cannot lie about what the knife will hit. If you
    /// find yourself "fixing" it into a real prediction, that is a design change, not a bug fix.
    /// </summary>
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField, Range(0, 6)] private int previewBounces = 2;
        [SerializeField] private float maxSegmentLength = 60f;

        private AimInput _input;
        private RuntimeTuning _tuning;
        private int _wallMask;

        public void Initialize(AimInput input, RuntimeTuning tuning)
        {
            _input = input;
            _tuning = tuning;
            _wallMask = 1 << LayerMask.NameToLayer("BoardWall");
            if (line != null) line.positionCount = 0;
        }

        private void LateUpdate()
        {
            if (_input == null || line == null) return;

            Vector2 drag = _input.CurrentDragWorld;
            if (!KnifeLauncher.IsValidDrag(drag, _tuning)) { line.positionCount = 0; return; }

            Vector2 velocity = KnifeLauncher.ResolveVelocity(drag, _tuning);
            if (velocity.sqrMagnitude < 1e-6f) { line.positionCount = 0; return; }

            Vector2 dir = velocity.normalized;
            Vector2 p = _tuning.LauncherPosition;

            line.positionCount = previewBounces + 2;
            line.SetPosition(0, p);

            for (int i = 0; i <= previewBounces; i++)
            {
                RaycastHit2D hit = Physics2D.Raycast(p, dir, maxSegmentLength, _wallMask);
                if (!hit)
                {
                    line.SetPosition(i + 1, p + dir * maxSegmentLength * 0.35f);
                    line.positionCount = i + 2;
                    return;
                }

                line.SetPosition(i + 1, hit.point);
                p = hit.point + hit.normal * 0.001f;   // step off the surface to avoid re-hitting
                dir = Vector2.Reflect(dir, hit.normal);
            }
        }
    }
}
```

5. Create `Assets/Scripts/Runtime/Board/IngredientSlicer.cs` — the swept collection query (§5.5).
   Keeping it in its own component is deliberate: it makes "the swept cast is the only collection
   path" a single file you can point at.

```csharp
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Speed-proof ingredient collection: every physics step, sweep the knife's own collider along
    /// the segment it is about to travel and collect whatever it crosses.
    ///
    /// This exists because Continuous collision detection does <b>not</b> sweep triggers, so a fast
    /// knife can pass an ingredient between steps. Section 3's Knife Speed upgrade exists precisely
    /// to make the knife faster, so this cannot be left to chance.
    /// </summary>
    public sealed class IngredientSlicer : MonoBehaviour
    {
        private readonly RaycastHit2D[] _results = new RaycastHit2D[16];

        private Rigidbody2D _rb;
        private ContactFilter2D _filter;
        private IngredientSpawner _spawner;

        public void Initialize(Rigidbody2D rb, IngredientSpawner spawner)
        {
            _rb = rb;
            _spawner = spawner;
            _filter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true,
                layerMask = 1 << LayerMask.NameToLayer("Ingredient")
            };
        }

        /// <summary>Returns how many ingredients were collected this step.</summary>
        public int Sweep()
        {
            Vector2 v = _rb.linearVelocity;
            if (v.sqrMagnitude < 1e-6f) return 0;   // a zero-length cast direction returns nothing

            int hits = _rb.Cast(v.normalized, _filter, _results, v.magnitude * Time.fixedDeltaTime);

            int collected = 0;
            for (int i = 0; i < hits; i++)
            {
                // The spawner unregisters on collection, so a repeat hit for the same collider
                // within this step resolves false. That is the within-step dedupe.
                if (!_spawner.TryResolve(_results[i].collider, out Ingredient ing)) continue;

                _spawner.Slice(ing);
                collected++;
            }
            return collected;
        }
    }
}
```

6. Create `Assets/Scripts/Runtime/Board/KnifeController.cs`:

```csharp
using System;
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    public sealed class KnifeController : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private IngredientSlicer slicer;

        private RuntimeTuning _tuning;
        private EventBus _events;
        private Rect _playArea;
        private int _wallLayer;

        private int _knifeId;
        private int _bounceCount;
        private float _age;
        private bool _live;

        public bool IsLive => _live;
        public int BounceCount => _bounceCount;
        public int LastSliceCount { get; private set; }

        public event Action<KnifeController> Expired;

        public void Initialize(RuntimeTuning tuning, EventBus events, IngredientSpawner spawner, Rect playArea)
        {
            _tuning = tuning;
            _events = events;
            _playArea = playArea;
            _wallLayer = LayerMask.NameToLayer("BoardWall");

            if (body == null) body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;                                       // decision D1
            body.linearDamping = 0f;
            body.angularDamping = 0.4f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.simulated = false;

            if (slicer == null) slicer = GetComponent<IngredientSlicer>();
            slicer.Initialize(body, spawner);
        }

        public void Launch(int knifeId, Vector2 velocity, LaunchSource source)
        {
            _knifeId = knifeId;
            _bounceCount = 0;
            _age = 0f;
            _live = true;
            transform.rotation = Quaternion.identity;
            body.simulated = true;
            body.linearVelocity = velocity;
            body.angularVelocity = UnityEngine.Random.Range(-360f, 360f);
            _events.Publish(new KnifeLaunched(knifeId, (int)source));
        }

        public void ReturnToPool()
        {
            if (!_live) return;

            _live = false;
            body.simulated = false;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            gameObject.SetActive(false);
            _events.Publish(new KnifeReturned(_knifeId));
        }

        private void FixedUpdate()
        {
            if (!_live) return;

            _age += Time.fixedDeltaTime;
            LastSliceCount = slicer.Sweep();   // plan §5.5 — collection never touches velocity
            CheckEnd();
        }

        private void CheckEnd()
        {
            float minSq = _tuning.MinLiveSpeed * _tuning.MinLiveSpeed;
            bool tooSlow = body.linearVelocity.sqrMagnitude < minSq;
            bool tooOld = _age > _tuning.MaxKnifeLifetime;
            bool tooBouncy = _bounceCount > _tuning.MaxKnifeBounces;
            bool escaped = !_playArea.Contains(body.position);   // safety net, not a mechanism

            if (tooSlow || tooOld || tooBouncy || escaped) Expired?.Invoke(this);
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (!_live) return;

            // Layer, not tag — this project defines no tags (plan §4.3).
            if (c.collider.gameObject.layer != _wallLayer) return;

            ContactPoint2D contact = c.GetContact(0);   // contactCount is always >= 1 here
            _bounceCount++;
            _events.Publish(new KnifeBounced(_knifeId, _bounceCount, contact.point));

            // OnCollisionEnter2D runs *after* the physics step, so the bounce has already
            // reflected the velocity; this only scales it.
            Vector2 v = body.linearVelocity * _tuning.BounceRetention;
            float cap = _tuning.SpeedHardCap;
            if (v.sqrMagnitude > cap * cap) v = v.normalized * cap;

            // Anti-"wall-hugging": if the outgoing direction is nearly parallel to the wall, nudge
            // it off a few degrees so the knife cannot ride the boundary.
            if (Mathf.Abs(Vector2.Dot(v.normalized, contact.normal)) < 0.08f)
                v = Rotate(v, 6f * (UnityEngine.Random.value < 0.5f ? -1f : 1f));

            body.linearVelocity = v;
        }

        /// Rotate a 2D vector by degrees, staying in Vector2 (no Vector3/Quaternion round-trip).
        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
```

7. Create `Assets/Scripts/Runtime/Board/KnifeManager.cs`:

```csharp
using System;
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Owns the knife pool and the only public way to launch. Pooled from day one even though
    /// Section 2 launches one at a time, because Section 3's "Knife Split Chance" multiplies
    /// within the cap rather than introducing the concept of many knives.
    /// </summary>
    public sealed class KnifeManager : MonoBehaviour
    {
        private const int PoolCapacity = 32;

        [SerializeField] private KnifeController knifePrefab;
        [SerializeField] private Transform launcherAnchor;

        private readonly KnifeController[] _pool = new KnifeController[PoolCapacity];
        private RuntimeTuning _tuning;
        private EventBus _events;
        private int _poolCount;
        private int _nextId = 1;

        public bool CanLaunch { get; private set; } = true;
        public Vector2 LauncherPosition => launcherAnchor.position;

        /// <summary>Fired whenever a knife returns to the launcher.</summary>
        public event Action<KnifeController> KnifeReturned;

        public void Initialize(RuntimeTuning tuning, EventBus events, BoardBounds bounds, IngredientSpawner spawner)
        {
            _tuning = tuning;
            _events = events;
            CanLaunch = true;

            // Pooled up front and never instantiated during a run. Section 3's "Knife Split
            // Chance" multiplies within this cap rather than raising it.
            _poolCount = PoolCapacity;
            for (int i = 0; i < _poolCount; i++)
            {
                KnifeController k = Instantiate(knifePrefab, launcherAnchor);
                k.name = $"Knife_{i}";
                k.gameObject.SetActive(false);
                k.Initialize(tuning, events, spawner, bounds.PlayArea);
                k.Expired += OnKnifeExpired;
                _pool[i] = k;
            }
        }

        /// <summary>Takes a velocity, not a drag, so §3's Sous-Chef helpers can call it with a
        /// computed aim vector and <see cref="LaunchSource.Helper"/> and no input at all.</summary>
        public KnifeController Launch(Vector2 velocity, LaunchSource source)
        {
            if (!CanLaunch) return null;

            for (int i = 0; i < _poolCount; i++)
            {
                KnifeController k = _pool[i];
                if (k.IsLive) continue;

                k.transform.position = launcherAnchor.position;
                k.gameObject.SetActive(true);
                k.Launch(_nextId++, velocity, source);
                CanLaunch = false;
                return k;
            }
            return null;
        }

        /// <summary>For §3's wave clear and §5's round reset.</summary>
        public void ReturnAllKnives()
        {
            for (int i = 0; i < _poolCount; i++)
                if (_pool[i].IsLive) _pool[i].ReturnToPool();

            CanLaunch = true;
        }

        private void OnKnifeExpired(KnifeController k)
        {
            k.ReturnToPool();
            CanLaunch = true;
            KnifeReturned?.Invoke(k);
        }
    }
}
```

> The pool instantiates every knife up front and never instantiates during a run. If you later want
> a fade on return, pool the effect too rather than calling `Instantiate` per knife.
> The pool size is currently `PoolCapacity` rather than a config value; when §3 needs more than 32
> knives, raise it *and* re-check the §5 budget before doing so.

8. Create the knife prefab `Assets/Prefabs/Knife.prefab`:
   - Root `Knife`, **layer `Knife`**, with:
     - `Rigidbody2D` — Dynamic, `Gravity Scale 0`, `Linear Damping 0`, `Angular Damping 0.4`,
       `Collision Detection Continuous`, `Interpolate`.
     - `CircleCollider2D` — **radius `0.18`**, material `KnifeMaterial`.
     - `IngredientSlicer` and `KnifeController` (assign `body` and `slicer` to those components).
   - Child `Visual` with a `SpriteRenderer` using `knife.png`.
   A circle, not a capsule, **on purpose**: a bouncing body needs a rotation-independent silhouette,
   or spin makes the rebound unpredictable. Because the collider is a circle, the sprite's spin is
   purely cosmetic.

**Verify:** in Play mode a knife given a velocity by hand bounces off the walls and loses ~10 % of
its speed per bounce (`bounceRetention = 0.90`).

---

## Step 8 — Ingredient definitions and spawner

**Goal:** a self-refilling board of non-solid pickups that expire, so no ingredient can become
permanently unobtainable. (Phase 3, plan §5.4)

1. Create `Assets/Scripts/Runtime/Data/IngredientDefinition.cs`:

```csharp
using UnityEngine;

namespace SliceNServe.Data
{
    [CreateAssetMenu(menuName = "Slice & Serve/Ingredient", fileName = "Ingredient")]
    public sealed class IngredientDefinition : ScriptableObject
    {
        [Tooltip("Stable key used by recipes and save data. Never the asset name.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;
        [SerializeField] private Sprite sprite;
        [SerializeField] private Color tint = Color.white;

        [Tooltip("Collider radius, and the basis for spawn separation.")]
        [SerializeField, Min(0.05f)] private float radius = 0.35f;

        [Tooltip("Weight in the spawn table. 0 means it never spawns naturally.")]
        [SerializeField, Min(0f)] private float spawnWeight = 1f;

        [Tooltip("Reserved: false knocks the knife aside (obstacle behaviour, §6 roadmap). "
               + "The code path is stubbed and unused in Section 2.")]
        [SerializeField] private bool slicesOnHit = true;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public Color Tint => tint;
        public float Radius => radius;
        public float SpawnWeight => spawnWeight;
        public bool SlicesOnHit => slicesOnHit;
    }
}
```

2. Create `Assets/Scripts/Runtime/Board/Ingredient.cs`:

```csharp
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// A pickup. Non-solid by design: the collision matrix keeps it out of every physical pair,
    /// so it can never nudge the knife, and collection happens through the knife's swept cast.
    /// </summary>
    public sealed class Ingredient : MonoBehaviour
    {
        [SerializeField] private CircleCollider2D body;
        [SerializeField] private SpriteRenderer visual;

        public IngredientDefinition Definition { get; private set; }
        public int ColliderId { get; private set; }
        public float ExpiresAt { get; private set; }

        private void Awake() => ColliderId = body.GetInstanceID();

        public void Present(IngredientDefinition definition, float lifetime)
        {
            Definition = definition;
            ExpiresAt = Time.time + lifetime;
            body.radius = definition.Radius;
            visual.sprite = definition.Sprite;
            visual.color = definition.Tint;
            gameObject.SetActive(true);
        }

        public void Reposition(Vector2 position) => transform.position = position;
    }
}
```

> The collider **never moves** — only the visual child animates. A static collider that moves
> forces broadphase rebuilds every frame, and a `Rigidbody2D` per ingredient would buy nothing
> when nothing can push anything.

3. Create `Assets/Scripts/Runtime/Board/IngredientSpawner.cs`:

```csharp
using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Owns the ingredient pool and acts as the registry the knife's sweep resolves against.
    /// Reads <see cref="RuntimeTuning"/> every tick and never caches it, so Section 3's
    /// spawn-rate upgrade is an assignment rather than a code change (plan §3.1).
    /// </summary>
    public sealed class IngredientSpawner : MonoBehaviour
    {
        private const int InitialCapacity = 160;

        [SerializeField] private Ingredient ingredientPrefab;
        [SerializeField] private IngredientDefinition[] definitions;
        [SerializeField] private Transform poolRoot;
        [SerializeField] private ParticleSystem sliceBurst;

        private readonly Dictionary<int, Ingredient> _live = new Dictionary<int, Ingredient>(InitialCapacity);
        private readonly List<Ingredient> _pool = new List<Ingredient>(InitialCapacity);
        private readonly List<Ingredient> _all = new List<Ingredient>(InitialCapacity);
        private readonly List<int> _expiring = new List<int>(InitialCapacity);

        private RuntimeTuning _tuning;
        private EventBus _events;
        private float _nextSpawnTime;
        private float _totalWeight;

        public void Initialize(RuntimeTuning tuning, EventBus events)
        {
            _tuning = tuning;
            _events = events;

            _totalWeight = 0f;
            for (int i = 0; i < definitions.Length; i++) _totalWeight += definitions[i].SpawnWeight;

            _nextSpawnTime = Time.time;
        }

        public int LiveCount => _live.Count;

        /// <summary>True when an ingredient of this kind is currently on the board. Used by the
        /// order spawner to bias dishes towards ingredients that actually exist (plan §6.2).</summary>
        public bool HasLive(IngredientDefinition definition)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].gameObject.activeSelf && _all[i].Definition == definition) return true;

            return false;
        }

        private void Update()
        {
            if (_tuning == null) return;

            Expire();

            if (_live.Count >= _tuning.TargetIngredientCount) return;
            if (Time.time < _nextSpawnTime) return;

            _nextSpawnTime = Time.time + _tuning.IngredientRespawnInterval;
            int budget = Mathf.Min(_tuning.MaxSpawnsPerTick, _tuning.TargetIngredientCount - _live.Count);
            for (int i = 0; i < budget; i++) TrySpawnOne();
        }

        private void TrySpawnOne()
        {
            if (_live.Count >= _tuning.WorkingMaxIngredientCount) return;

            IngredientDefinition def = PickDefinition();
            if (def == null) return;

            Rect zone = _tuning.IngredientZone;
            float separation = _tuning.IngredientMinSeparation;
            int mask = 1 << LayerMask.NameToLayer("Ingredient");

            for (int attempt = 0; attempt < 8; attempt++)
            {
                var p = new Vector2(Random.Range(zone.xMin, zone.xMax), Random.Range(zone.yMin, zone.yMax));

                // Validated BEFORE instantiating, so the candidate cannot self-hit even though
                // queriesStartInColliders is on in this project (plan §5.4).
                if (Physics2D.OverlapCircle(p, def.Radius + separation, mask) != null) continue;

                Ingredient ing = Rent();
                ing.Reposition(p);
                ing.Present(def, _tuning.IngredientLifetime);
                _live[ing.ColliderId] = ing;
                return;   // one success per call; failing all 8 attempts defers to the next tick
            }
        }

        private Ingredient Rent()
        {
            int last = _pool.Count - 1;
            if (last < 0)
            {
                Ingredient created = Instantiate(ingredientPrefab, poolRoot);
                _all.Add(created);
                return created;
            }

            Ingredient pooled = _pool[last];
            _pool.RemoveAt(last);
            return pooled;
        }

        private void Expire()
        {
            float now = Time.time;
            _expiring.Clear();

            var e = _live.GetEnumerator();
            while (e.MoveNext())
                if (now >= e.Current.Value.ExpiresAt) _expiring.Add(e.Current.Key);

            for (int i = 0; i < _expiring.Count; i++)
            {
                Ingredient ing = _live[_expiring[i]];
                _live.Remove(_expiring[i]);
                Recycle(ing);
            }
        }

        public bool TryResolve(Collider2D collider, out Ingredient ingredient)
            => _live.TryGetValue(collider.GetInstanceID(), out ingredient);

        /// <summary>
        /// The single entry point for collection. Keeping it here — rather than letting each
        /// caller unregister and recycle — is what stops the registry and the pool drifting apart.
        /// </summary>
        public void Slice(Ingredient ingredient)
        {
            _live.Remove(ingredient.ColliderId);

            _events.Publish(new IngredientSliced(
                ingredient.Definition.Id, ingredient.transform.position,
                knifeId: 0, bounceIndex: 0));   // filled in by the knife if telemetry needs it

            if (sliceBurst != null)
            {
                sliceBurst.transform.position = ingredient.transform.position;
                sliceBurst.Emit(8);   // pooled emitter: no Instantiate, no per-slice garbage
            }

            Recycle(ingredient);
        }

        private void Recycle(Ingredient ingredient)
        {
            ingredient.gameObject.SetActive(false);
            _pool.Add(ingredient);
        }

        private IngredientDefinition PickDefinition()
        {
            if (_totalWeight <= 0f) return null;

            float r = Random.value * _totalWeight;
            for (int i = 0; i < definitions.Length; i++)
            {
                r -= definitions[i].SpawnWeight;
                if (r <= 0f) return definitions[i];
            }
            return definitions[definitions.Length - 1];
        }
    }
}
```

> `Slice` publishes `IngredientSliced` with `knifeId: 0, bounceIndex: 0`, which is a placeholder
> that loses the combo telemetry the plan's §5.5 step 1 asks for. Either drop those two fields
> from the event, or pass the knife's id and bounce count into `Slice` from `IngredientSlicer`.
> Decide once and keep one source of truth — do not have both `Slice` and the knife publish it.

4. Create six ingredient assets in `Assets/ScriptableObjects/Ingredients/`, each with a unique
   `id` string and a distinct `tint`:

| Asset | `id` | `spawnWeight` |
| --- | --- | --- |
| `Ingredient_Tomato.asset` | `tomato` | 1 |
| `Ingredient_Onion.asset` | `onion` | 1 |
| `Ingredient_Mushroom.asset` | `mushroom` | 1 |
| `Ingredient_Cheese.asset` | `cheese` | 0.6 |
| `Ingredient_Lettuce.asset` | `lettuce` | 1 |
| `Ingredient_Dough.asset` | `dough` | 1 |

5. Create the prefab `Assets/Prefabs/Ingredient.prefab`:
   - Root `Ingredient`, **layer `Ingredient`**, with `CircleCollider2D` (**Is Trigger** ✔,
     radius `0.35`) and the `Ingredient` component (assign `body`, `visual`).
     **No `Rigidbody2D`** — the trigger is what the knife's swept cast hits, and nothing pushes it.
   - Child `Visual` with a `SpriteRenderer` using `circle.png` (assign to `visual`).
6. In `Game.unity`: add `IngredientSpawner` with `ingredientPrefab`, the six assets in
   `definitions`, a `poolRoot` empty GameObject, and a `ParticleSystem` (play-on-awake **off**)
   assigned to `sliceBurst`.

**Verify:** Play mode holds ~12 ingredients inside the board box; none appear in the bottom launch
corridor or on top of the launcher; uncollected ones vanish after ~12 s and are replaced. Run this
grep and confirm the sweep is the only collection path:

```bash
grep -rn "OnTriggerEnter2D\|OnCollisionEnter2D" Assets/Scripts/Runtime
```

The only hit must be `KnifeController.OnCollisionEnter2D`, and it must filter on the `BoardWall`
layer. An `OnTriggerEnter2D` on `Ingredient` would be a second source of truth for collection and
would race the swept cast.

---

## Step 9 — Orders: definitions, queue, patience, spawner

**Goal:** sliced ingredients credit the oldest demanding order, as pure testable logic. (Phase 4,
plan §6.1–6.4)

1. Create `Assets/Scripts/Runtime/Data/DishDefinition.cs` and `CustomerDefinition.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace SliceNServe.Data
{
    [System.Serializable]
    public struct IngredientRequirement
    {
        public IngredientDefinition ingredient;
        [Min(1)] public int count;
    }

    [CreateAssetMenu(menuName = "Slice & Serve/Dish", fileName = "Dish")]
    public sealed class DishDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite sprite;
        [SerializeField] private List<IngredientRequirement> ingredients = new List<IngredientRequirement>();
        [SerializeField, Min(0)] private int baseCash = 10;
        [SerializeField, Min(0f)] private float baseOrderWeight = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public IReadOnlyList<IngredientRequirement> Ingredients => ingredients;
        public int BaseCash => baseCash;
        public float BaseOrderWeight => baseOrderWeight;
    }

    [CreateAssetMenu(menuName = "Slice & Serve/Customer", fileName = "Customer")]
    public sealed class CustomerDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite[] spriteSet;
        [SerializeField, Range(0.1f, 5f)] private float patienceMultiplier = 1f;
        [SerializeField, Min(0f)] private float tipMultiplier = 1f;
        [SerializeField, Min(1)] private int orderCount = 1;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite[] SpriteSet => spriteSet;
        public float PatienceMultiplier => patienceMultiplier;
        public float TipMultiplier => tipMultiplier;
        public int OrderCount => orderCount;
    }
}
```

2. Create `Assets/Scripts/Runtime/Orders/OrderInstance.cs` — plain C#, **not** a MonoBehaviour,
   which is what makes the whole of §2.2 testable without a scene:

```csharp
using SliceNServe.Data;

namespace SliceNServe.Orders
{
    public sealed class OrderLine
    {
        public IngredientDefinition Ingredient;
        public int Required;
        public int Fulfilled;

        public bool Satisfied => Fulfilled >= Required;
    }

    public sealed class OrderInstance
    {
        public int OrderId;
        public DishDefinition Dish;
        public CustomerDefinition Customer;
        public OrderLine[] Lines;
        public float SpawnTime;
        public float PatienceDuration;
        public float RemainingPatience;
        public PatienceState State = PatienceState.Normal;

        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < Lines.Length; i++)
                    if (!Lines[i].Satisfied) return false;

                return true;
            }
        }

        public bool Needs(IngredientDefinition ingredient)
        {
            for (int i = 0; i < Lines.Length; i++)
                if (Lines[i].Ingredient == ingredient && !Lines[i].Satisfied) return true;

            return false;
        }

        /// <summary>Used as the tie-break when two orders are equally old.</summary>
        public int RemainingUnits
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Lines.Length; i++) n += Lines[i].Required - Lines[i].Fulfilled;
                return n;
            }
        }
    }
}
```

3. Create `Assets/Scripts/Runtime/Orders/PatienceSystem.cs` and `DifficultyProvider.cs`:

```csharp
using SliceNServe.Core;
using SliceNServe.Data;

namespace SliceNServe.Orders
{
    public enum PatienceState { Normal = 0, Hurry = 1, Critical = 2, Expired = 3, Served = 4 }

    public static class PatienceSystem
    {
        /// <summary>Pure arithmetic, so it is testable without a frame loop.</summary>
        public static void Tick(OrderInstance order, float delta, RuntimeTuning tuning)
        {
            if (order.State == PatienceState.Served || order.State == PatienceState.Expired) return;

            order.RemainingPatience -= delta;
            float fraction = order.PatienceDuration <= 0f
                ? 0f
                : order.RemainingPatience / order.PatienceDuration;

            if (order.RemainingPatience <= 0f)
            {
                order.RemainingPatience = 0f;
                order.State = PatienceState.Expired;
            }
            else if (fraction < tuning.PatienceCriticalThreshold) order.State = PatienceState.Critical;
            else if (fraction < tuning.PatienceHurryThreshold) order.State = PatienceState.Hurry;
            else order.State = PatienceState.Normal;
        }

        /// <summary>
        /// Resolved ONCE at spawn, so a later tuning change never retroactively rewrites a live
        /// customer. Required by §4 and for PvP fairness (plan §6.4).
        /// </summary>
        public static float ResolveDuration(RuntimeTuning tuning, CustomerDefinition customer, IDifficultyProvider difficulty)
            => tuning.BasePatienceSeconds
             * customer.PatienceMultiplier
             * difficulty.PatienceScale;
    }
}
```

```csharp
namespace SliceNServe.Orders
{
    /// <summary>Section 4 replaces this with a real time-based ramp. Section 2 ships the seam.</summary>
    public interface IDifficultyProvider
    {
        float PatienceScale { get; }
        float OrderSpawnIntervalScale { get; }
    }

    public sealed class ConstantDifficultyProvider : IDifficultyProvider
    {
        public float PatienceScale => 1f;
        public float OrderSpawnIntervalScale => 1f;
    }
}
```

4. Create `Assets/Scripts/Runtime/Orders/LocalOrderQueue.cs` — the auto-credit rule:

```csharp
using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Orders
{
    /// <summary>
    /// The auto-credit rule from plan §6.3. Deliberately a plain class over a list of orders: it
    /// is the highest-value thing in Section 2 to unit-test, and it must run with no scene at all.
    /// </summary>
    public sealed class LocalOrderQueue
    {
        private readonly List<OrderInstance> _orders;
        private readonly EventBus _events;
        private readonly RunClock _clock;
        private readonly RuntimeTuning _tuning;

        public LocalOrderQueue(EventBus events, RunClock clock, RuntimeTuning tuning, int capacity = 8)
        {
            _events = events;
            _clock = clock;
            _tuning = tuning;
            _orders = new List<OrderInstance>(capacity);
        }

        public int ActiveCount => _orders.Count;
        public OrderInstance this[int index] => _orders[index];

        public void Add(OrderInstance order)
        {
            _orders.Add(order);
            _events.Publish(new OrderCreated(order.OrderId, order.Dish.Id, order.Customer.Id));
        }

        public void SubmitIngredient(IngredientDefinition ingredient, int count)
        {
            for (int n = 0; n < count; n++)
            {
                OrderInstance target = FindTarget(ingredient);
                if (target == null)
                {
                    // Discarded, with feedback. A pantry is a Section 3 idea (plan decision D3).
                    _events.Publish(new IngredientWasted(ingredient.Id, Vector2.zero));
                    return;
                }

                Credit(target, ingredient);
                if (target.IsComplete) Serve(target);
            }
        }

        /// <summary>Oldest customer first; tie-break on fewest units remaining, so nearly-done
        /// dishes finish first.</summary>
        private OrderInstance FindTarget(IngredientDefinition ingredient)
        {
            OrderInstance best = null;

            for (int i = 0; i < _orders.Count; i++)
            {
                OrderInstance o = _orders[i];
                if (o.State == PatienceState.Served || o.State == PatienceState.Expired) continue;
                if (!o.Needs(ingredient)) continue;

                if (best == null
                    || o.SpawnTime < best.SpawnTime
                    || (Mathf.Approximately(o.SpawnTime, best.SpawnTime) && o.RemainingUnits < best.RemainingUnits))
                    best = o;
            }

            return best;
        }

        private void Credit(OrderInstance order, IngredientDefinition ingredient)
        {
            for (int i = 0; i < order.Lines.Length; i++)
            {
                OrderLine line = order.Lines[i];
                if (line.Ingredient != ingredient || line.Satisfied) continue;

                line.Fulfilled++;
                _events.Publish(new OrderProgressed(order.OrderId, ingredient.Id, line.Fulfilled, line.Required));
                return;
            }
        }

        /// <summary>Driven by <see cref="RunClock"/>, so §4 can scale patience and §5 can pause a
        /// round without touching the physics step.</summary>
        public void Tick()
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                OrderInstance o = _orders[i];
                if (o.State == PatienceState.Served) continue;

                PatienceSystem.Tick(o, _clock.DeltaTime, _tuning);
                if (o.State != PatienceState.Expired) continue;

                _orders.RemoveAt(i);
                _events.Publish(new OrderExpired(o.OrderId, o.Customer.Id));
            }
        }

        private void Serve(OrderInstance order)
        {
            order.State = PatienceState.Served;
            _orders.Remove(order);

            int cash = Mathf.RoundToInt(order.Dish.BaseCash * order.Customer.TipMultiplier * _tuning.TotalCashMultiplier);
            _events.Publish(new OrderServed(order.OrderId, cash));
        }
    }
}
```

5. Create `Assets/Scripts/Runtime/Orders/OrderSpawner.cs`:

```csharp
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Orders
{
    public sealed class OrderSpawner : MonoBehaviour
    {
        [SerializeField] private DishDefinition[] dishes;
        [SerializeField] private CustomerDefinition[] customers;

        private LocalOrderQueue _queue;
        private EventBus _events;
        private RunClock _clock;
        private RuntimeTuning _tuning;
        private IDifficultyProvider _difficulty;
        private IngredientSpawner _board;
        private ReputationService _reputation;

        private float[] _weights;
        private float _nextSpawnTime;
        private int _nextOrderId = 1;

        public void Initialize(LocalOrderQueue queue, EventBus events, RunClock clock, RuntimeTuning tuning,
                               IDifficultyProvider difficulty, IngredientSpawner board, ReputationService reputation)
        {
            _queue = queue;
            _events = events;
            _clock = clock;
            _tuning = tuning;
            _difficulty = difficulty;
            _board = board;
            _reputation = reputation;

            _weights = new float[dishes.Length];   // sized once: no per-spawn allocation
            _nextSpawnTime = 0f;
        }

        private void Update()
        {
            if (_queue == null || _reputation.IsGameOver) return;

            _queue.Tick();

            if (_queue.ActiveCount >= _tuning.MaxActiveOrders) return;

            _nextSpawnTime -= _clock.DeltaTime;
            if (_nextSpawnTime > 0f) return;

            _nextSpawnTime = _tuning.OrderSpawnInterval * _difficulty.OrderSpawnIntervalScale;
            Spawn();
        }

        private void Spawn()
        {
            if (dishes.Length == 0 || customers.Length == 0) return;

            DishDefinition dish = PickDish();
            if (dish == null) return;

            CustomerDefinition customer = customers[Random.Range(0, customers.Length)];

            var lines = new OrderLine[dish.Ingredients.Count];
            for (int i = 0; i < lines.Length; i++)
            {
                IngredientRequirement r = dish.Ingredients[i];
                lines[i] = new OrderLine { Ingredient = r.ingredient, Required = r.count, Fulfilled = 0 };
            }

            float duration = PatienceSystem.ResolveDuration(_tuning, customer, _difficulty);
            _queue.Add(new OrderInstance
            {
                OrderId = _nextOrderId++,
                Dish = dish,
                Customer = customer,
                Lines = lines,
                SpawnTime = Time.time,
                PatienceDuration = duration,
                RemainingPatience = duration
            });
        }

        /// <summary>Weighted by baseOrderWeight / (1 + missingIngredientPenalty) so the run is not
        /// lost to RNG — a small, tunable bias, never a hard rule (plan §6.2).</summary>
        private DishDefinition PickDish()
        {
            float total = 0f;
            for (int i = 0; i < dishes.Length; i++)
            {
                float weight = dishes[i].BaseOrderWeight / (1f + MissingPenalty(dishes[i]));
                _weights[i] = weight;
                total += weight;
            }
            if (total <= 0f) return dishes[0];

            float r = Random.value * total;
            for (int i = 0; i < dishes.Length; i++)
            {
                r -= _weights[i];
                if (r <= 0f) return dishes[i];
            }
            return dishes[dishes.Length - 1];
        }

        private float MissingPenalty(DishDefinition dish)
        {
            const float perMissingIngredient = 0.35f;

            int missing = 0;
            for (int i = 0; i < dish.Ingredients.Count; i++)
                if (!_board.HasLive(dish.Ingredients[i].ingredient)) missing++;

            return missing * perMissingIngredient;
        }
    }
}
```

6. Create `Assets/Scripts/Runtime/Orders/ReputationService.cs`:

```csharp
using SliceNServe.Core;

namespace SliceNServe.Orders
{
    /// <summary>
    /// Run-level lives. Plan decision D6: the failure consequence is reputation rather than lost
    /// revenue, because it is legible and it is what Section 3's upgrades protect.
    /// </summary>
    public sealed class ReputationService
    {
        private readonly EventBus _events;

        public int Reputation { get; private set; }
        public bool IsGameOver => Reputation <= 0;

        public ReputationService(EventBus events, RuntimeTuning tuning)
        {
            _events = events;
            Reputation = tuning.StartReputation;
            _events.Publish(new ReputationChanged(Reputation));
        }

        public void Penalise()
        {
            if (IsGameOver) return;

            Reputation--;
            _events.Publish(new ReputationChanged(Reputation));

            if (IsGameOver) _events.Publish(new GameOver((int)GameOverReason.ReputationDepleted));
        }
    }
}
```

7. Create assets:
   - `Assets/ScriptableObjects/Dishes/`: `Dish_Salad` (`salad`: 2 × lettuce, 1 × tomato, `baseCash` 14),
     `Dish_Soup` (`soup`: 1 × mushroom, 1 × onion, `baseCash` 12),
     `Dish_Pizza` (`pizza`: 1 × dough, 1 × cheese, 1 × tomato, `baseCash` 24),
     `Dish_Toast` (`toast`: 1 × dough, 1 × cheese, `baseCash` 16).
   - `Assets/ScriptableObjects/Customers/`: `Customer_Patient` (`patient`, `patienceMultiplier` 1.4),
     `Customer_Average` (`average`, 1.0), `Customer_Impatient` (`impatient`, 0.6).
8. In `Game.unity`: add `OrderSpawner` with the four dishes and three customers.

**Verify:** Play mode spawns up to 4 orders at ~6 s intervals.

---

## Step 10 — HUD and the run wiring

**Goal:** make the loop visible and close it. (Phase 5, plan §6.5–6.6)

1. Create `Assets/Scripts/Runtime/Core/GameRunner.cs` — the one place that constructs the run and
   binds the systems, so nothing else has to know the wiring order:

```csharp
using SliceNServe.Board;
using SliceNServe.Data;
using SliceNServe.Orders;
using UnityEngine;

namespace SliceNServe.Core
{
    /// <summary>
    /// Builds the run from the services GameBootstrap installed. Ordering matters: the board
    /// bounds must exist before the knife (the knife needs the play area) and the ingredient
    /// spawner must exist before the knife (the knife needs the registry).
    /// </summary>
    public sealed class GameRunner : MonoBehaviour
    {
        [SerializeField] private GameBootstrap bootstrap;
        [SerializeField] private BoardBounds bounds;
        [SerializeField] private IngredientSpawner ingredients;
        [SerializeField] private KnifeManager knives;
        [SerializeField] private AimInput aimInput;
        [SerializeField] private TrajectoryPreview trajectoryPreview;
        [SerializeField] private OrderSpawner orderSpawner;

        public LocalOrderQueue Queue { get; private set; }
        public ReputationService Reputation { get; private set; }

        private void Start()
        {
            ServiceRegistry services = bootstrap != null ? bootstrap.Services : ServiceRegistry.Current;
            if (services == null)
            {
                Debug.LogError("GameRunner: GameBootstrap has not run. Add it to the scene.", this);
                return;
            }

            BoardConfig config = services.Tuning.Config;
            RuntimeTuning tuning = services.Tuning;

            bounds.Initialize(config);
            ingredients.Initialize(tuning, services.Events);
            knives.Initialize(tuning, services.Events, bounds, ingredients);

            aimInput.Initialize(tuning);
            trajectoryPreview.Initialize(aimInput, tuning);
            aimInput.OnLaunch += dragWorld =>
                knives.Launch(KnifeLauncher.ResolveVelocity(dragWorld, tuning), LaunchSource.Manual);

            Queue = new LocalOrderQueue(services.Events, services.Clock, tuning);
            Reputation = new ReputationService(services.Events, tuning);
            var difficulty = new ConstantDifficultyProvider();

            orderSpawner.Initialize(Queue, services.Events, services.Clock, tuning, difficulty, ingredients, Reputation);
        }
    }
}
```

2. In `Game.unity`: create `Runner` with `GameRunner`, and assign `bootstrap`, `bounds`,
   `ingredients`, `knives`, `aimInput`, `trajectoryPreview`, `orderSpawner`.
   `KnifeManager` needs `knifePrefab` and `launcherAnchor` (an empty `Launcher` GameObject at
   `(0, -4.4, 0)`, matching `BoardConfig.launcherPosition`). `AimInput` and `TrajectoryPreview`
   need the `Main Camera` and a `LineRenderer` (unlit sprite material, width `0.05`,
   `useWorldSpace` ✔) respectively.
3. Build the HUD: a `Canvas` (`Screen Space - Overlay`), a `CanvasScaler` (`Scale With Screen Size`,
   reference `1920 × 1080`, match `0.5`), and a `StatusBar` with two `TMP_Text` fields.
   Section 1's runbook adds `SafeAreaFitter` to this canvas — if it has already run, put the HUD
   under the safe-area panel it created. Either way, **do not invent a second scaling rule.**
   One-time: `Window > TextMeshPro > Import TMP Essential Resources`.
4. Create `Assets/Scripts/Runtime/UI/PatienceBar.cs`, `CashReadout.cs` and `ReputationReadout.cs`:

```csharp
using SliceNServe.Data;
using SliceNServe.Orders;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    public sealed class PatienceBar : MonoBehaviour
    {
        [SerializeField] private Image fill;
        [SerializeField] private RectTransform shakeTarget;

        private static readonly Color NormalColor = new Color(0.25f, 0.85f, 0.35f);
        private static readonly Color HurryColor = new Color(0.95f, 0.70f, 0.15f);
        private static readonly Color CriticalColor = new Color(0.90f, 0.20f, 0.20f);

        private PatienceState _state = PatienceState.Normal;

        public void SetFraction(float fraction, PatienceState state)
        {
            _state = state;
            fill.fillAmount = Mathf.Clamp01(fraction);
            fill.color = state switch
            {
                PatienceState.Critical => CriticalColor,
                PatienceState.Hurry => HurryColor,
                _ => NormalColor
            };
        }

        public void SetFrom(OrderInstance order)
        {
            float fraction = order.PatienceDuration <= 0f ? 0f : order.RemainingPatience / order.PatienceDuration;
            SetFraction(fraction, order.State);
        }

        private void Update()
        {
            if (shakeTarget == null) return;

            // Purely presentational: the bar shakes only while the order is critical.
            float amp = _state == PatienceState.Critical ? 3f : 0f;
            shakeTarget.anchoredPosition = amp > 0f
                ? new Vector2(Mathf.Sin(Time.time * 30f) * amp, 0f)
                : Vector2.zero;
        }
    }
}
```

```csharp
using SliceNServe.Core;
using TMPro;
using UnityEngine;

namespace SliceNServe.UI
{
    public sealed class CashReadout : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        private Wallet _wallet;

        public void Initialize(Wallet wallet)
        {
            _wallet = wallet;
            _wallet.Changed += OnChanged;
            OnChanged(_wallet.Amount);
        }

        private void OnDestroy()
        {
            if (_wallet != null) _wallet.Changed -= OnChanged;
        }

        private void OnChanged(int amount) => label.SetText("${0}", amount);
    }

    public sealed class ReputationReadout : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        private EventBus _events;

        public void Initialize(EventBus events)
        {
            _events = events;
            _events.Subscribe<ReputationChanged>(OnChanged);
        }

        private void OnDestroy() => _events?.Unsubscribe<ReputationChanged>(OnChanged);

        private void OnChanged(ReputationChanged e) => label.SetText("Rep {0}", e.Reputation);
    }
}
```

5. Create `Assets/Scripts/Runtime/UI/OrderTicketHud.cs` and the `OrderTicket` prefab:

```csharp
using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Orders;
using TMPro;
using UnityEngine;

namespace SliceNServe.UI
{
    /// <summary>
    /// Rebuilds the ticket strip from the queue. It polls positions but writes only on change, so
    /// the stack stays correct without a per-frame UI cost. The service layer never knows it exists.
    /// </summary>
    public sealed class OrderTicketHud : MonoBehaviour
    {
        [SerializeField] private RectTransform container;
        [SerializeField] private OrderTicketView ticketPrefab;
        [SerializeField] private CashReadout cash;
        [SerializeField] private ReputationReadout reputation;

        private readonly List<OrderTicketView> _tickets = new List<OrderTicketView>(8);

        private LocalOrderQueue _queue;
        private EventBus _events;

        public void Initialize(LocalOrderQueue queue, EventBus events, Wallet wallet, RuntimeTuning tuning)
        {
            _queue = queue;
            _events = events;

            _events.Subscribe<OrderCreated>(OnCreated);
            _events.Subscribe<OrderServed>(OnClosed);
            _events.Subscribe<OrderExpired>(OnClosed);
            _events.Subscribe<OrderProgressed>(OnProgressed);

            cash.Initialize(wallet);
            reputation.Initialize(events);
        }

        private void OnDestroy()
        {
            if (_events == null) return;

            _events.Unsubscribe<OrderCreated>(OnCreated);
            _events.Unsubscribe<OrderServed>(OnClosed);
            _events.Unsubscribe<OrderExpired>(OnClosed);
            _events.Unsubscribe<OrderProgressed>(OnProgressed);
        }

        private void OnCreated(OrderCreated e)
        {
            OrderTicketView ticket = Instantiate(ticketPrefab, container);
            for (int i = 0; i < _queue.ActiveCount; i++)
            {
                if (_queue[i].OrderId != e.OrderId) continue;

                ticket.Bind(_queue[i]);
                break;
            }
            _tickets.Add(ticket);
        }

        private void OnProgressed(OrderProgressed e)
        {
            for (int i = 0; i < _tickets.Count; i++)
            {
                if (_tickets[i].OrderId != e.OrderId) continue;

                _tickets[i].SetLine(e.IngredientId, e.Fulfilled, e.Required);
                return;
            }
        }

        private void OnClosed(OrderServed e) => Close(e.OrderId);
        private void OnClosed(OrderExpired e) => Close(e.OrderId);

        private void Close(int orderId)
        {
            for (int i = 0; i < _tickets.Count; i++)
            {
                if (_tickets[i].OrderId != orderId) continue;

                Destroy(_tickets[i].gameObject);
                _tickets.RemoveAt(i);
                return;
            }
        }

        private void Update()
        {
            // Patience is the one thing that changes continuously, so it is the one thing polled.
            for (int i = 0; i < _tickets.Count; i++) _tickets[i].RefreshPatience();
        }
    }
}
```

```csharp
using SliceNServe.Orders;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    public sealed class OrderTicketView : MonoBehaviour
    {
        [SerializeField] private Image dishIcon;
        [SerializeField] private Image portrait;
        [SerializeField] private PatienceBar patience;
        [SerializeField] private TMP_Text ingredientList;

        private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(128);

        private OrderInstance _order;

        public int OrderId => _order != null ? _order.OrderId : 0;

        public void Bind(OrderInstance order)
        {
            _order = order;
            dishIcon.sprite = order.Dish != null ? order.Dish.Sprite : null;
            portrait.sprite = order.Customer != null && order.Customer.SpriteSet.Length > 0
                ? order.Customer.SpriteSet[0]
                : null;
            RefreshPatience();
            RefreshList();
        }

        public void RefreshPatience()
        {
            if (_order != null) patience.SetFrom(_order);
        }

        public void SetLine(string ingredientId, int fulfilled, int required) => RefreshList();

        private void RefreshList()
        {
            if (_order == null) return;

            _sb.Clear();
            for (int i = 0; i < _order.Lines.Length; i++)
            {
                if (i > 0) _sb.Append('\n');

                OrderLine line = _order.Lines[i];
                _sb.Append(line.Ingredient.DisplayName)
                   .Append(' ')
                   .Append(line.Fulfilled)
                   .Append('/')
                   .Append(line.Required);
                if (line.Satisfied) _sb.Append(" ✓");
            }
            ingredientList.SetText(_sb);
        }
    }
}
```

   Create `Assets/Prefabs/OrderTicket.prefab` from a `OrderTicketView` with a dish `Image`, a
   portrait `Image`, a `PatienceBar`, and an ingredient-list `TMP_Text`, then assign the prefab plus
   `container`, `cash` and `reputation` on `OrderTicketHud`, and assign the HUD to `GameRunner`.

**Verify:** Play mode shows one ticket per active order, each with a bar that drains, turns amber,
then red, then disappears. Serving adds to the cash readout; an expiry decrements the reputation
readout. At 0 reputation, spawning stops and `GameOver` is published (log it to confirm).

---

## Step 11 — Tests

**Goal:** prove the logic without a human watching the screen. (plan §10)

1. Create `Assets/Scripts/Tests/EditMode/`:
   - `AimMathTests.cs` — drag length → power across `[launchMinSpeed, launchMaxSpeed]`; the
     dead zone; slingshot direction (`-drag.normalized`); `ClampAwayFromParallelWalls` lifting a
     near-parallel direction above 4°.
   - `OrderCreditTests.cs` — oldest-demander targeting; tie-break on fewest remaining units;
     multi-unit recipes; exactly one `IngredientWasted` when nothing needs the ingredient; a served
     order is never credited again; a completed order leaves the queue.
   - `PatienceTests.cs` — `base × customer × difficulty`; band transitions at **exactly** 60 % and
     30 %; expiry at exactly 0; an expired order stops depleting and never serves; a paused clock
     yields zero depletion.
   - `DifficultyTests.cs` — `ConstantDifficultyProvider` returns 1.0; a fake provider changes the
     duration at spawn time and **not** for already-spawned orders.
   - `SpawnTableTests.cs` — a seeded RNG produces the expected weighted distribution within
     tolerance, and `spawnWeight = 0` never appears.

   These construct `LocalOrderQueue`, `OrderInstance`, `PatienceSystem` and `KnifeLauncher`
   **directly — no scene, no MonoBehaviour**. That is only possible because Steps 7 and 9 kept them
   plain classes and static methods. It is the point of the design, not an accident.
2. Create `Assets/Scripts/Tests/PlayMode/BoardPhysicsTests.cs` using a dedicated test scene plus
   `Physics2D.simulationMode = SimulationMode2D.Script` and manual `physicsScene.Simulate(0.02f)`
   stepping, so results are editor-independent and fast:
   - **Tunneling guard** — a launch at 40 u/s over 1,000 steps never leaves `worldBounds` by more
     than a collider radius.
   - **Collection at speed** — an ingredient trigger placed on a 40 u/s path is collected **in the
     single step that crosses it**. This is the test that fails the moment somebody replaces the
     swept cast with `OnTriggerEnter2D`.
   - **Energy math** — the speed after N synthetic bounces equals `v × 0.90^N` within epsilon.
   - **Anti-stall** — a launched knife always reaches a terminal state within `maxKnifeLifetime + 1 s`
     of simulated time.
3. Run both platforms:

```
"C:\Program Files\Unity\Hub\Editor\6000.3.16f1\Editor\Unity.exe" -batchmode -quit -nographics \
  -projectPath "C:/study/unity projects/Slice n Serve" \
  -runTests -testPlatform EditMode -testResults editmode.xml -logFile -
```

**Verify:** every new fixture passes, and Section 1's `ViewportMathTests` (if it has landed) still
passes.

> Assert **invariants and bounds, never exact trajectories.** Box2D is deterministic for a given
> binary and platform, not universally, so exact-position assertions are how this suite rots.

---

## Step 12 — Verification and commit

**Goal:** confirm the plan's §12 definition of done, then record it.

1. **The loop.** In `Game.unity`: drag and release → the knife launches, bounces and slices →
   ingredients credit the **oldest** demanding customer → the bar drains and bands at 60 %/30 % →
   completion adds cash → expiry decrements reputation.
2. **Input parity.** The same drag works with the mouse and with touch (use the Device Simulator),
   and the aim is not occluded by the finger.
3. **Feel.** A full-power launch lasts ~10–15 s, never escapes the board, and never gets stuck on a
   wall or in a loop.
4. **Aspect.** Check 16:9, 16:10, 4:3 and 21:9. The board box must never be cropped, and once
   Section 1's `BoardViewportAdapter` and `SafeAreaFitter` are in, the HUD must stay inside the safe
   area.
5. **No regressions on the physics decisions.** Re-read this list against the project:
   `Fixed Timestep` still `0.02`; gravity `(0, 0)`; layers 8–11 named `Knife`/`Ingredient`/
   `BoardWall`/`Obstacle`; `Knife ↔ Ingredient` still **unchecked**.
6. **Console.** Clean for a full run — no warnings, no leftover `Debug.Log`.
7. **Commit:**

```bash
git add -A
git commit -m "Section 2: core mechanics (board, knife, ingredients, orders, patience)"
```

**Verify:** `git status --short` is empty; the Console is clean; both test suites are green.

# Section 1 — Implementation Runbook

> Plan: [`section-1-game-overview.md`](./section-1-game-overview.md). This file is the *how*.
> Sibling: [`section-2-core-mechanics.md`](./section-2-core-mechanics.md) owns **Phase 0
> Foundations** — folders, assembly definitions, layers, `Physics2D` settings, materials,
> `Game.unity`, the `Board` action map, `BoardConfig`. Its runbook is
> [`section-2-implementation-steps.md`](./section-2-implementation-steps.md).
>
> **Nothing here has been applied to the project yet.** Every step is performed by hand in the Unity
> Editor or a shell.
>
> Unity Editor version: **6000.3.16f1**.
>
> **Scope.** §1 adds exactly three things to §2's Phase 0 — `ViewportMath` + `BoardViewportAdapter`,
> `SafeAreaFitter`, and the player/platform settings. Do **not** create the folder tree, assemblies,
> layers, `Physics2D` settings or `Game.unity` from this document; those are §2 Phase 0, and
> duplicating them is how the two sections ended up describing two different projects.

---

## Step 0 — Preflight

1. Confirm the Editor version is `6000.3.16f1` (`ProjectSettings/ProjectVersion.txt`). Opening the project with a different version silently rewrites `ProjectSettings/`, producing a large meaningless diff.
2. Confirm these modules are installed (Unity Hub → Installs → gear → Add Modules):
   - **Android Build Support** + *OpenJDK* + *Android SDK & NDK Tools* — required by Step 5.4.
   - **Windows Build Support (IL2CPP)** — required by Step 5.4's IL2CPP build.
   - iOS Build Support is **not** required: §1 configures iOS but does not build it.
3. Confirm the template import is already committed — `git status --short` should be empty. It was committed as `create GameManager` alongside an unrelated stub; there is nothing left to commit before starting.

**Verify:** the version matches, the modules are present, and `git status --short` is empty.

---

## Step 1 — Platform & player settings

**Goal:** landscape-only, correctly identified builds on both platforms. (F2)

Open `Project Settings → Player`.

### 1.1 Resolution and Presentation — the important one

| Setting | Value |
| --- | --- |
| **Default Orientation** | **Landscape Left** |
| **Allowed Orientations for Auto Rotation** | **only** `Landscape Left` + `Landscape Right` — uncheck `Portrait` and `Portrait Upside Down` |
| Default Is Native Resolution | **off** |
| Default Screen Width / Height | `1920` / `1080` |
| Resizable Window | **on** (so the aspect policy is testable by dragging the window edge) |
| Fullscreen Mode | `Fullscreen Window` |
| Allow Fullscreen Switch | on |
| Run In Background | on (dev convenience; revisit at ship) |

> **This is the highest-value change in Section 1.** The project currently has
> `defaultScreenOrientation: 4` — Auto Rotation with all four orientations allowed. On a phone that
> renders a portrait frame around a 19.2 × 10.8 landscape board: a broken build, not a layout bug.
>
> Keep **both** landscape orientations rather than hard-locking `Landscape Left`. A player holding
> the phone with the charging port on the right would otherwise see the game upside down.

### 1.2 Shared identity

| Setting | Value | Note |
| --- | --- | --- |
| Company Name | your studio name | currently `DefaultCompany` — a release-blocking placeholder |
| Product Name | `Slice & Serve` | already correct; window title and Android app label |
| Bundle Version | `0.1.0` | not a 1.0 yet |
| Application Identifier (Android) | `com.<studio>.slicenserve` | currently empty |
| Application Identifier (iOS) | `com.<studio>.slicenserve` | currently empty |

### 1.3 Other Settings

| Setting | Value | Why |
| --- | --- | --- |
| Color Space | **Linear** | already set (`m_ActiveColorSpace: 1`); URP requires it and Gamma renders 2D lights visibly wrong |
| Auto Graphics API | on | D3D11/12 on Windows, Vulkan + GLES3 on Android |
| Scripting Backend | **IL2CPP** on Android and iOS; Windows may stay `Mono` for iteration speed | IL2CPP is mandatory for ARM64 |
| Api Compatibility Level | `.NET Standard 2.1` | modern default; nothing here needs `.NET Framework` |
| Managed Stripping Level | `Low` now, `Medium` at ship | aggressive stripping plus reflection is a classic shipping-week bug |
| **Incremental GC** | **on** | required to have any chance at the zero-allocation budget (§8) |
| Active Input Handling | `Input System Package (New)` | already correct (`activeInputHandler: 1`) |
| Prebake Collision Meshes | off | no meshes |

### 1.4 Android tab

| Setting | Value | Note |
| --- | --- | --- |
| Minimum API Level | `25` (Android 7.1) | already set |
| Target API Level | `Automatic (highest installed)` | the Play Store requires a recent target |
| **Target Architectures** | **ARM64 only** | already `AndroidTargetArchitectures: 2` |
| Scripting Backend | IL2CPP | required for ARM64 |
| **Render Outside Safe Area** | **on** (keep the current value) | the board and backdrop should fill the physical display; only the *UI* respects the safe area — that is what `SafeAreaFitter` (Step 4) is for |
| Start In Fullscreen | on | already set |
| Optimized Frame Pacing | on | steadier frame delivery on mid-tier devices |

> Do **not** tick `ARMv7` "just in case". Google Play requires 64-bit, ARMv7 doubles build time, and
> no device in the supported range needs it.

### 1.5 iOS tab

| Setting | Value |
| --- | --- |
| Target minimum iOS Version | `13.0` (replace the template default `988`) |
| Target Device | `iPhone + iPad` |
| Scripting Backend | IL2CPP |
| Target SDK | `Device SDK` |
| Signing Team ID | leave empty — §1 does not build iOS |

**Verify:** switch the active build target to **Android** (`File → Build Profiles`) and let the Editor
recompile. The Console must show no *"scripting backend not supported"* or *"architecture"* warnings.
Then confirm `ProjectSettings/ProjectSettings.asset` no longer has `defaultScreenOrientation: 4`.

---

## Step 2 — `ViewportMath` + tests

**Goal:** the aspect policy as pure, testable maths. (F3)

This is the one piece of §1 with real logic in it, so it is written first and tested first.

### 2.1 `Assets/Scripts/Runtime/Core/ViewportMath.cs`

```csharp
using UnityEngine;

namespace SliceNServe.Core
{
    /// <summary>
    /// The Section 1 aspect policy, as pure math: contain-fit the board box, clamp to the
    /// supported envelope, letterbox outside it. No scene and no camera, which is exactly why
    /// it can be covered by millisecond EditMode tests.
    ///
    /// The camera is the only caller — see BoardViewportAdapter.
    /// </summary>
    public static class ViewportMath
    {
        /// <summary>Narrowest supported aspect: 4:3 tablets.</summary>
        public const float MinAspect = 4f / 3f;

        /// <summary>Widest supported aspect: 21:9 ultrawide. Beyond this we pillarbox, so
        /// nobody can see more board than a 16:9 player.</summary>
        public const float MaxAspect = 21f / 9f;

        public static float EffectiveAspect(float screenAspect,
                                            float minAspect = MinAspect,
                                            float maxAspect = MaxAspect)
            => Mathf.Clamp(screenAspect, minAspect, maxAspect);

        /// <summary>
        /// Orthographic size that *contains* the whole board box at this aspect. The board is
        /// never cropped; leftover space becomes backdrop and HUD rails.
        /// Evaluates to exactly 5.4 for the 19.2 x 10.8 board at 16:9 — Section 2's reference
        /// camera value — so adopting this policy costs Section 2 nothing.
        /// </summary>
        public static float OrthographicSize(float boardHalfWidth, float boardHalfHeight,
                                             float effectiveAspect)
            => Mathf.Max(boardHalfHeight, boardHalfWidth / effectiveAspect);

        /// <summary>
        /// The <see cref="Camera.rect"/> that letterboxes the clamped aspect inside the real
        /// viewport. Full screen inside the supported envelope.
        /// </summary>
        public static Rect LetterboxRect(float screenAspect, float effectiveAspect)
        {
            if (screenAspect > effectiveAspect)
            {
                // Too wide: pillarbox.
                float width = effectiveAspect / screenAspect;
                return new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }

            // Too tall: letterbox.
            float height = screenAspect / effectiveAspect;
            return new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }
    }
}
```

### 2.2 `Assets/Scripts/Tests/EditMode/ViewportMathTests.cs`

The `SliceNServe.Tests.EditMode` assembly is created by §2 Phase 0 — this only adds a file to it.

```csharp
using NUnit.Framework;
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Tests
{
    public sealed class ViewportMathTests
    {
        private const float HalfWidth = 9.6f;   // 19.2-unit board box (BoardConfig.worldBounds)
        private const float HalfHeight = 5.4f;  // 10.8-unit board box
        private const float Tolerance = 1e-4f;

        /// <summary>The table in the plan, Section 6.3, as executable assertions.</summary>
        [TestCase(4f / 3f, 7.2f)]       // 4:3     -> height-bound
        [TestCase(3f / 2f, 6.4f)]       // 3:2
        [TestCase(16f / 10f, 6f)]       // 16:10
        [TestCase(16f / 9f, 5.4f)]      // 16:9    -> exactly Section 2's reference camera
        [TestCase(19.5f / 9f, 5.4f)]    // tall phone
        [TestCase(20f / 9f, 5.4f)]      // taller phone
        [TestCase(21f / 9f, 5.4f)]      // ultrawide edge
        public void OrthographicSize_MatchesTheDocumentedAspectTable(float aspect, float expected)
        {
            float effective = ViewportMath.EffectiveAspect(aspect);
            float size = ViewportMath.OrthographicSize(HalfWidth, HalfHeight, effective);
            Assert.That(size, Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void OrthographicSize_NeverCropsTheBoard_AtAnyRealScreen()
        {
            float[] aspects = { 0.5f, 1f, 4f / 3f, 1.5f, 16f / 10f, 16f / 9f, 19.5f / 9f, 21f / 9f, 32f / 9f };

            foreach (float aspect in aspects)
            {
                float effective = ViewportMath.EffectiveAspect(aspect);
                float size = ViewportMath.OrthographicSize(HalfWidth, HalfHeight, effective);

                Assert.That(size * effective, Is.GreaterThanOrEqualTo(HalfWidth - Tolerance),
                    $"board width cropped at aspect {aspect}");
                Assert.That(size, Is.GreaterThanOrEqualTo(HalfHeight - Tolerance),
                    $"board height cropped at aspect {aspect}");
            }
        }

        [Test]
        public void EffectiveAspect_ClampsToTheSupportedEnvelope()
        {
            Assert.That(ViewportMath.EffectiveAspect(32f / 9f), Is.EqualTo(ViewportMath.MaxAspect));
            Assert.That(ViewportMath.EffectiveAspect(0.5f), Is.EqualTo(ViewportMath.MinAspect));
            Assert.That(ViewportMath.EffectiveAspect(16f / 9f), Is.EqualTo(16f / 9f).Within(Tolerance));
        }

        [Test]
        public void LetterboxRect_IsTheFullViewportInsideTheEnvelope()
        {
            Rect rect = ViewportMath.LetterboxRect(16f / 9f, ViewportMath.EffectiveAspect(16f / 9f));
            Assert.That(rect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
        }

        [Test]
        public void LetterboxRect_PillarboxesBeyondTheWidestSupportedAspect()
        {
            const float screen = 32f / 9f;
            float effective = ViewportMath.EffectiveAspect(screen);
            Rect rect = ViewportMath.LetterboxRect(screen, effective);

            Assert.That(rect.height, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(rect.width, Is.EqualTo(effective / screen).Within(Tolerance));
            Assert.That(rect.x, Is.EqualTo((1f - rect.width) * 0.5f).Within(Tolerance));
            Assert.That(rect.width, Is.LessThan(1f), "ultrawide must not see more board");
        }

        [Test]
        public void LetterboxRect_LetterboxesBelowTheNarrowestSupportedAspect()
        {
            const float screen = 1f;
            float effective = ViewportMath.EffectiveAspect(screen);
            Rect rect = ViewportMath.LetterboxRect(screen, effective);

            Assert.That(rect.width, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(rect.height, Is.EqualTo(screen / effective).Within(Tolerance));
            Assert.That(rect.y, Is.EqualTo((1f - rect.height) * 0.5f).Within(Tolerance));
        }
    }
}
```

**Verify:** `Window → General → Test Runner → EditMode → Run All` — all green.

---

## Step 3 — Camera adapter

**Goal:** one — and only one — writer of the camera's orthographic size and rect. (F4)

### 3.1 Precondition

`BoardViewportAdapter` reads `SliceNServe.Data.BoardConfig`, which §2 Phase 0 creates.

- **If Phase 0 has landed:** reference the existing asset.
- **If you are doing §1 first:** create `Assets/Scripts/Runtime/Data/BoardConfig.cs` with only the field §1 needs. §2 extends this same class with its tuning fields — **do not create a second config type.**

```csharp
using UnityEngine;

namespace SliceNServe.Data
{
    /// <summary>
    /// Board tuning. Section 2 owns the full set (speeds, dead-zone, spawn counts, patience);
    /// Section 1 only needs the play-area rectangle, and creates this class early so the camera
    /// adapter has something to read. Extend this file — do not duplicate it.
    /// </summary>
    [CreateAssetMenu(menuName = "Slice & Serve/Board Config", fileName = "BoardConfig")]
    public sealed class BoardConfig : ScriptableObject
    {
        [Tooltip("Play area in world units, centred on the origin. Section 2, §5.1.")]
        public Rect worldBounds = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);
    }
}
```

Create the asset: right-click in `Assets/ScriptableObjects/` → **Create → Slice & Serve → Board
Config**, name it `BoardConfig`, and leave the default `worldBounds`.

### 3.2 `Assets/Scripts/Runtime/Board/BoardViewportAdapter.cs`

```csharp
using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Applies the Section 1 aspect policy to the board camera: contain-fit the board box,
    /// clamp to [4:3 .. 21:9], letterbox outside it.
    ///
    /// This is the ONLY component that writes Camera.orthographicSize or Camera.rect. Section 2,
    /// §4.4 specifies orthographicSize = 5.4 for Game.unity's camera; that value is exactly what
    /// this component computes at 16:9, so it is the reference, not a competing source of truth.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public sealed class BoardViewportAdapter : MonoBehaviour
    {
        /// <summary>Used only when no BoardConfig is assigned, so the Scene view still frames
        /// the board correctly before the asset is wired up.</summary>
        private static readonly Rect FallbackBounds = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);

        [SerializeField] private BoardConfig boardConfig;

        private Camera _camera;
        private int _lastWidth = -1;
        private int _lastHeight = -1;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            Apply();
        }

        private void LateUpdate()
        {
            if (Screen.width == _lastWidth && Screen.height == _lastHeight) return;
            Apply();
        }

        public void Apply()
        {
            if (_camera == null) _camera = GetComponent<Camera>();

            // Guard against a zero height during a window resize, and in headless builds.
            if (Screen.height <= 0 || Screen.width <= 0) return;

            _lastWidth = Screen.width;
            _lastHeight = Screen.height;

            Rect bounds = boardConfig != null ? boardConfig.worldBounds : FallbackBounds;

            // Keep the board centred without touching the camera's z (which sets its depth).
            Vector3 position = transform.position;
            if (position.x != bounds.center.x || position.y != bounds.center.y)
                transform.position = new Vector3(bounds.center.x, bounds.center.y, position.z);

            float screenAspect = (float)Screen.width / Screen.height;
            float effectiveAspect = ViewportMath.EffectiveAspect(screenAspect);

            _camera.orthographic = true;
            _camera.orthographicSize = ViewportMath.OrthographicSize(
                bounds.width * 0.5f, bounds.height * 0.5f, effectiveAspect);
            _camera.rect = ViewportMath.LetterboxRect(screenAspect, effectiveAspect);
        }
    }
}
```

> `[ExecuteAlways]` is deliberate: the policy must be verifiable in the Scene view while dragging the
> Game view's aspect dropdown, not only in Play mode.

### 3.3 Attach it

1. Open `Assets/Scenes/Game.unity` (created by §2 Phase 0; if §1 runs first, create it with a single orthographic `Main Camera` at `(0, 0, -10)`).
2. Add `BoardViewportAdapter` to the `Main Camera`.
3. Assign the `BoardConfig` asset.
4. Leave the camera's `Size` field as it is — the adapter overwrites it on the first frame and on every resolution change. Do not hand-tune it; that is the bug this prevents.

**Verify:** in the Scene view, drag the Game view's aspect dropdown. `Size` should read **7.200** at
4:3, **6.000** at 16:10 and **5.400** at 16:9, and the board box must stay fully inside the frame at
every setting.

---

## Step 4 — Safe-area HUD

**Goal:** HUD content that clears a landscape notch. (F5)

### 4.1 `Assets/Scripts/Runtime/UI/SafeAreaFitter.cs`

```csharp
using UnityEngine;

namespace SliceNServe.UI
{
    /// <summary>
    /// Insets this RectTransform to Screen.safeArea so children clear notches and rounded
    /// corners. In landscape the notch sits on the *left or right* edge, so the horizontal
    /// insets are the ones that actually bite.
    ///
    /// One component on the HUD root. Individual layouts must never each solve this themselves.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreen;

        private void Awake() => _rect = (RectTransform)transform;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (Screen.safeArea != _lastSafeArea ||
                Screen.width != _lastScreen.x ||
                Screen.height != _lastScreen.y)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            if (Screen.width <= 0 || Screen.height <= 0) return;

            _lastSafeArea = Screen.safeArea;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);

            // Screen Space - Overlay + Scale With Screen Size means canvas-normalised
            // coordinates are identical to screen-normalised ones.
            Vector2 min = _lastSafeArea.position;
            Vector2 max = _lastSafeArea.position + _lastSafeArea.size;
            min.x /= Screen.width;  min.y /= Screen.height;
            max.x /= Screen.width;  max.y /= Screen.height;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
```

### 4.2 HUD canvas contract

In `Game.unity`, on the HUD canvas:

| Setting | Value |
| --- | --- |
| Render Mode | `Screen Space - Overlay` |
| Canvas Scaler → UI Scale Mode | `Scale With Screen Size` |
| Canvas Scaler → Reference Resolution | `1920 × 1080` |
| Canvas Scaler → Match | `0.5` |

Then create an empty child named `SafeAreaRoot` directly under the canvas and add `SafeAreaFitter` to
it. **Every HUD element goes under `SafeAreaRoot`** — tickets, cash readout, reputation readout,
patience bars, pause button. Nothing may parent directly to the canvas.

> The CanvasScaler contract is stack-neutral; it holds whether the tickets are uGUI or UI Toolkit.

**Verify:** `Window → General → Device Simulator`, pick a notched landscape device profile, and
confirm no HUD element is clipped or covered. Then widen the Game view to a very wide aspect and
confirm the HUD follows the safe area rather than the raw screen edges.

---

## Step 5 — Verification matrix

**Goal:** §1's definition of done is *demonstrated*, not assumed.

### 5.1 Aspect matrix (Editor only, no device needed)

Use the Game view's aspect dropdown; use the Device Simulator
(`com.unity.device-simulator.devices` is installed) for phone shapes.

| Aspect | Setting | Expect |
| --- | --- | --- |
| 4:3 | `4:3` | Full board visible, 1.8 u of backdrop above and below, camera `Size` **7.200** |
| 16:10 | `16:10` | Full board visible, `Size` **6.000** |
| 16:9 | `16:9` | Board box fills the frame exactly, `Size` **5.400** |
| 19.5:9 | phone profile | Board centred, 2.1 u of backdrop left and right, `Size` **5.400** |
| 21:9 | `21:9` | As above, widest legal view, `Size` **5.400** |
| 32:9 | free aspect / ultrawide profile | **Pillarboxed** — black bars, board not stretched |

At every aspect: **no part of the 19.2 × 10.8 box is cut off**, and HUD content stays inside the safe
area.

### 5.2 Cross-platform input parity (plan §7)

Requires §2 Phase 1 (`AimInput`). Once it exists:

1. Drag with the **mouse** from the launcher, a known world-unit distance. Record `power` and `direction`.
2. Repeat the identical world-unit drag with **touch** via the Device Simulator (it maps mouse to touch when touch simulation is enabled).
3. Both must report the same `power` and the same `direction`.
4. Confirm a drag shorter than `0.35 u` launches nothing.

### 5.3 Orientation check

- `ProjectSettings/ProjectSettings.asset` shows `defaultScreenOrientation` ≠ `4`.
- On a device or the Device Simulator: **rotating never produces a portrait frame.**

### 5.4 Platform builds

```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.16f1/Editor/Unity.exe" \
  -batchmode -quit -nographics \
  -projectPath "<repo>" \
  -buildTarget Win64 -logFile -
```

Build via `File → Build Profiles` for the interactive path. Then:

- The Windows build boots to `Game.unity`; **dragging the window edge live never crops the board** (`BoardViewportAdapter` reacts to `Screen.width`/`height`).
- `adb install -r <apk>` on a device: the app opens in **landscape** and cannot be rotated to portrait.

### 5.5 Budget check

- `Window → Analysis → Profiler → GC Alloc` while `Game.unity` idles: expect **0 B/frame**.
- Record the draw-call count in the Frame Debugger as the §1 baseline later sections are held to.

### 5.6 Commit

```bash
git add -A
git commit -m "Section 1: landscape lock, aspect policy, camera adapter, safe-area HUD"
```

---

## Optional cleanups

Small, independent, safe to do or skip. §2's runbook does not cover them.

| Cleanup | Why |
| --- | --- |
| Delete the template `Player` action map from `InputSystem_Actions.inputactions` | §2 adds a `Board` map but never says what happens to the template one. Leaving it means the asset advertises Move/Look/Jump/Attack for a game that has none. **Keep the `UI` map** — the tickets and HUD buttons need it. |
| Remove the `Gamepad`, `Joystick` and `XR` control schemes | Same reason. Keep `Keyboard&Mouse` and `Touch`. |
| Delete the template scene assets: `Assets/Settings/Lit2DSceneTemplate.scenetemplate`, `Assets/Settings/Scenes/URP2DSceneTemplate.unity` | Clutter in the Create menu. Do **not** touch `UniversalRP.asset`, `Renderer2D.asset` or `UniversalRenderPipelineGlobalSettings.asset` — they are referenced by GUID from `GraphicsSettings` and `QualitySettings`. |
| Delete `Assets/Scripts/GameManager.cs` | A 16-line empty auto-generated stub, no namespace, no `.asmdef`, outside the planned `Scripts/Runtime/` layout. It is not part of any design. |

**§1 is complete when every row in Step 5 passes.** If a row fails, fix it in the step that owns it and
re-run the matrix — do not start §2's board work on top of an unverified frame.

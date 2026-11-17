using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// The kitchen around the board: a cooking line along the bottom (stoves with sizzling pans
    /// and steaming pots, a chopping station), shelves and hanging tools down the right side, and
    /// the dining room on the left where <see cref="CustomerLine"/> queues the customers — all in
    /// the space between <see cref="BoardConfig.viewBounds"/> and the play area.
    ///
    /// Purely presentational, like <see cref="BoardFx"/>: built at start from the layout, animated
    /// procedurally (no Animator, as elsewhere), and it reacts to the run — flames flare when an
    /// order is served and burn hotter in a rush. Removing it changes nothing about the game.
    /// </summary>
    public sealed class KitchenDecor : MonoBehaviour
    {
        private const int SteamCount = 48;
        private const int MaxBurners = 8;
        private const int MaxSwingers = 12;
        private const int MaxSizzlers = 8;
        private const int MaxChefs = 4;

        [Header("Shapes")]
        [SerializeField] private Sprite square;
        [SerializeField] private Sprite circle;
        [SerializeField] private Sprite wallTile;

        [Header("Props (any may be empty)")]
        [Tooltip("Sit on a stove and sizzle: frying pans.")]
        [SerializeField] private Sprite[] pans;
        [Tooltip("Sit on a stove and steam: kettles, pots.")]
        [SerializeField] private Sprite[] pots;
        [Tooltip("Stand on the counter between stoves.")]
        [SerializeField] private Sprite[] counterItems;
        [Tooltip("Stand on the side shelves.")]
        [SerializeField] private Sprite[] shelfItems;
        [Tooltip("Hang from the side rails and swing.")]
        [SerializeField] private Sprite[] hangingTools;

        [Header("Dining room (left of the board, where the customers queue)")]
        [SerializeField] private Sprite diningWall;
        [SerializeField] private Sprite door;
        [Tooltip("A chair, then a table.")]
        [SerializeField] private Sprite[] furniture;
        [Tooltip("Optional: hung from the dining room ceiling in place of the two simple lamps. "
               + "More than one sprite plays as an animation (a flickering flame).")]
        [SerializeField] private Sprite[] chandelierFrames;
        [SerializeField, Min(0.5f)] private float chandelierSize = 2.2f;
        [SerializeField, Min(1f)] private float chandelierFps = 8f;
        [SerializeField] private Color diningWallColor = new Color(0.78f, 0.62f, 0.5f);
        [SerializeField] private Color wainscotColor = new Color(0.4f, 0.25f, 0.17f);
        [SerializeField] private Color floorColor = new Color(0.5f, 0.33f, 0.2f);

        [Header("Chefs (optional animated frames, pivoted at the feet)")]
        [Tooltip("Loops while they work.")]
        [SerializeField] private Sprite[] chefChop;
        [Tooltip("Played once when an order is served.")]
        [SerializeField] private Sprite[] chefCheer;
        [SerializeField, Min(1f)] private float chefFps = 6f;

        [Header("Colours")]
        [SerializeField] private Color wallColor = new Color(0.42f, 0.33f, 0.3f);
        [SerializeField] private Color counterColor = new Color(0.45f, 0.28f, 0.18f);
        [SerializeField] private Color counterTopColor = new Color(0.62f, 0.42f, 0.27f);
        [SerializeField] private Color stoveColor = new Color(0.2f, 0.19f, 0.22f);
        [SerializeField] private Color shelfColor = new Color(0.52f, 0.34f, 0.21f);

        private struct Burner { public SpriteRenderer[] Flames; public Vector2 At; public float Seed; }
        private struct Swinger { public Transform Pivot; public float Seed, Amplitude; }
        private struct Sizzler { public Transform Pan; public Vector3 Home; public float Seed; }
        private struct Puff { public Vector2 Velocity; public float Age, Life, Size; }

        private readonly Burner[] _burners = new Burner[MaxBurners];
        private readonly Swinger[] _swingers = new Swinger[MaxSwingers];
        private readonly Sizzler[] _sizzlers = new Sizzler[MaxSizzlers];
        private readonly SpriteRenderer[] _chefs = new SpriteRenderer[MaxChefs];
        private readonly float[] _cheerStart = new float[MaxChefs];   // Time.time the cheer began; < 0 when chopping
        private readonly Vector2[] _steamSources = new Vector2[MaxBurners];
        private readonly SpriteRenderer[] _steamViews = new SpriteRenderer[SteamCount];
        private readonly Puff[] _steam = new Puff[SteamCount];

        private readonly SpriteRenderer[] _glows = new SpriteRenderer[4];
        private int _glowCount;
        private SpriteRenderer _chandelierView;
        private int _burnerCount, _swingerCount, _sizzlerCount, _chefCount, _steamSourceCount, _nextPuff;
        private float _untilPuff, _flare, _heat = 1f;
        private EventBus _events;

        public void Initialize(BoardConfig config, EventBus events)
        {
            _events = events;
            Build(config.viewBounds, config.worldBounds);

            if (_events == null) return;
            _events.Subscribe<OrderServed>(OnServed);
            _events.Subscribe<RushHourChanged>(OnRush);
        }

        private void OnDestroy()
        {
            if (_events == null) return;
            _events.Unsubscribe<OrderServed>(OnServed);
            _events.Unsubscribe<RushHourChanged>(OnRush);
        }

        private void OnServed(OrderServed e)
        {
            _flare = 1f;
            for (int i = 0; i < _steamSourceCount; i++) EmitSteam(_steamSources[i], 1.4f);

            // The chefs cheer, one just after the other.
            if (chefCheer != null && chefCheer.Length > 0)
                for (int i = 0; i < _chefCount; i++) _cheerStart[i] = Time.time + i * 0.12f;
        }

        private void OnRush(RushHourChanged e) => _heat = e.Active ? 1.6f : 1f;

        // ------------------------------------------------------------------ layout

        private void Build(Rect view, Rect play)
        {
            const float border = 0.5f;   // BoardBounds' frame reaches this far outside the play area
            float counterTop = play.yMin - border;
            float lineY = (counterTop + view.yMin) * 0.5f;

            // The wall behind it all, tiled, reaching well past the view for ultrawide screens.
            Tiled("Wall", view.center, new Vector2(view.width * 3f, view.height * 3f), wallTile, wallColor, -15, 1.2f);

            // Left of the board is the dining room, where the customers queue; the kitchen is the rest.
            float kitchenLeft = play.xMin - border;
            float kitchenRight = view.xMax + view.width;   // past the view, for ultrawide screens
            if (kitchenLeft > view.xMin + 1f) Dining(view, kitchenLeft, counterTop);

            // Cooking line: the counter body, then its lighter lip.
            float lineCenter = (kitchenLeft + kitchenRight) * 0.5f, lineWidth = kitchenRight - kitchenLeft;
            Block(lineCenter, (counterTop + view.yMin - 3f) * 0.5f, lineWidth, counterTop - view.yMin + 3f, counterColor, -8);
            Block(lineCenter, counterTop - 0.08f, lineWidth, 0.16f, counterTopColor, -7);

            // Along the line, across the kitchen's visible width: stoves, counter items, and chefs
            // between them, at fixed fractions so any board width lays out the same way.
            float x0 = kitchenLeft + 0.9f, x1 = view.xMax - 0.9f;
            float At(float f) => Mathf.Lerp(x0, x1, f);
            float[] stoveF = { 0f, 0.36f, 0.64f, 1f };
            for (int i = 0; i < stoveF.Length; i++)
                Stove(new Vector2(At(stoveF[i]), lineY), i % 2 == 0 ? Pick(pans, i / 2) : Pick(pots, i / 2), steams: i % 2 == 1);

            float[] itemF = { 0.24f, 0.5f, 0.76f };
            for (int i = 0; i < itemF.Length; i++)
                Prop(Pick(counterItems, i), new Vector2(At(itemF[i]), lineY + 0.05f), 0.85f, 6, Hash(i) * 6f - 3f);

            // The side strip on the right: a rail of hanging tools up high, shelves lower down.
            float sideInner = play.xMax + border;
            float sideWidth = view.xMax - sideInner;
            Side(1, sideInner + Mathf.Max(0.5f, sideWidth * 0.5f), play, counterTop);

            // Chefs, if their animation frames were provided: working the line between the stoves,
            // feet on the counter's lower edge, heads peeking over the board's rim.
            if (chefChop != null && chefChop.Length > 0)
            {
                float feet = view.yMin + 0.05f;
                Chef(new Vector2(At(0.12f), feet), flip: false);
                Chef(new Vector2(At(0.88f), feet), flip: true);
            }

            for (int i = 0; i < SteamCount; i++)
            {
                _steamViews[i] = Sprite("Steam", circle, Vector2.zero, 0.3f, new Color(1f, 1f, 1f, 0f), 3);
                _steamViews[i].enabled = false;
                _steam[i].Life = -1f;
            }
        }

        /// <summary>The dining room: panelled wall, wooden floor, the front door the customers use,
        /// a table by the wall, warm lamps, and the serving counter they queue up to.</summary>
        private void Dining(Rect view, float right, float counterTop)
        {
            float left = view.xMin - view.width;   // past the view, for ultrawide screens
            float floorY = counterTop + 0.2f;      // CustomerLine.FloorY: where their feet are
            float w = right - left, cx = (left + right) * 0.5f;
            float floorH = floorY - view.yMin + 3f, floorMid = floorY - floorH * 0.5f;

            Tiled("DiningWall", new Vector2(cx, view.center.y), new Vector2(w, view.height * 3f),
                  diningWall != null ? diningWall : wallTile, diningWallColor, -14, 1.0f);
            Block(cx, floorY + 0.55f, w, 1.1f, wainscotColor, -13);
            Block(cx, floorY + 1.12f, w, 0.08f, counterTopColor, -12);
            Block(cx, floorMid, w, floorH, floorColor, -12);
            for (float x = right - 0.6f; x > left; x -= 1.2f)   // floorboard seams
                Block(x, floorMid, 0.04f, floorH, new Color(0f, 0f, 0f, 0.18f), -11);

            // The door the customers come and go by (CustomerLine walks them to and from it).
            float doorX = view.xMin + 1.0f;
            if (door != null) Prop(door, new Vector2(doorX, floorY), 2.3f, -9, 0f);

            // A table and chairs against the back wall, behind the line.
            float tableX = (doorX + right) * 0.5f;
            if (furniture != null && furniture.Length >= 2)
            {
                var dim = new Color(0.8f, 0.75f, 0.72f);
                Prop(furniture[0], new Vector2(tableX - 0.9f, floorY + 0.15f), 1.0f, -10, 0f).color = dim;
                Prop(furniture[1], new Vector2(tableX, floorY + 0.15f), 1.3f, -10, 0f).color = dim;
                SpriteRenderer chair = Prop(furniture[0], new Vector2(tableX + 0.9f, floorY + 0.15f), 1.0f, -10, 0f);
                chair.color = dim;
                chair.flipX = true;
            }

            // Light from the ceiling, swaying a little: one chandelier if the art is there (its
            // candlelight flickers), otherwise two simple lamps.
            Sprite chandelier = chandelierFrames != null && chandelierFrames.Length > 0 ? chandelierFrames[0] : null;
            int lights = chandelier != null ? 1 : 2;
            for (int i = 0; i < lights; i++)
            {
                float x = lights == 1 ? (doorX + right) * 0.5f : Mathf.Lerp(doorX + 0.9f, right - 1.0f, i);
                var hook = new GameObject(lights == 1 ? "Chandelier" : "Lamp").transform;
                hook.SetParent(transform, false);
                hook.position = new Vector3(x, view.yMax, 0f);
                Block(x, view.yMax - 0.4f, 0.05f, 0.8f, new Color(0.15f, 0.1f, 0.1f), -9).transform.SetParent(hook, true);
                float glowY = chandelier != null ? view.yMax - 0.8f - chandelierSize * 0.4f : view.yMax - 1.2f;
                SpriteRenderer glow = Sprite("Glow", circle, new Vector2(x, glowY), chandelier != null ? chandelierSize * 1.3f : 1.5f,
                                             new Color(1f, 0.85f, 0.5f, 0.1f), -9);
                glow.transform.SetParent(hook, true);
                if (_glowCount < _glows.Length) _glows[_glowCount++] = glow;

                if (chandelier != null)
                {
                    // Hung by its top edge from the end of the chain.
                    SpriteRenderer c = Prop(chandelier, new Vector2(x, view.yMax - 0.8f), chandelierSize, -7, 0f);
                    c.transform.position += Vector3.down * (c.bounds.max.y - (view.yMax - 0.8f));
                    c.transform.SetParent(hook, true);
                    _chandelierView = c;
                }
                else
                {
                    Sprite("Shade", circle, new Vector2(x, view.yMax - 1.05f), 0.5f, new Color(0.95f, 0.55f, 0.2f), -8).transform.SetParent(hook, true);
                    Sprite("Bulb", circle, new Vector2(x, view.yMax - 1.2f), 0.22f, new Color(1f, 0.95f, 0.7f), -7).transform.SetParent(hook, true);
                }
                if (_swingerCount < MaxSwingers)
                    _swingers[_swingerCount++] = new Swinger { Pivot = hook, Seed = i * 2.3f, Amplitude = 2.5f };
            }

            // The serving counter at the pass, which the front of the line faces.
            Block(right - 0.35f, floorY + 0.6f, 0.7f, 1.2f, counterTopColor, -6);
            Block(right - 0.35f, floorY + 0.6f, 0.5f, 1.0f, shelfColor, -5);
            Block(right - 0.38f, floorY + 1.22f, 0.86f, 0.14f, counterColor, -4);
        }

        private void Side(int side, float x, Rect play, float counterTop)
        {
            // Rail of tools, below the HUD that covers the top corners.
            float railY = play.yMax - 3.1f;
            // At 16:9 a side strip is only ~1 u wide, so props hug its centre; wider screens show
            // more wall around them.
            Block(x, railY, 1.1f, 0.08f, shelfColor, -6);
            for (int i = 0; i < 2; i++)
            {
                Sprite tool = Pick(hangingTools, i + (side > 0 ? 2 : 0));
                if (tool == null) continue;
                Swing(tool, new Vector2(x + (i - 0.5f) * 0.42f, railY - 0.02f), 0.6f);
            }

            // Two shelves with a few jars each.
            for (int s = 0; s < 2; s++)
            {
                float y = counterTop + 1.4f + s * 1.6f;
                Block(x, y, 1.1f, 0.12f, shelfColor, -6);
                for (int j = 0; j < 2; j++)
                    Prop(Pick(shelfItems, s * 2 + j + (side > 0 ? 4 : 0)), new Vector2(x + (j - 0.5f) * 0.46f, y + 0.27f), 0.46f, -5, 0f);
            }
        }

        private void Stove(Vector2 at, Sprite cookware, bool steams)
        {
            Block(at.x, at.y, 1.5f, 0.95f, stoveColor, -6);
            Sprite("Burner", circle, at, 0.75f, new Color(0.1f, 0.1f, 0.12f), -5);

            if (_burnerCount < MaxBurners)
            {
                var flames = new SpriteRenderer[3];
                for (int f = 0; f < flames.Length; f++)
                {
                    float a = f / 3f * Mathf.PI * 2f;
                    flames[f] = Sprite("Flame", circle, at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.3f, 0.2f, new Color(1f, 0.55f, 0.15f), -4);
                }
                _burners[_burnerCount++] = new Burner { Flames = flames, At = at, Seed = Hash(_burnerCount) * 100f };
            }

            if (cookware == null) return;
            SpriteRenderer pan = Prop(cookware, at + new Vector2(0f, 0.08f), 1.05f, 6, 0f);
            if (steams && _steamSourceCount < _steamSources.Length) _steamSources[_steamSourceCount++] = at + new Vector2(0f, 0.45f);
            else if (_sizzlerCount < MaxSizzlers)
                _sizzlers[_sizzlerCount++] = new Sizzler { Pan = pan.transform, Home = pan.transform.position, Seed = Hash(_sizzlerCount + 7) * 50f };
        }

        private void Swing(Sprite tool, Vector2 hook, float size)
        {
            if (_swingerCount >= MaxSwingers) return;

            // A pivot at the hook, the tool hanging below it, so rotating the pivot swings the tool.
            var pivot = new GameObject("Hook").transform;
            pivot.SetParent(transform, false);
            pivot.position = hook;
            SpriteRenderer sr = Prop(tool, Vector2.zero, size, -5, 0f);
            sr.transform.SetParent(pivot, false);
            sr.transform.localPosition = new Vector3(0f, -size * 0.5f, 0f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);   // handle up, as hung

            _swingers[_swingerCount++] = new Swinger { Pivot = pivot, Seed = Hash(_swingerCount + 3) * 10f, Amplitude = 4f + Hash(_swingerCount) * 4f };
        }

        private void Chef(Vector2 at, bool flip)
        {
            if (_chefCount >= MaxChefs) return;
            SpriteRenderer sr = Prop(chefChop[0], at, 1.7f, 7, 0f);
            sr.flipX = flip;
            _cheerStart[_chefCount] = -1f;
            _chefs[_chefCount++] = sr;
        }

        // ------------------------------------------------------------------ animation

        private void Update()
        {
            float t = Time.time, dt = Time.deltaTime;
            _flare = Mathf.MoveTowards(_flare, 0f, dt * 1.5f);
            float heat = _heat + _flare * 0.8f;

            for (int b = 0; b < _burnerCount; b++)
            {
                Burner burner = _burners[b];
                for (int f = 0; f < burner.Flames.Length; f++)
                {
                    float n = Mathf.PerlinNoise(burner.Seed + f * 3.1f, t * 6f);
                    burner.Flames[f].transform.localScale = Vector3.one * (0.16f + n * 0.12f) * heat / SpriteSize(burner.Flames[f]);
                    burner.Flames[f].color = Color.Lerp(new Color(1f, 0.4f, 0.1f), new Color(1f, 0.85f, 0.3f), n);
                }
            }

            if (_chandelierView != null && chandelierFrames.Length > 1)
                _chandelierView.sprite = chandelierFrames[Mathf.FloorToInt(t * chandelierFps) % chandelierFrames.Length];

            // Candlelight: the dining room's glow breathes a little.
            for (int i = 0; i < _glowCount; i++)
                _glows[i].color = new Color(1f, 0.85f, 0.5f, 0.07f + 0.06f * Mathf.PerlinNoise(i * 7.3f, t * 3f));

            for (int i = 0; i < _swingerCount; i++)
                _swingers[i].Pivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.3f + _swingers[i].Seed) * _swingers[i].Amplitude);

            for (int i = 0; i < _sizzlerCount; i++)
            {
                Sizzler s = _sizzlers[i];
                float jitter = (Mathf.PerlinNoise(s.Seed, t * 14f) - 0.5f) * 0.035f * heat;
                s.Pan.position = s.Home + new Vector3(jitter, 0f, 0f);
            }

            for (int i = 0; i < _chefCount; i++) _chefs[i].sprite = ChefFrame(i, t);

            _untilPuff -= dt * heat;
            if (_untilPuff <= 0f && _steamSourceCount > 0)
            {
                _untilPuff = 0.18f;
                EmitSteam(_steamSources[(int)(t * 7f) % _steamSourceCount], 1f);
            }

            for (int i = 0; i < SteamCount; i++)
            {
                if (_steam[i].Life < 0f) continue;

                ref Puff p = ref _steam[i];
                p.Age += dt;
                SpriteRenderer view = _steamViews[i];
                if (p.Age >= p.Life) { p.Life = -1f; view.enabled = false; continue; }

                float u = p.Age / p.Life;
                view.transform.position += (Vector3)(p.Velocity * dt);
                view.transform.localScale = Vector3.one * (p.Size * Mathf.Lerp(0.6f, 1.6f, u)) / SpriteSize(view);
                view.color = new Color(1f, 1f, 1f, 0.38f * Mathf.Sin(u * Mathf.PI));
            }
        }

        /// <summary>Chopping, faster in a rush; or the cheer, once through, after a serve.</summary>
        private Sprite ChefFrame(int chef, float t)
        {
            float since = t - _cheerStart[chef];
            if (_cheerStart[chef] >= 0f && since >= 0f)
            {
                int cheer = Mathf.FloorToInt(since * chefFps * 1.6f);
                if (cheer < chefCheer.Length) return chefCheer[cheer];
                _cheerStart[chef] = -1f;
            }

            float fps = chefFps * (_heat > 1f ? 1.7f : 1f);
            int frame = Mathf.FloorToInt(t * fps + chef * 0.5f);   // the pair chop out of step
            return chefChop[frame % chefChop.Length];
        }

        private void EmitSteam(Vector2 at, float strength)
        {
            int i = _nextPuff;
            _nextPuff = (_nextPuff + 1) % SteamCount;

            _steam[i] = new Puff
            {
                Velocity = new Vector2(UnityEngine.Random.Range(-0.15f, 0.15f), UnityEngine.Random.Range(0.45f, 0.7f) * strength),
                Age = 0f,
                Life = UnityEngine.Random.Range(1.1f, 1.6f),
                Size = UnityEngine.Random.Range(0.22f, 0.34f) * strength
            };
            SpriteRenderer view = _steamViews[i];
            view.enabled = true;
            view.transform.position = at + new Vector2(UnityEngine.Random.Range(-0.12f, 0.12f), 0f);
        }

        // ------------------------------------------------------------------ helpers

        private static Sprite Pick(Sprite[] from, int index)
            => from == null || from.Length == 0 ? null : from[((index % from.Length) + from.Length) % from.Length];

        private static float Hash(int seed) => Mathf.Repeat(Mathf.Sin(seed * 12.9898f) * 43758.547f, 1f);

        private static float SpriteSize(SpriteRenderer sr)
            => sr.sprite != null ? Mathf.Max(0.01f, Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y)) : 1f;

        private SpriteRenderer Prop(Sprite sprite, Vector2 at, float size, int order, float degrees)
        {
            if (sprite == null) return Sprite("Prop", null, at, size, Color.clear, order);

            SpriteRenderer sr = Sprite(sprite.name, sprite, at, size, Color.white, order);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, degrees);
            return sr;
        }

        private SpriteRenderer Sprite(string objectName, Sprite sprite, Vector2 at, float size, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            go.transform.localScale = Vector3.one * (size / SpriteSize(sr));
            return sr;
        }

        private SpriteRenderer Block(float x, float y, float width, float height, Color color, int order)
        {
            if (square == null) return Sprite("Block", null, new Vector2(x, y), 1f, Color.clear, order);

            SpriteRenderer sr = Sprite("Block", square, new Vector2(x, y), 1f, color, order);
            Vector2 native = square.bounds.size;
            sr.transform.localScale = new Vector3(width / native.x, height / native.y, 1f);
            return sr;
        }

        private void Tiled(string objectName, Vector2 at, Vector2 size, Sprite sprite, Color color, int order, float tile)
        {
            if (sprite == null) return;

            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(at.x, at.y, 1f);
            float scale = tile / sprite.bounds.size.x;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size / scale;
            sr.sortingOrder = order;
        }
    }
}

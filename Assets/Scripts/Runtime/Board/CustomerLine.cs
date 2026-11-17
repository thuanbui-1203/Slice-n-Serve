using System;
using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using TMPro;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// The customers, standing in line in the dining room to the left of the kitchen: one walks in
    /// through the door for every order, waits in line with a speech bubble showing what is still
    /// missing, and walks out — happily when served, angrily when their patience runs out.
    ///
    /// The dining room is also the button for the orders panel (<see cref="Area"/>): orders are
    /// no longer served automatically, so the player taps the customers to hand dishes over.
    ///
    /// Presentational, like <see cref="KitchenDecor"/>: it reads the queue and the order events and
    /// changes nothing about the game. Animated procedurally from walk-cycle frames (no Animator).
    /// </summary>
    public sealed class CustomerLine : MonoBehaviour
    {
        private const int MaxLines = 3;
        private const float Border = 0.5f;      // BoardBounds' frame reaches this far outside the play area

        /// <summary>One look: a side-view walk cycle facing right, and optionally idle frames.</summary>
        [Serializable]
        public sealed class WalkCycle
        {
            public Sprite[] walk;
            public Sprite[] idle;
        }

        [SerializeField] private WalkCycle[] looks;
        [Tooltip("True if the sheets face right (walking into the restaurant); false flips them.")]
        [SerializeField] private bool sheetsFaceRight = true;
        [SerializeField, Min(0.5f)] private float height = 2.1f;
        [SerializeField, Min(0.1f)] private float walkSpeed = 2.6f;
        [SerializeField, Min(1f)] private float walkFps = 10f;

        [Header("Bubbles")]
        [SerializeField] private Sprite square;
        [SerializeField] private Sprite check;
        [SerializeField] private TMP_FontAsset font;

        private enum Phase { Arriving, Waiting, Leaving }

        private sealed class Guest
        {
            public OrderInstance Order;
            public WalkCycle Look;
            public Transform Root;
            public SpriteRenderer Body;
            public float X, Bob, Anim, Fade, Hop, Shake, Since;
            public Phase Phase;
            public bool Happy;

            public Transform Bubble;
            public SpriteRenderer BubbleFrame, BubbleFill, BarBack, BarFill, Ready;
            public SpriteRenderer[] Icons;
            public TextMeshPro[] Counts;
            public TextMeshPro ReadyText;
            public float BubbleWidth;
        }

        private readonly List<Guest> _guests = new List<Guest>(16);
        private EventBus _events;
        private IOrderQueue _queue;
        private Rect _area;
        private float _floorY, _frontX, _doorX;
        private TextMeshPro _sign;

        /// <summary>The dining room in world units: tapping it opens the orders panel.</summary>
        public Rect Area => _area;

        /// <summary>Customers still in line (not walking out).</summary>
        public int Waiting
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _guests.Count; i++) if (_guests[i].Phase != Phase.Leaving) n++;
                return n;
            }
        }

        /// <summary>The dining room: everything left of the board's frame.</summary>
        public static Rect DiningArea(BoardConfig config)
        {
            Rect view = config.viewBounds, play = config.worldBounds;
            return Rect.MinMaxRect(view.xMin, view.yMin, play.xMin - Border, view.yMax);
        }

        /// <summary>Where customers' feet are: level with the cooking line's counter top.</summary>
        public static float FloorY(BoardConfig config) => config.worldBounds.yMin - Border + 0.2f;

        public void Initialize(RuntimeTuning tuning, EventBus events, IOrderQueue queue)
        {
            _events = events;
            _queue = queue;

            BoardConfig config = tuning.Config;
            _area = DiningArea(config);
            _floorY = FloorY(config);
            _frontX = _area.xMax - 0.75f;
            _doorX = _area.xMin + 1.0f;

            // A chalkboard on the wall, so the dining room reads as something to tap.
            var signAt = new Vector2(_area.center.x + 0.3f, _area.yMax - 2.7f);
            var board = new GameObject("SignBoard").transform;
            board.SetParent(transform, false);
            SpriteRenderer frame = Block(board, "Frame", new Color(0.45f, 0.28f, 0.16f), -6);
            Fit(frame, signAt.x, signAt.y, 4.1f, 1.25f, frame.color);
            SpriteRenderer slate = Block(board, "Slate", new Color(0.17f, 0.22f, 0.2f), -5);
            Fit(slate, signAt.x, signAt.y, 3.85f, 1.0f, slate.color);
            _sign = Text("Sign", signAt, 3.6f, new Color(1f, 0.95f, 0.8f), -4);
            _sign.rectTransform.sizeDelta = new Vector2(3.7f, 0.9f);
            _sign.SetText("Tap to serve");

            _events.Subscribe<OrderCreated>(OnCreated);
            _events.Subscribe<OrderServed>(OnServed);
            _events.Subscribe<OrderExpired>(OnExpired);
        }

        private void OnDestroy()
        {
            if (_events == null) return;
            _events.Unsubscribe<OrderCreated>(OnCreated);
            _events.Unsubscribe<OrderServed>(OnServed);
            _events.Unsubscribe<OrderExpired>(OnExpired);
        }

        // ------------------------------------------------------------------ events

        private void OnCreated(OrderCreated e)
        {
            OrderInstance order = _queue.Find(e.OrderId);
            if (order == null) return;

            WalkCycle look = looks != null && looks.Length > 0 ? looks[(int)(Hash(e.OrderId) * looks.Length) % looks.Length] : null;
            var guest = new Guest { Order = order, Look = look, X = _doorX, Phase = Phase.Arriving, Bob = Hash(e.OrderId + 11) * 6f };

            guest.Root = new GameObject($"Customer {e.OrderId}").transform;
            guest.Root.SetParent(transform, false);
            guest.Root.position = new Vector3(_doorX, _floorY, 0f);

            guest.Body = new GameObject("Body").AddComponent<SpriteRenderer>();
            guest.Body.transform.SetParent(guest.Root, false);
            guest.Body.sortingOrder = 8;
            Sprite first = FirstFrame(look);
            guest.Body.sprite = first;
            if (first != null) guest.Body.transform.localScale = Vector3.one * (height / first.bounds.size.y);

            BuildBubble(guest);
            _guests.Add(guest);
        }

        private void OnServed(OrderServed e) => Leave(e.OrderId, happy: true);

        private void OnExpired(OrderExpired e) => Leave(e.OrderId, happy: false);

        private void Leave(int orderId, bool happy)
        {
            for (int i = 0; i < _guests.Count; i++)
            {
                Guest g = _guests[i];
                if (g.Order.OrderId != orderId || g.Phase == Phase.Leaving) continue;

                g.Phase = Phase.Leaving;
                g.Happy = happy;
                g.Since = 0f;
                g.Hop = happy ? 1f : 0f;
                g.Shake = happy ? 0f : 1f;
                g.Bubble.gameObject.SetActive(false);
                return;
            }
        }

        /// <summary>The world position of the bubble icon for an ingredient, for a credit to fly to.</summary>
        public bool TryGetIconPosition(int orderId, string ingredientId, out Vector3 position)
        {
            position = default;
            for (int i = 0; i < _guests.Count; i++)
            {
                Guest g = _guests[i];
                if (g.Order.OrderId != orderId || g.Phase == Phase.Leaving) continue;

                position = g.Bubble.position;
                OrderLine[] lines = g.Order.Lines;
                for (int l = 0; l < lines.Length && l < MaxLines; l++)
                    if (lines[l].Ingredient.Id == ingredientId && g.Icons[l].gameObject.activeSelf)
                        position = g.Icons[l].transform.position;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ animation

        private void Update()
        {
            if (_queue == null) return;
            float dt = Time.deltaTime, t = Time.time;

            // The line: spawn order, front at the counter, bunching up as it grows.
            int waiting = Waiting;
            float room = _frontX - (_doorX + 0.2f);
            float spacing = waiting > 1 ? Mathf.Min(1.0f, room / (waiting - 1)) : 1f;
            int rows = Mathf.Clamp(Mathf.CeilToInt(1.55f / Mathf.Max(0.01f, spacing)), 1, 3);
            OrderInstance front = _queue.FrontOnly ? _queue.Front() : null;
            bool anyReady = false;

            int place = 0;

            for (int i = 0; i < _guests.Count; i++)
            {
                Guest g = _guests[i];
                g.Since += dt;
                float targetX;
                if (g.Phase == Phase.Leaving) targetX = _doorX - 0.3f;
                else targetX = _frontX - place++ * spacing;

                // Angry customers stamp before they go.
                bool stamping = g.Phase == Phase.Leaving && !g.Happy && g.Since < 0.45f;
                float speed = walkSpeed * (g.Phase == Phase.Leaving && !g.Happy ? 1.5f : 1f);
                float before = g.X;
                if (!stamping) g.X = Mathf.MoveTowards(g.X, targetX, speed * dt);
                bool moving = Mathf.Abs(g.X - before) > 1e-4f;
                if (g.Phase == Phase.Arriving && !moving && g.Since > 0.1f) g.Phase = Phase.Waiting;

                // Fade in through the door, out the same way.
                float fadeTarget = g.Phase == Phase.Leaving && Mathf.Abs(g.X - targetX) < 0.35f ? 0f : 1f;
                g.Fade = Mathf.MoveTowards(g.Fade, fadeTarget, dt * 4f);

                bool facingLeft = g.Phase == Phase.Leaving && !stamping;
                g.Body.flipX = facingLeft == sheetsFaceRight;
                g.Body.sprite = Frame(g, moving, dt);

                // Bob while idle, hop when served, shake when angry.
                g.Hop = Mathf.MoveTowards(g.Hop, 0f, dt * 2.2f);
                g.Shake = Mathf.MoveTowards(g.Shake, 0f, dt * 2f);
                float y = moving ? 0f : Mathf.Max(0f, Mathf.Sin(t * 2.2f + g.Bob)) * 0.03f;
                y += Mathf.Sin((1f - g.Hop) * Mathf.PI) * 0.45f * (g.Hop > 0f ? 1f : 0f);
                float shakeX = Mathf.Sin(t * 60f) * 0.06f * g.Shake;

                PatienceState state = g.Order.State;
                bool critical = g.Phase != Phase.Leaving && state == PatienceState.Critical && !g.Order.IsReady;
                Color tint = !g.Happy && g.Phase == Phase.Leaving ? new Color(1f, 0.55f, 0.5f) : Color.white;
                if (critical) tint = Color.Lerp(Color.white, new Color(1f, 0.6f, 0.55f), 0.5f + 0.5f * Mathf.Sin(t * 10f));
                tint.a = g.Fade;
                g.Body.color = tint;
                if (critical) shakeX += Mathf.Sin(t * 45f) * 0.02f;

                g.Root.position = new Vector3(g.X + shakeX, _floorY + y, 0f);

                if (g.Phase != Phase.Leaving)
                {
                    int row = (place - 1) % rows;
                    UpdateBubble(g, row, g.Order == front, t);
                    anyReady |= g.Order.IsReady;
                }

                if (g.Phase == Phase.Leaving && g.Fade <= 0f && g.Since > 0.5f)
                {
                    Destroy(g.Root.gameObject);
                    _guests.RemoveAt(i--);
                }
            }

            if (_sign != null)
            {
                // The sign wakes up when there is something to hand over.
                float pulse = anyReady ? 0.5f + 0.5f * Mathf.Sin(t * 6f) : 0f;
                _sign.color = Color.Lerp(new Color(1f, 0.95f, 0.8f, 0.8f), new Color(0.55f, 1f, 0.5f, 1f), anyReady ? 0.6f + 0.4f * pulse : 0f);
                _sign.transform.localScale = Vector3.one * (1f + 0.05f * pulse);
            }
        }

        private Sprite Frame(Guest g, bool moving, float dt)
        {
            WalkCycle look = g.Look;
            if (look == null || look.walk == null || look.walk.Length == 0) return null;

            if (moving)
            {
                g.Anim += dt * walkFps * (g.Phase == Phase.Leaving && !g.Happy ? 1.5f : 1f);
                return look.walk[Mathf.FloorToInt(g.Anim) % look.walk.Length];
            }

            g.Anim = 0f;
            if (look.idle != null && look.idle.Length > 0)
                return look.idle[Mathf.FloorToInt(Time.time * walkFps * 0.5f + g.Bob) % look.idle.Length];
            return look.walk[0];
        }

        private static Sprite FirstFrame(WalkCycle look)
            => look != null && look.walk != null && look.walk.Length > 0 ? look.walk[0] : null;

        // ------------------------------------------------------------------ the speech bubble

        private void BuildBubble(Guest g)
        {
            g.Bubble = new GameObject("Bubble").transform;
            g.Bubble.SetParent(g.Root, false);

            g.BubbleFrame = Block(g.Bubble, "Frame", new Color(0.24f, 0.16f, 0.14f), 30);
            g.BubbleFill = Block(g.Bubble, "Fill", new Color(1f, 0.97f, 0.89f), 31);
            g.BarBack = Block(g.Bubble, "BarBack", new Color(0.24f, 0.16f, 0.14f), 31);
            g.BarFill = Block(g.Bubble, "BarFill", Color.green, 32);

            g.Icons = new SpriteRenderer[MaxLines];
            g.Counts = new TextMeshPro[MaxLines];
            for (int l = 0; l < MaxLines; l++)
            {
                var icon = new GameObject($"Icon{l}").AddComponent<SpriteRenderer>();
                icon.transform.SetParent(g.Bubble, false);
                icon.sortingOrder = 33;
                g.Icons[l] = icon;

                TextMeshPro count = Text($"Count{l}", Vector2.zero, 2.4f, new Color(0.2f, 0.13f, 0.12f), 34);
                count.transform.SetParent(g.Bubble, false);
                count.alignment = TextAlignmentOptions.BottomRight;
                count.rectTransform.sizeDelta = new Vector2(0.5f, 0.3f);
                g.Counts[l] = count;
            }

            g.Ready = new GameObject("Ready").AddComponent<SpriteRenderer>();
            g.Ready.transform.SetParent(g.Bubble, false);
            g.Ready.sprite = check;
            g.Ready.sortingOrder = 33;
            if (check != null) g.Ready.transform.localScale = Vector3.one * (0.42f / Mathf.Max(0.01f, check.bounds.size.x));
            g.ReadyText = Text("ReadyText", Vector2.zero, 2.3f, new Color(0.15f, 0.45f, 0.15f), 34);
            g.ReadyText.transform.SetParent(g.Bubble, false);
            g.ReadyText.SetText("Serve!");
            g.ReadyText.rectTransform.sizeDelta = new Vector2(1.0f, 0.4f);
        }

        private void UpdateBubble(Guest g, int row, bool isFront, float t)
        {
            OrderInstance order = g.Order;
            bool ready = order.IsReady;
            const float icon = 0.38f, gap = 0.1f, pad = 0.1f;

            // Only what is still missing; a ready order shows a tick and "Serve!".
            int shown = 0;
            for (int l = 0; l < MaxLines; l++)
            {
                bool show = !ready && l < order.Lines.Length && !order.Lines[l].Satisfied;
                g.Icons[l].gameObject.SetActive(show);
                g.Counts[l].gameObject.SetActive(show);
                if (!show) continue;

                OrderLine line = order.Lines[l];
                SpriteRenderer sr = g.Icons[l];
                sr.sprite = line.Ingredient.Sprite;
                if (sr.sprite != null) sr.transform.localScale = Vector3.one * (icon / Mathf.Max(0.01f, Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y)));
                float x = pad + shown * (icon + gap) + icon * 0.5f;
                sr.transform.localPosition = new Vector3(x, 0.05f, 0f);
                g.Counts[l].transform.localPosition = new Vector3(x + 0.05f, -0.06f, 0f);
                g.Counts[l].SetText("{0}", line.Required - line.Fulfilled);
                shown++;
            }

            g.Ready.gameObject.SetActive(ready);
            g.ReadyText.gameObject.SetActive(ready);
            float width = ready ? 1.6f : pad * 2f + Mathf.Max(1, shown) * icon + Mathf.Max(0, shown - 1) * gap;
            g.BubbleWidth = width;
            if (ready)
            {
                g.Ready.transform.localPosition = new Vector3(0.3f, 0.02f, 0f);
                g.ReadyText.transform.localPosition = new Vector3(0.98f, 0.02f, 0f);
            }

            // Centred over the head; staggered rows when the line is packed tight.
            float top = height * 0.95f + 0.2f + row * 0.78f;
            float pop = ready ? 1f + 0.06f * Mathf.Sin(t * 8f) : 1f;
            g.Bubble.localPosition = new Vector3(-width * 0.5f * pop, top, 0f);
            g.Bubble.localScale = Vector3.one * pop;

            const float h = 0.5f;
            Color frame = ready ? new Color(0.25f, 0.62f, 0.25f) : isFront ? new Color(0.95f, 0.65f, 0.15f) : new Color(0.24f, 0.16f, 0.14f);
            Fit(g.BubbleFrame, width * 0.5f, 0f, width + 0.08f, h + 0.08f, frame);
            Fit(g.BubbleFill, width * 0.5f, 0f, width, h, ready ? new Color(0.86f, 1f, 0.82f) : new Color(1f, 0.97f, 0.89f));

            float f = Mathf.Clamp01(order.PatienceFraction);
            Fit(g.BarBack, width * 0.5f, -h * 0.5f - 0.1f, width + 0.08f, 0.12f, new Color(0.24f, 0.16f, 0.14f));
            Color barColor = order.State == PatienceState.Critical ? new Color(0.9f, 0.25f, 0.2f)
                           : order.State == PatienceState.Hurry ? new Color(0.95f, 0.75f, 0.2f)
                           : new Color(0.4f, 0.8f, 0.3f);
            Fit(g.BarFill, width * f * 0.5f, -h * 0.5f - 0.1f, width * f, 0.07f, barColor);
        }

        // ------------------------------------------------------------------ helpers

        private SpriteRenderer Block(Transform parent, string name, Color color, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            sr.sprite = square;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        private static void Fit(SpriteRenderer sr, float x, float y, float width, float height, Color color)
        {
            sr.transform.localPosition = new Vector3(x, y, 0f);
            Vector2 native = sr.sprite != null ? (Vector2)sr.sprite.bounds.size : Vector2.one;
            sr.transform.localScale = new Vector3(width / Mathf.Max(0.001f, native.x), height / Mathf.Max(0.001f, native.y), 1f);
            sr.color = color;
        }

        private TextMeshPro Text(string name, Vector2 at, float size, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var text = go.AddComponent<TextMeshPro>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.sortingOrder = order;
            text.rectTransform.sizeDelta = new Vector2(2f, 0.5f);
            return text;
        }

        private static float Hash(int seed) => Mathf.Repeat(Mathf.Sin(seed * 12.9898f) * 43758.547f, 1f);
    }
}

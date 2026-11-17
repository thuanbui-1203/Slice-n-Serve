using System;
using SliceNServe.Orders;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// One order ticket: customer portrait (its mood follows patience), dish, an ingredient
    /// checklist with fulfilled/required counts, and the patience bar. Bound to an
    /// <see cref="OrderInstance"/> it only reads. Once every ingredient is in, it lights up as
    /// ready, and tapping it asks to serve the order.
    /// </summary>
    public sealed class OrderTicketView : MonoBehaviour
    {
        [Serializable]
        public sealed class LineSlot
        {
            public GameObject root;
            public Image icon;
            public TMP_Text count;
            public Image check;
            [NonSerialized] public float Punch;
        }

        private const float AppearSeconds = 0.3f;
        private const float CloseSeconds = 0.55f;

        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform body;
        [SerializeField] private Image flash;
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text dishName;
        [SerializeField] private Image dishIcon;
        [SerializeField] private LineSlot[] lines;
        [SerializeField] private PatienceBar patience;
        [SerializeField] private Button button;
        [Tooltip("Shown over the checklist once the order is complete.")]
        [SerializeField] private GameObject readyBanner;

        private OrderInstance _order;
        private float _age;
        private float _closingAge = -1f;
        private bool _served;
        private float _nudge;
        private Action<int> _serve;

        public int OrderId => _order != null ? _order.OrderId : 0;
        public bool IsClosing => _closingAge >= 0f;
        public bool IsFinished => _closingAge >= CloseSeconds;

        /// <param name="serve">Called with the order id when a ready ticket is tapped.</param>
        public void Bind(OrderInstance order, Action<int> serve = null)
        {
            _order = order;
            _serve = serve;
            _age = 0f;
            if (button != null) button.onClick.AddListener(OnTapped);

            dishName.SetText(order.Dish.DisplayName);
            dishIcon.sprite = order.Dish.Sprite;
            dishIcon.enabled = order.Dish.Sprite != null;

            for (int i = 0; i < lines.Length; i++)
            {
                bool used = i < order.Lines.Length;
                lines[i].root.SetActive(used);
                if (!used) continue;

                lines[i].icon.sprite = order.Lines[i].Ingredient.Sprite;
                RefreshLine(i);
            }

            flash.color = Color.clear;
            Refresh();
        }

        /// <summary>A ready ticket is served; one still cooking shakes its head.</summary>
        public void OnTapped()
        {
            if (_order == null || IsClosing) return;
            if (_order.IsReady) _serve?.Invoke(_order.OrderId);
            else _nudge = 1f;
        }

        public bool IsReady => _order != null && _order.IsReady && !IsClosing;

        /// <summary>The ingredient icon a credit for this line should fly to, or null.</summary>
        public RectTransform LineTarget(string ingredientId)
        {
            if (_order == null) return null;

            for (int i = 0; i < _order.Lines.Length && i < lines.Length; i++)
                if (_order.Lines[i].Ingredient.Id == ingredientId) return (RectTransform)lines[i].icon.transform;

            return null;
        }

        public void OnProgress(string ingredientId)
        {
            for (int i = 0; i < _order.Lines.Length && i < lines.Length; i++)
            {
                if (_order.Lines[i].Ingredient.Id != ingredientId) continue;

                RefreshLine(i);
                lines[i].Punch = 1f;
            }
        }

        public void Close(bool served)
        {
            if (IsClosing) return;

            _served = served;
            _closingAge = 0f;
            for (int i = 0; i < _order.Lines.Length && i < lines.Length; i++) RefreshLine(i);
            Refresh();
        }

        private void RefreshLine(int i)
        {
            OrderLine line = _order.Lines[i];
            lines[i].count.SetText("{0}/{1}", line.Fulfilled, line.Required);
            lines[i].check.enabled = line.Satisfied;
            lines[i].icon.color = line.Satisfied ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
        }

        /// <summary>Patience is the one thing that changes continuously, so it is the one thing polled.</summary>
        public void Refresh()
        {
            if (_order == null) return;

            patience.SetFrom(_order);
            if (readyBanner != null) readyBanner.SetActive(_order.IsReady && !IsClosing);

            Sprite[] moods = _order.Customer.SpriteSet;
            if (moods == null || moods.Length == 0) return;

            int mood = _order.State switch
            {
                PatienceState.Served => 0,
                PatienceState.Normal => _order.PatienceFraction > 0.8f ? 0 : 1,
                PatienceState.Hurry => 2,
                _ => 3
            };
            portrait.sprite = moods[Mathf.Min(mood, moods.Length - 1)];
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _age += dt;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Punch <= 0f) continue;

                lines[i].Punch = Mathf.MoveTowards(lines[i].Punch, 0f, dt * 4f);
                lines[i].icon.transform.localScale = Vector3.one * (1f + 0.5f * lines[i].Punch);
            }

            if (!IsClosing)
            {
                float t = Mathf.Clamp01(_age / AppearSeconds);
                float ease = 1f - (1f - t) * (1f - t);
                float ready = IsReady ? 1f + 0.035f * Mathf.Sin(_age * 7f) : 1f;
                body.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, ease) * (1f + Mathf.Sin(t * Mathf.PI) * 0.08f) * ready;
                _nudge = Mathf.MoveTowards(_nudge, 0f, dt * 3f);
                body.anchoredPosition = new Vector2(Mathf.Sin(_age * 55f) * 9f * _nudge, 0f);
                group.alpha = ease;
                return;
            }

            _closingAge += dt;
            float c = Mathf.Clamp01(_closingAge / CloseSeconds);
            Color tint = _served ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.25f, 0.2f);
            tint.a = 0.55f * (1f - c);
            flash.color = tint;

            if (_served)
            {
                body.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(c * Mathf.PI));
                body.anchoredPosition = new Vector2(0f, 60f * c * c);
            }
            else
            {
                body.anchoredPosition = new Vector2(Mathf.Sin(_closingAge * 50f) * 10f * (1f - c), -40f * c * c);
            }
            group.alpha = 1f - c * c;
        }
    }
}

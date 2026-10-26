using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Orders;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// The ticket strip and the HUD readouts. Driven by plain C# events from the services, which
    /// never know it exists (decision D15): delete the HUD and the game still runs.
    /// </summary>
    public sealed class OrderTicketHud : MonoBehaviour
    {
        private const int FlyerCount = 10;
        private const int PopupCount = 4;
        private const float FlySeconds = 0.4f;

        [SerializeField] private RectTransform container;
        [SerializeField] private OrderTicketView ticketPrefab;
        [SerializeField] private CashReadout cash;
        [SerializeField] private ReputationReadout reputation;
        [SerializeField] private CanvasGroup hint;
        [SerializeField] private Camera boardCamera;
        [SerializeField] private RectTransform effectsRoot;
        [SerializeField] private TMP_FontAsset font;

        private struct Flyer
        {
            public Vector3 From;
            public RectTransform Target;
            public float Age;
        }

        private struct Popup
        {
            public float Age;
            public Vector3 From;
        }

        private readonly List<OrderTicketView> _tickets = new List<OrderTicketView>(8);
        private readonly Image[] _flyerViews = new Image[FlyerCount];
        private readonly Flyer[] _flyers = new Flyer[FlyerCount];
        private readonly TextMeshProUGUI[] _popupViews = new TextMeshProUGUI[PopupCount];
        private readonly Popup[] _popups = new Popup[PopupCount];

        private IOrderQueue _queue;
        private EventBus _events;
        private int _nextFlyer, _nextPopup;
        private bool _hintDismissed;

        public void Initialize(IOrderQueue queue, EventBus events, Wallet wallet, int startReputation)
        {
            _queue = queue;
            _events = events;

            _events.Subscribe<OrderCreated>(OnCreated);
            _events.Subscribe<OrderProgressed>(OnProgressed);
            _events.Subscribe<OrderServed>(OnServed);
            _events.Subscribe<OrderExpired>(OnExpired);
            _events.Subscribe<KnifeLaunched>(OnLaunched);

            cash.Initialize(wallet);
            reputation.Initialize(events, startReputation);

            for (int i = 0; i < FlyerCount; i++)
            {
                var go = new GameObject("Flyer", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(effectsRoot, false);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                ((RectTransform)go.transform).sizeDelta = new Vector2(56f, 56f);
                go.SetActive(false);
                _flyerViews[i] = img;
                _flyers[i].Age = -1f;
            }

            for (int i = 0; i < PopupCount; i++)
            {
                var go = new GameObject("CashPopup", typeof(RectTransform));
                go.transform.SetParent(effectsRoot, false);
                var text = go.AddComponent<TextMeshProUGUI>();
                if (font != null) text.font = font;
                text.fontSize = 44f;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(1f, 0.86f, 0.3f);
                text.outlineWidth = 0.2f;
                text.outlineColor = new Color32(60, 30, 10, 255);
                text.raycastTarget = false;
                ((RectTransform)go.transform).sizeDelta = new Vector2(240f, 60f);
                go.SetActive(false);
                _popupViews[i] = text;
                _popups[i].Age = -1f;
            }
        }

        private void OnDestroy()
        {
            if (_events == null) return;

            _events.Unsubscribe<OrderCreated>(OnCreated);
            _events.Unsubscribe<OrderProgressed>(OnProgressed);
            _events.Unsubscribe<OrderServed>(OnServed);
            _events.Unsubscribe<OrderExpired>(OnExpired);
            _events.Unsubscribe<KnifeLaunched>(OnLaunched);
        }

        private void OnCreated(OrderCreated e)
        {
            OrderInstance order = _queue.Find(e.OrderId);
            if (order == null) return;

            OrderTicketView ticket = Instantiate(ticketPrefab, container);
            ticket.Bind(order);
            _tickets.Add(ticket);
        }

        private void OnProgressed(OrderProgressed e)
        {
            OrderTicketView ticket = FindTicket(e.OrderId);
            if (ticket == null) return;

            ticket.OnProgress(e.IngredientId);

            RectTransform target = ticket.LineTarget(e.IngredientId);
            if (target != null && boardCamera != null) Fly(e.Position, target);
        }

        private void OnServed(OrderServed e)
        {
            OrderTicketView ticket = FindTicket(e.OrderId);
            if (ticket == null) return;

            ticket.Close(served: true);
            ShowCashPopup(ticket.transform.position, e.Cash);
        }

        private void OnExpired(OrderExpired e) => FindTicket(e.OrderId)?.Close(served: false);

        private void OnLaunched(KnifeLaunched e) => _hintDismissed |= e.Source == LaunchSource.Manual;

        private OrderTicketView FindTicket(int orderId)
        {
            for (int i = 0; i < _tickets.Count; i++)
                if (_tickets[i].OrderId == orderId && !_tickets[i].IsClosing) return _tickets[i];

            return null;
        }

        private void Fly(Vector2 worldFrom, RectTransform target)
        {
            int i = _nextFlyer;
            _nextFlyer = (_nextFlyer + 1) % FlyerCount;

            // Screen Space - Overlay: a RectTransform's world position is in screen pixels.
            _flyers[i] = new Flyer { From = boardCamera.WorldToScreenPoint(worldFrom), Target = target, Age = 0f };
            Image view = _flyerViews[i];
            view.sprite = target.GetComponent<Image>().sprite;
            view.gameObject.SetActive(true);
            view.transform.position = _flyers[i].From;
        }

        private void ShowCashPopup(Vector3 at, int amount)
        {
            int i = _nextPopup;
            _nextPopup = (_nextPopup + 1) % PopupCount;

            _popups[i] = new Popup { Age = 0f, From = at };
            TextMeshProUGUI view = _popupViews[i];
            view.SetText("+${0}", amount);
            view.gameObject.SetActive(true);
            view.transform.position = at;
        }

        private void Update()
        {
            if (_events == null) return;   // not initialised: the pools are empty

            float dt = Time.unscaledDeltaTime;

            for (int i = _tickets.Count - 1; i >= 0; i--)
            {
                OrderTicketView ticket = _tickets[i];
                if (!ticket.IsClosing) { ticket.Refresh(); continue; }
                if (!ticket.IsFinished) continue;

                Destroy(ticket.gameObject);
                _tickets.RemoveAt(i);
            }

            for (int i = 0; i < FlyerCount; i++)
            {
                if (_flyers[i].Age < 0f) continue;

                ref Flyer f = ref _flyers[i];
                f.Age += dt;
                Image view = _flyerViews[i];
                float t = Mathf.Clamp01(f.Age / FlySeconds);
                if (t >= 1f || f.Target == null) { f.Age = -1f; view.gameObject.SetActive(false); continue; }

                // An arc: ease towards the ticket, lifted at the midpoint.
                float ease = t * t * (3f - 2f * t);
                Vector3 p = Vector3.Lerp(f.From, f.Target.position, ease);
                p.y += Mathf.Sin(t * Mathf.PI) * 80f;
                view.transform.position = p;
                view.transform.localScale = Vector3.one * Mathf.Lerp(1.3f, 0.8f, t);
            }

            for (int i = 0; i < PopupCount; i++)
            {
                if (_popups[i].Age < 0f) continue;

                ref Popup p = ref _popups[i];
                p.Age += dt;
                TextMeshProUGUI view = _popupViews[i];
                if (p.Age >= 1.1f) { p.Age = -1f; view.gameObject.SetActive(false); continue; }

                view.transform.position = p.From + Vector3.down * (90f + 60f * p.Age);
                view.alpha = p.Age < 0.7f ? 1f : 1f - (p.Age - 0.7f) / 0.4f;
            }

            if (hint != null)
                hint.alpha = Mathf.MoveTowards(hint.alpha, _hintDismissed ? 0f : 1f, dt * 2f);
        }
    }
}

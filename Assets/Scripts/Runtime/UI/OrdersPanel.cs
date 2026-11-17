using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// The waiting orders, slid in from the left over the dining room when the player taps the
    /// customers. Tapping a ready ticket serves it (<see cref="OrderTicketView"/>). The run keeps
    /// going while it is open: it is a counter you glance at, not a menu.
    /// </summary>
    public sealed class OrdersPanel : MonoBehaviour
    {
        private const float SlideSeconds = 0.22f;

        [SerializeField] private RectTransform slide;
        [SerializeField] private RectTransform list;
        [SerializeField] private Button closeButton;

        private float _shown;   // 0 hidden … 1 open
        private bool _open;

        public bool IsOpen => _open;

        /// <summary>Where the tickets live (the HUD instantiates them here).</summary>
        public RectTransform List => list;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(() => Close());
            Apply();
        }

        public void Open() => _open = true;

        public void Close(bool instant = false)
        {
            _open = false;
            if (!instant) return;
            _shown = 0f;
            Apply();
        }

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        private void Update()
        {
            float target = _open ? 1f : 0f;
            if (Mathf.Approximately(_shown, target)) return;

            _shown = Mathf.MoveTowards(_shown, target, Time.unscaledDeltaTime / SlideSeconds);
            Apply();
        }

        /// <summary>Slides fully off the left edge when hidden, so it never catches a press.</summary>
        private void Apply()
        {
            if (slide == null) return;
            float ease = 1f - (1f - _shown) * (1f - _shown);
            float width = slide.rect.width + 40f;
            slide.anchoredPosition = new Vector2(Mathf.Lerp(-width, 0f, ease), slide.anchoredPosition.y);
        }
    }
}

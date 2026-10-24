using SliceNServe.Orders;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>The ticket's patience bar (decision D14): green, amber at 60 %, red and shaking at 30 %.</summary>
    public sealed class PatienceBar : MonoBehaviour
    {
        [SerializeField] private Image fill;
        [SerializeField] private RectTransform shakeTarget;

        private static readonly Color NormalColor = new Color(0.30f, 0.80f, 0.35f);
        private static readonly Color HurryColor = new Color(0.98f, 0.70f, 0.15f);
        private static readonly Color CriticalColor = new Color(0.92f, 0.22f, 0.20f);

        private PatienceState _state = PatienceState.Normal;

        public void SetFraction(float fraction, PatienceState state)
        {
            _state = state;
            fill.fillAmount = Mathf.Clamp01(fraction);

            Color color = state switch
            {
                PatienceState.Critical => CriticalColor,
                PatienceState.Hurry => HurryColor,
                PatienceState.Expired => CriticalColor,
                _ => NormalColor
            };

            // Hurry pulses gently; Critical pulses fast, in sync with the shake.
            float pulse = state switch
            {
                PatienceState.Hurry => 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f),
                PatienceState.Critical => 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 14f),
                _ => 1f
            };
            fill.color = color * pulse + new Color(0f, 0f, 0f, 1f - pulse);
        }

        public void SetFrom(OrderInstance order) => SetFraction(order.PatienceFraction, order.State);

        private void Update()
        {
            if (shakeTarget == null) return;

            // Purely presentational: the bar shakes only while the order is critical.
            shakeTarget.anchoredPosition = _state == PatienceState.Critical
                ? new Vector2(Mathf.Sin(Time.unscaledTime * 40f) * 3f, 0f)
                : Vector2.zero;
        }
    }
}

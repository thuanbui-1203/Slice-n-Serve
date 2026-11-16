using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// The strip under the tickets that announces run events: rush hour, with its countdown, and
    /// "bigger orders" when demand levels up. Purely presentational — the run owner tells it
    /// what to say.
    /// </summary>
    public sealed class HudBanner : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Color rushColor = new Color(0.78f, 0.16f, 0.12f, 0.92f);
        [SerializeField] private Color noticeColor = new Color(0.2f, 0.13f, 0.12f, 0.85f);

        private bool _rush;
        private float _noticeLeft;
        private float _t;

        /// <summary>Holds the rush banner up while a rush lasts, with how many are still in line.</summary>
        public void SetRush(bool active, int waiting, float cashBonus)
        {
            _rush = active;
            if (!active) return;

            background.color = rushColor;
            label.SetText("RUSH HOUR!  {0} in line  -  front customer only  -  x{1:1} cash", waiting, cashBonus);
        }

        /// <summary>A short notice, shown when no rush is on.</summary>
        public void Notify(string message, float seconds = 3f)
        {
            if (_rush) return;

            background.color = noticeColor;
            label.SetText(message);
            _noticeLeft = seconds;
        }

        private void Update()
        {
            _noticeLeft -= Time.deltaTime;
            bool visible = _rush || _noticeLeft > 0f;

            _t = Mathf.MoveTowards(_t, visible ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            group.alpha = _t;

            // A rush pulses, so it reads as urgent at a glance.
            float pulse = _rush ? 1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.03f : 1f;
            transform.localScale = Vector3.one * Mathf.Lerp(0.9f, pulse, _t);
        }
    }
}

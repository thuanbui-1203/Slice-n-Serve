using SliceNServe.Core;
using TMPro;
using UnityEngine;

namespace SliceNServe.UI
{
    /// <summary>The HUD cash total. Counts up to the new amount and punches on every change.</summary>
    public sealed class CashReadout : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private RectTransform punchTarget;

        private Wallet _wallet;
        private float _shown;
        private int _target;
        private float _punch;

        public void Initialize(Wallet wallet)
        {
            _wallet = wallet;
            _wallet.Changed += OnChanged;
            _target = wallet.Amount;
            _shown = _target;
            label.SetText("${0}", _target);
        }

        private void OnDestroy()
        {
            if (_wallet != null) _wallet.Changed -= OnChanged;
        }

        private void OnChanged(int amount)
        {
            _target = amount;
            _punch = 1f;
        }

        private void Update()
        {
            if (Mathf.RoundToInt(_shown) != _target)
            {
                _shown = Mathf.MoveTowards(_shown, _target, Mathf.Max(20f, Mathf.Abs(_target - _shown) * 6f) * Time.unscaledDeltaTime);
                label.SetText("${0}", Mathf.RoundToInt(_shown));
            }

            if (punchTarget == null) return;

            _punch = Mathf.MoveTowards(_punch, 0f, Time.unscaledDeltaTime * 3f);
            punchTarget.localScale = Vector3.one * (1f + 0.3f * _punch * _punch);
        }
    }
}

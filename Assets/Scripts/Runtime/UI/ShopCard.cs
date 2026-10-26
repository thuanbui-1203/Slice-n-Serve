using System;
using SliceNServe.Data;
using SliceNServe.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>One upgrade in the shop: what it does now, what the next level adds, and its price.</summary>
    public sealed class ShopCard : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text level;
        [SerializeField] private TMP_Text effect;
        [SerializeField] private Button buy;
        [SerializeField] private Image buyBackground;
        [SerializeField] private TMP_Text price;
        [SerializeField] private RectTransform punchTarget;

        private static readonly Color Affordable = new Color(0.93f, 0.45f, 0.18f);
        private static readonly Color TooExpensive = new Color(0.62f, 0.56f, 0.52f);
        private static readonly Color Maxed = new Color(0.36f, 0.68f, 0.38f);

        private UpgradeDefinition _upgrade;
        private UpgradeService _service;
        private float _punch;

        public event Action<UpgradeDefinition, bool> BuyPressed;

        public void Bind(UpgradeDefinition upgrade, UpgradeService service)
        {
            _upgrade = upgrade;
            _service = service;
            icon.sprite = upgrade.Icon;
            title.SetText(upgrade.DisplayName);
            buy.onClick.AddListener(OnBuy);
            Refresh();
        }

        public void Refresh()
        {
            int owned = _service.LevelOf(_upgrade);
            bool maxed = _service.IsMaxed(_upgrade);

            level.SetText("Lv {0}/{1}", owned, _upgrade.MaxLevel);
            effect.SetText(maxed
                ? $"{_upgrade.Describe(owned)}  (max)"
                : owned == 0
                    ? $"Next: {_upgrade.Describe(1)}"
                    : $"{_upgrade.Describe(owned)} → {_upgrade.Describe(owned + 1)}");

            if (maxed)
            {
                price.SetText("MAX");
                buyBackground.color = Maxed;
                buy.interactable = false;
                return;
            }

            price.SetText("${0}", _service.NextCost(_upgrade));
            bool affordable = _service.CanBuy(_upgrade);
            buyBackground.color = affordable ? Affordable : TooExpensive;
            buy.interactable = true;   // a press you can't afford still gives feedback
        }

        private void OnBuy()
        {
            bool bought = _service.TryBuy(_upgrade);
            _punch = bought ? 1f : -1f;
            BuyPressed?.Invoke(_upgrade, bought);
        }

        private void Update()
        {
            if (_punch == 0f || punchTarget == null) return;

            // Bought: a happy pop. Can't afford: a short shake.
            _punch = Mathf.MoveTowards(_punch, 0f, Time.unscaledDeltaTime * 3f);
            if (_punch > 0f)
            {
                punchTarget.localScale = Vector3.one * (1f + 0.12f * _punch);
                punchTarget.localRotation = Quaternion.identity;
            }
            else
            {
                punchTarget.localScale = Vector3.one;
                punchTarget.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 50f) * 4f * -_punch);
            }
        }
    }
}

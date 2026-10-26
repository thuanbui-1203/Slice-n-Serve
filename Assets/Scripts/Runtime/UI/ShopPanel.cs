using System;
using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// The upgrade shop, opened between runs from the title and end-of-run cards. Knows nothing
    /// about gameplay: it shows <see cref="UpgradeService"/> and forwards presses to it.
    /// </summary>
    public sealed class ShopPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform grid;
        [SerializeField] private ShopCard cardPrefab;
        [SerializeField] private TMP_Text cash;
        [SerializeField] private Button back;

        private readonly List<ShopCard> _cards = new List<ShopCard>(8);

        private UpgradeService _upgrades;
        private Wallet _wallet;
        private Action _onClose;
        private bool _visible;
        private float _t;

        public bool IsOpen => _visible;

        /// <summary>Raised on every buy press: whether it went through. For sound and juice.</summary>
        public event Action<bool> BuyAttempted;

        public void Initialize(UpgradeService upgrades, Wallet wallet)
        {
            _upgrades = upgrades;
            _wallet = wallet;

            for (int i = 0; i < upgrades.Catalog.Count; i++)
            {
                ShopCard card = Instantiate(cardPrefab, grid);
                card.Bind(upgrades.Catalog[i], upgrades);
                card.BuyPressed += OnBuyPressed;
                _cards.Add(card);
            }

            back.onClick.AddListener(Close);
            _wallet.Changed += OnCashChanged;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_wallet != null) _wallet.Changed -= OnCashChanged;
        }

        public void Open(Action onClose)
        {
            _onClose = onClose;
            _visible = true;
            gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;
            RefreshAll();
        }

        public void Close()
        {
            if (!_visible) return;

            _visible = false;
            group.blocksRaycasts = false;
            group.interactable = false;

            Action action = _onClose;
            _onClose = null;
            action?.Invoke();
        }

        private void OnBuyPressed(UpgradeDefinition upgrade, bool bought)
        {
            RefreshAll();   // one purchase changes what every other card can afford
            BuyAttempted?.Invoke(bought);
        }

        private void OnCashChanged(int amount)
        {
            if (_visible) RefreshAll();
        }

        private void RefreshAll()
        {
            cash.SetText("${0}", _wallet.Amount);
            for (int i = 0; i < _cards.Count; i++) _cards[i].Refresh();
        }

        private void Update()
        {
            _t = Mathf.MoveTowards(_t, _visible ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            group.alpha = _t;
            if (!_visible && _t <= 0f) gameObject.SetActive(false);
        }
    }
}

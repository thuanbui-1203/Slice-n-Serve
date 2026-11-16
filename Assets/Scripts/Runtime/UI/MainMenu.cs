using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// The screen the game opens on: Play, Upgrade, Settings, over the live board. Knows nothing
    /// about the run — <see cref="Core.GameRunner"/> hands it the three actions. Fades on
    /// unscaled time, like every other card.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform logo;
        [SerializeField] private Button play;
        [SerializeField] private Button upgrade;
        [SerializeField] private Button settings;
        [SerializeField] private Button exit;
        [SerializeField] private TMP_Text till;
        [SerializeField] private TMP_Text best;
        [SerializeField] private GameObject upgradeBadge;

        private Action _onPlay, _onUpgrade, _onSettings, _onExit;
        private Vector2 _logoHome;
        private bool _visible;
        private float _t;

        public bool IsVisible => _visible;

        private void Awake()
        {
            play.onClick.AddListener(() => Choose(_onPlay));
            upgrade.onClick.AddListener(() => Choose(_onUpgrade));
            settings.onClick.AddListener(() => Choose(_onSettings));
            if (exit != null)
            {
                exit.onClick.AddListener(() => Choose(_onExit));
                // iOS apps never quit themselves (App Store guidelines); the home gesture does it.
                exit.gameObject.SetActive(Application.platform != RuntimePlatform.IPhonePlayer);
            }
            if (logo != null) _logoHome = logo.anchoredPosition;
        }

        /// <summary>Shows the menu. <paramref name="upgradeAffordable"/> lights the badge on Upgrade.</summary>
        public void Show(int cash, int bestServed, bool upgradeAffordable, Action onPlay, Action onUpgrade, Action onSettings,
                         Action onExit = null)
        {
            _onPlay = onPlay;
            _onUpgrade = onUpgrade;
            _onSettings = onSettings;
            _onExit = onExit;

            till.SetText("${0}", cash);
            best.SetText(bestServed > 0 ? $"Best: {bestServed} served" : "");
            if (upgradeBadge != null) upgradeBadge.SetActive(upgradeAffordable);

            _visible = true;
            gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;
        }

        public void Hide()
        {
            _visible = false;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        private void Choose(Action action)
        {
            if (!_visible) return;

            Hide();
            action?.Invoke();
        }

        private void Update()
        {
            _t = Mathf.MoveTowards(_t, _visible ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            group.alpha = _t;

            // The title bobs gently, so the menu reads as alive while it waits.
            if (logo != null)
                logo.anchoredPosition = _logoHome + Vector2.up * (Mathf.Sin(Time.unscaledTime * 1.6f) * 8f);

            if (!_visible && _t <= 0f) gameObject.SetActive(false);
        }
    }
}

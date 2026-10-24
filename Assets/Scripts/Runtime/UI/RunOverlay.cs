using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// The one full-screen card the run uses: the title before a run, pause, and the end-of-run
    /// summary. Fades in and out on unscaled time, because it is shown while the game is paused.
    /// </summary>
    public sealed class RunOverlay : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform card;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text body;
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text buttonLabel;

        private Action _onConfirm;
        private bool _visible;
        private float _t;

        public bool IsVisible => _visible;

        private void Awake() => button.onClick.AddListener(Confirm);

        public void Show(string heading, string message, string action, Action onConfirm)
        {
            title.SetText(heading);
            body.SetText(message);
            buttonLabel.SetText(action);
            _onConfirm = onConfirm;
            _visible = true;
            gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;
        }

        public void Hide()
        {
            _visible = false;
            _onConfirm = null;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        private void Confirm()
        {
            if (!_visible) return;

            Action action = _onConfirm;
            Hide();
            action?.Invoke();
        }

        private void Update()
        {
            _t = Mathf.MoveTowards(_t, _visible ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            group.alpha = _t;

            float ease = 1f - (1f - _t) * (1f - _t);
            card.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, ease);
            if (!_visible && _t <= 0f) gameObject.SetActive(false);
        }
    }
}

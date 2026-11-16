using System;
using SliceNServe.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// Music and sound volume, and fullscreen on PC. Writes straight to <see cref="GameSettings"/>,
    /// so a slider is heard as it moves; saved to disk when the panel closes.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Slider music;
        [SerializeField] private Slider sfx;
        [SerializeField] private Toggle fullscreen;
        [SerializeField] private GameObject fullscreenRow;
        [SerializeField] private Button back;

        private GameSettings _settings;
        private Action _onClose;
        private bool _visible;
        private float _t;

        public bool IsOpen => _visible;

        public void Initialize(GameSettings settings)
        {
            _settings = settings;

            music.onValueChanged.AddListener(v => _settings.MusicVolume = v);
            sfx.onValueChanged.AddListener(v => _settings.SfxVolume = v);
            if (fullscreen != null) fullscreen.onValueChanged.AddListener(v => _settings.Fullscreen = v);
            if (fullscreenRow != null) fullscreenRow.SetActive(GameSettings.SupportsFullscreenToggle);
            back.onClick.AddListener(Close);

            gameObject.SetActive(false);
        }

        public void Open(Action onClose)
        {
            _onClose = onClose;
            music.SetValueWithoutNotify(_settings.MusicVolume);
            sfx.SetValueWithoutNotify(_settings.SfxVolume);
            if (fullscreen != null) fullscreen.SetIsOnWithoutNotify(_settings.Fullscreen);

            _visible = true;
            gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;
        }

        public void Close()
        {
            if (!_visible) return;

            _visible = false;
            group.blocksRaycasts = false;
            group.interactable = false;
            _settings.Save();

            Action action = _onClose;
            _onClose = null;
            action?.Invoke();
        }

        private void Update()
        {
            _t = Mathf.MoveTowards(_t, _visible ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            group.alpha = _t;
            if (!_visible && _t <= 0f) gameObject.SetActive(false);
        }
    }
}

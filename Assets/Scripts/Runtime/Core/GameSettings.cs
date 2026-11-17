using System;
using UnityEngine;

namespace SliceNServe.Core
{
    /// <summary>
    /// Player preferences: volumes and, on PC, fullscreen. Kept in PlayerPrefs rather than the
    /// save file — they belong to the device, not to the kitchen's progress, and wiping a save
    /// should not reset someone's volume.
    /// </summary>
    public sealed class GameSettings
    {
        private const string MusicKey = "sns.musicVolume";
        private const string SfxKey = "sns.sfxVolume";
        private const string AutoThrowKey = "sns.autoThrow";

        private float _music;
        private float _sfx;

        public GameSettings()
        {
            _music = PlayerPrefs.GetFloat(MusicKey, 0.5f);
            _sfx = PlayerPrefs.GetFloat(SfxKey, 0.8f);
        }

        /// <summary>Raised after any setting changes, so audio can follow a slider as it moves.</summary>
        public event Action Changed;

        public float MusicVolume
        {
            get => _music;
            set => Set(ref _music, MusicKey, value);
        }

        public float SfxVolume
        {
            get => _sfx;
            set => Set(ref _sfx, SfxKey, value);
        }

        /// <summary>Only meaningful on desktop; phones are always fullscreen.</summary>
        public static bool SupportsFullscreenToggle => !Application.isMobilePlatform;

        public bool Fullscreen
        {
            get => Screen.fullScreen;
            set
            {
                if (!SupportsFullscreenToggle || Screen.fullScreen == value) return;
                Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                Changed?.Invoke();
            }
        }

        /// <summary>The player's auto-throw switch (it only matters once auto-throw is unlocked). On by default.</summary>
        public bool AutoThrow
        {
            get => PlayerPrefs.GetInt(AutoThrowKey, 1) == 1;
            set
            {
                if (AutoThrow == value) return;
                PlayerPrefs.SetInt(AutoThrowKey, value ? 1 : 0);
                Changed?.Invoke();
            }
        }

        /// <summary>PlayerPrefs only reaches disk on a clean quit unless told to; call when a panel closes.</summary>
        public void Save() => PlayerPrefs.Save();

        private void Set(ref float field, string key, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value)) return;

            field = value;
            PlayerPrefs.SetFloat(key, value);
            Changed?.Invoke();
        }
    }
}

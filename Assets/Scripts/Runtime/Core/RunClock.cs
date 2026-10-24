using System;

namespace SliceNServe.Core
{
    /// <summary>Pause-aware, independently scalable frame delta. Defaults to exactly
    /// <c>Time.deltaTime</c>, so Section 2's behaviour is unchanged until something scales it.</summary>
    public sealed class RunClock
    {
        private float _timeScale = 1f;

        public bool IsPaused { get; private set; }

        /// <summary>Unpaused, scaled seconds since the run started. Orders are stamped with it.</summary>
        public float Elapsed { get; private set; }

        public float TimeScale
        {
            get => _timeScale;
            set => _timeScale = UnityEngine.Mathf.Clamp(value, 0f, 8f);
        }

        public float DeltaTime => IsPaused ? 0f : UnityEngine.Time.deltaTime * _timeScale;
        public float UnscaledDeltaTime => UnityEngine.Time.deltaTime;

        public event Action<bool> PauseChanged;

        /// <summary>Called once per frame by the run owner, before anything reads <see cref="Elapsed"/>.</summary>
        public void Advance() => Elapsed += DeltaTime;

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;

            IsPaused = paused;
            PauseChanged?.Invoke(paused);
        }

        public void Toggle() => SetPaused(!IsPaused);
    }
}

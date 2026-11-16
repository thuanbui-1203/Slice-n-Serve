using SliceNServe.Data;

namespace SliceNServe.Orders
{
    /// <summary>
    /// Rush hour (§4): customers stop walking in one at a time and arrive in waves of
    /// <see cref="BoardConfig.rushWaveMin"/>–<see cref="BoardConfig.rushWaveMax"/>. A new wave
    /// arrives when the last one is gone, for as long as the rush has time left; then normal
    /// service resumes. While a rush is on, the queue serves the front customer only.
    ///
    /// Plain C#, ticked with the run clock's delta by <see cref="OrderSpawner"/>, so it pauses
    /// with the run. Holds no orders itself: the caller says how many wave customers remain.
    /// </summary>
    public sealed class RushHour
    {
        private readonly BoardConfig _config;
        private readonly System.Random _random;
        private float _untilNext;

        public RushHour(BoardConfig config, int seed = 0)
        {
            _config = config;
            _random = seed == 0 ? new System.Random() : new System.Random(seed);
            _untilNext = config.rushFirstAt;
        }

        public bool IsActive { get; private set; }

        /// <summary>Seconds of the rush left; once 0, the wave in progress is the last.</summary>
        public float TimeLeft { get; private set; }

        /// <summary>Seconds until the next rush starts, or 0 during one. For an "incoming" warning.</summary>
        public float UntilNext => IsActive ? 0f : System.Math.Max(0f, _untilNext);

        /// <summary>Advances the rush. Returns how many customers should arrive now — a whole wave — or 0.</summary>
        /// <param name="waveCustomersWaiting">Customers from the current wave still in line.</param>
        public int Tick(float delta, int waveCustomersWaiting)
        {
            if (!IsActive)
            {
                _untilNext -= delta;
                if (_untilNext > 0f) return 0;

                IsActive = true;
                TimeLeft = _config.rushDuration;
                return NextWaveSize();
            }

            TimeLeft = System.Math.Max(0f, TimeLeft - delta);
            if (waveCustomersWaiting > 0) return 0;

            if (TimeLeft > 0f) return NextWaveSize();

            IsActive = false;
            _untilNext = _config.rushEvery;
            return 0;
        }

        private int NextWaveSize()
        {
            int min = System.Math.Max(1, _config.rushWaveMin);
            int max = System.Math.Max(min, _config.rushWaveMax);
            return _random.Next(min, max + 1);
        }
    }
}

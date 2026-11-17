using System;

namespace SliceNServe.Pvp
{
    /// <summary>
    /// The rules of a PvP match that are not about orders: a fixed time, and a score per player —
    /// the cash they earned by serving first. No reputation, no game over: the clock ends it, and
    /// the higher score wins. Plain C#, ticked with the run clock.
    /// </summary>
    public sealed class PvpMatch
    {
        private readonly int[] _scores;

        public PvpMatch(int players, float durationSeconds)
        {
            _scores = new int[players];
            Duration = durationSeconds;
            Remaining = durationSeconds;
        }

        public float Duration { get; }
        public float Remaining { get; private set; }
        public bool Running { get; private set; }
        public bool Finished { get; private set; }
        public int PlayerCount => _scores.Length;

        public event Action Ended;

        public int Score(int player) => _scores[player];

        public void Start()
        {
            if (Finished) return;
            Running = true;
        }

        public void AddScore(int player, int cash)
        {
            if (Finished || player < 0 || player >= _scores.Length) return;
            _scores[player] += cash;
        }

        /// <summary>Online: the opponent reports their own total.</summary>
        public void SetScore(int player, int total)
        {
            if (player >= 0 && player < _scores.Length) _scores[player] = total;
        }

        public void Tick(float delta)
        {
            if (!Running || Finished) return;

            Remaining -= delta;
            if (Remaining > 0f) return;

            Remaining = 0f;
            End();
        }

        /// <summary>Ends early (an opponent left).</summary>
        public void End()
        {
            if (Finished) return;
            Running = false;
            Finished = true;
            Ended?.Invoke();
        }

        /// <summary>The highest score's player, or -1 for a draw.</summary>
        public int Winner
        {
            get
            {
                int best = -1, bestScore = int.MinValue;
                bool tie = false;
                for (int i = 0; i < _scores.Length; i++)
                {
                    if (_scores[i] > bestScore) { best = i; bestScore = _scores[i]; tie = false; }
                    else if (_scores[i] == bestScore) tie = true;
                }
                return tie ? -1 : best;
            }
        }
    }
}

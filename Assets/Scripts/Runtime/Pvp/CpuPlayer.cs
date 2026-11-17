using System.Collections.Generic;
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Orders;

namespace SliceNServe.Pvp
{
    /// <summary>
    /// "Play with CPU": a player 2 that plays itself, built on auto-throw. Its knife throws
    /// whenever it is idle — the die rolls, then a random direction at full speed — exactly as
    /// <see cref="AutoThrower"/> does for a player who has unlocked it. It also hands over its own
    /// ready orders, after a short, human-ish reaction time, so it scores like a person would.
    /// Plain C#, ticked by <see cref="PvpRunner"/> with the run clock.
    /// </summary>
    public sealed class CpuPlayer
    {
        private readonly AutoThrower _thrower;
        private readonly LocalOrderQueue _queue;
        private readonly GameRandom _random;
        private readonly Dictionary<int, float> _serveAt = new Dictionary<int, float>();
        private readonly List<int> _due = new List<int>();
        private float _time;

        /// <summary>Seconds between an order becoming ready and the CPU serving it.</summary>
        public float MinReaction { get; set; } = 0.8f;
        public float MaxReaction { get; set; } = 2.2f;

        public CpuPlayer(RuntimeTuning tuning, KnifeManager knives, LocalOrderQueue queue, GameRandom random)
        {
            _thrower = new AutoThrower(tuning, knives, random) { Enabled = true };
            _queue = queue;
            _random = random;
        }

        public void Tick(float delta, bool diceSettled)
        {
            _time += delta;
            _thrower.Tick(delta, diceSettled);

            // Notice each ready order, then serve it once the reaction time has passed.
            for (int i = 0; i < _queue.ActiveCount; i++)
            {
                OrderInstance o = _queue[i];
                if (o.IsReady && !_serveAt.ContainsKey(o.OrderId))
                    _serveAt[o.OrderId] = _time + _random.Range(MinReaction, MaxReaction);
            }

            _due.Clear();
            foreach (KeyValuePair<int, float> pair in _serveAt)
                if (_time >= pair.Value) _due.Add(pair.Key);
            foreach (int id in _due)
            {
                _serveAt.Remove(id);
                _queue.TryServe(id);   // false if it expired meanwhile: nothing lost
            }
        }
    }
}

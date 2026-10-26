using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// The Sous-Chef Helpers upgrade — the core idle mechanic (concept §3). Each helper throws a
    /// knife on its own timer, aimed straight at an ingredient the oldest waiting customer needs.
    /// Straight shots only: helpers are steady, the player's bank shots are what stay skilful.
    ///
    /// Plain C#, ticked by the run owner with the run clock, so helpers pause with the run.
    /// </summary>
    public sealed class SousChefCrew
    {
        public const int MaxHelpers = 8;

        private readonly RuntimeTuning _tuning;
        private readonly KnifeManager _knives;
        private readonly IngredientSpawner _board;
        private readonly IOrderQueue _orders;
        private readonly float[] _timers = new float[MaxHelpers];
        private int _staggeredFor = -1;

        public SousChefCrew(RuntimeTuning tuning, KnifeManager knives, IngredientSpawner board, IOrderQueue orders)
        {
            _tuning = tuning;
            _knives = knives;
            _board = board;
            _orders = orders;
        }

        public int HelperCount => Mathf.Clamp(_tuning.HelperCount, 0, MaxHelpers);

        public void Tick(float delta)
        {
            int count = HelperCount;
            if (count == 0) return;

            // Spread the helpers evenly across one interval, so they don't all throw at once.
            if (count != _staggeredFor)
            {
                for (int i = 0; i < count; i++) _timers[i] = _tuning.HelperLaunchInterval * (i + 1) / (count + 1);
                _staggeredFor = count;
            }

            for (int i = 0; i < count; i++)
            {
                _timers[i] -= delta;
                if (_timers[i] > 0f) continue;

                _timers[i] += _tuning.HelperLaunchInterval;
                Throw();
            }
        }

        private void Throw()
        {
            Vector2 from = _tuning.LauncherPosition;
            int target = PickTarget(_orders, _board.Live, from);

            Vector2 dir;
            if (target >= 0) dir = (_board.Live[target].Position - from).normalized;
            else dir = new Vector2(Random.Range(-0.6f, 0.6f), 1f).normalized;   // nothing to aim at

            dir = KnifeLauncher.ClampAwayFromParallelWalls(dir);
            _knives.Launch(from, dir * _tuning.HelperLaunchSpeed, LaunchSource.Helper);
        }

        /// <summary>
        /// The nearest live ingredient the oldest order still needs; failing that, any order's
        /// need; failing that, the nearest ingredient of all. -1 when the board is empty.
        /// </summary>
        public static int PickTarget(IOrderQueue orders, IReadOnlyList<Ingredient> live, Vector2 from)
        {
            if (live.Count == 0) return -1;

            OrderInstance oldest = null;
            for (int o = 0; o < orders.ActiveCount; o++)
            {
                OrderInstance order = orders[o];
                if (order.IsClosed) continue;
                if (oldest == null || order.SpawnTime < oldest.SpawnTime) oldest = order;
            }

            if (oldest != null)
            {
                int best = NearestNeeded(oldest, live, from);
                if (best >= 0) return best;
            }

            for (int o = 0; o < orders.ActiveCount; o++)
            {
                if (orders[o].IsClosed) continue;
                int best = NearestNeeded(orders[o], live, from);
                if (best >= 0) return best;
            }

            return Nearest(live, from, null);
        }

        private static int NearestNeeded(OrderInstance order, IReadOnlyList<Ingredient> live, Vector2 from)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int l = 0; l < order.Lines.Length; l++)
            {
                if (order.Lines[l].Satisfied) continue;

                int i = Nearest(live, from, order.Lines[l].Ingredient);
                if (i < 0) continue;

                float sq = (live[i].Position - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        private static int Nearest(IReadOnlyList<Ingredient> live, Vector2 from, IngredientDefinition only)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < live.Count; i++)
            {
                if (only != null && live[i].Definition != only) continue;

                float sq = (live[i].Position - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }
    }
}

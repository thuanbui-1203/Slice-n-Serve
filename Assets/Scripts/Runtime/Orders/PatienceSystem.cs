using SliceNServe.Core;
using SliceNServe.Data;

namespace SliceNServe.Orders
{
    public static class PatienceSystem
    {
        /// <summary>
        /// Linear depletion and banding. Pure arithmetic, so it is testable without a frame loop.
        /// Bands: Normal above the hurry threshold, Hurry down to and including the critical
        /// threshold, Critical below it, Expired at zero.
        /// </summary>
        public static void Tick(OrderInstance order, float delta, RuntimeTuning tuning)
        {
            if (order.IsClosed) return;

            order.RemainingPatience -= delta;
            if (order.RemainingPatience <= 0f)
            {
                order.RemainingPatience = 0f;
                order.State = PatienceState.Expired;
                return;
            }

            order.State = Band(order.PatienceFraction, tuning);
        }

        public static PatienceState Band(float fraction, RuntimeTuning tuning)
        {
            if (fraction > tuning.PatienceHurryThreshold) return PatienceState.Normal;
            if (fraction >= tuning.PatienceCriticalThreshold) return PatienceState.Hurry;
            return PatienceState.Critical;
        }

        /// <summary>
        /// Resolved ONCE at spawn, so a later tuning change never retroactively rewrites a live
        /// customer. Required by §4 and for PvP fairness (plan §6.4).
        /// </summary>
        public static float ResolveDuration(RuntimeTuning tuning, CustomerDefinition customer, IDifficultyProvider difficulty)
            => tuning.BasePatienceSeconds
             * customer.PatienceMultiplier
             * difficulty.PatienceScale
             * tuning.PatienceUpgradeMultiplier;
    }
}

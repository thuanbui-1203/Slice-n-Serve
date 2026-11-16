using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Orders
{
    /// <summary>Everything about a new order that depends on how far into the run it is.
    /// Read at spawn only, so a change never rewrites a live order.</summary>
    public interface IDifficultyProvider
    {
        float PatienceScale { get; }
        float OrderSpawnIntervalScale { get; }

        /// <summary>How much bigger orders are than their recipe; 0 is the recipe as written.</summary>
        int DemandLevel => 0;
    }

    public sealed class ConstantDifficultyProvider : IDifficultyProvider
    {
        public float PatienceScale => 1f;
        public float OrderSpawnIntervalScale => 1f;
    }

    /// <summary>
    /// The §4 ramp, all driven by the run clock — pausing or shopping mid-run never advances it:
    /// <list type="bullet">
    /// <item>patience falls linearly to <see cref="BoardConfig.minPatienceScale"/>, then holds;</item>
    /// <item>orders grow one demand level every <see cref="BoardConfig.demandStepSeconds"/>, forever.</item>
    /// </list>
    /// Rush hour is <see cref="RushHour"/>'s job: its waves depend on the queue, not just the clock.
    /// </summary>
    public sealed class TimeRampDifficultyProvider : IDifficultyProvider
    {
        private readonly RunClock _clock;
        private readonly BoardConfig _config;

        public TimeRampDifficultyProvider(RunClock clock, BoardConfig config)
        {
            _clock = clock;
            _config = config;
        }

        public float PatienceScale => PatienceScaleAt(_clock.Elapsed, _config);
        public float OrderSpawnIntervalScale => 1f;
        public int DemandLevel => DemandLevelAt(_clock.Elapsed, _config);

        public static float PatienceScaleAt(float runTime, BoardConfig config)
            => Mathf.Lerp(1f, config.minPatienceScale, runTime / Mathf.Max(1f, config.patienceRampSeconds));

        public static int DemandLevelAt(float runTime, BoardConfig config)
            => Mathf.Max(0, Mathf.FloorToInt(runTime / Mathf.Max(5f, config.demandStepSeconds)));

        /// <summary>A recipe count at a demand level. Rounds up, so every level asks for at least as much.</summary>
        public static int RequiredAt(int recipeCount, int level, BoardConfig config)
            => Mathf.CeilToInt(recipeCount * (1f + level * config.demandGrowthPerLevel) - 1e-4f);

        public static float RewardScaleAt(int level, BoardConfig config)
            => 1f + level * config.demandRewardPerLevel;
    }
}

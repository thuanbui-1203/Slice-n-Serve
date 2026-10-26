using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Core
{
    /// <summary>
    /// The mutable view of <see cref="BoardConfig"/> that gameplay reads every tick. Section 3's
    /// upgrades and Section 4's difficulty ramp write here; nothing writes to the asset at
    /// runtime, so a play session can never corrupt the authored defaults.
    ///
    /// This exists so that "increase knife speed by 15%" is an assignment, not a code change.
    /// </summary>
    public sealed class RuntimeTuning
    {
        private readonly BoardConfig _config;

        public RuntimeTuning(BoardConfig config) => _config = config;

        /// <summary>Authoring defaults, for a reset button or a new run.</summary>
        public BoardConfig Config => _config;

        // §3 seams — 1.0 until an upgrade says otherwise.
        public float LaunchSpeedMultiplier { get; set; } = 1f;
        public float IngredientSpawnRateMultiplier { get; set; } = 1f;
        public float PatienceMultiplier { get; set; } = 1f;
        public float CashMultiplier { get; set; } = 1f;
        public float IngredientDensityMultiplier { get; set; } = 1f;
        public float SplitChance { get; set; }
        public int HelperCount { get; set; }

        public float HelperLaunchInterval => _config.helperLaunchInterval;
        public float HelperLaunchSpeed => _config.helperLaunchSpeed * LaunchSpeedMultiplier;

        public float LaunchMinSpeed => _config.launchMinSpeed * LaunchSpeedMultiplier;
        public float LaunchMaxSpeed => _config.launchMaxSpeed * LaunchSpeedMultiplier;
        public float SpeedHardCap => _config.speedHardCap;
        public float MinLiveSpeed => _config.minLiveSpeed;
        public float BounceRetention => _config.bounceRetention;
        public float MaxKnifeLifetime => _config.maxKnifeLifetime;
        public int MaxKnifeBounces => _config.maxKnifeBounces;

        public float MaxDragLength => _config.maxDragLength;
        public float DeadZone => _config.deadZone;
        public float IngredientMinSeparation => _config.ingredientMinSeparation;
        public float IngredientRespawnInterval => _config.ingredientRespawnInterval / Mathf.Max(0.01f, IngredientSpawnRateMultiplier);
        public float IngredientLifetime => _config.ingredientLifetime;
        public int TargetIngredientCount => Dense(_config.targetIngredientCount);
        public int WorkingMaxIngredientCount => Dense(_config.workingMaxIngredientCount);
        public int MaxSpawnsPerTick => _config.maxSpawnsPerTick;

        public int MaxActiveOrders => _config.maxActiveOrders;
        public float OrderSpawnInterval => _config.orderSpawnInterval;
        public float BasePatienceSeconds => _config.basePatienceSeconds;
        public float PatienceUpgradeMultiplier => _config.patienceUpgradeMultiplier * PatienceMultiplier;
        public float PatienceHurryThreshold => _config.patienceHurryThreshold;
        public float PatienceCriticalThreshold => _config.patienceCriticalThreshold;
        public int StartReputation => _config.startReputation;
        public float TotalCashMultiplier => _config.cashMultiplier * CashMultiplier;

        private int Dense(int count)
            => Mathf.Min(_config.ingredientCeiling, Mathf.RoundToInt(count * IngredientDensityMultiplier));

        public Rect WorldBounds => _config.worldBounds;
        public Rect IngredientZone => _config.ingredientZone;
        public Vector2 LauncherPosition => _config.launcherPosition;
    }
}

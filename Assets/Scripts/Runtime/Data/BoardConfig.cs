using UnityEngine;

namespace SliceNServe.Data
{
    /// <summary>
    /// Every balance number Section 2 references, in one asset, so a designer can retune the
    /// whole board without touching code. Section 1 reads <see cref="worldBounds"/> for the
    /// camera; Section 2 reads the rest; Sections 3 and 4 retune it at runtime through
    /// <c>RuntimeTuning</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Slice & Serve/Board Config", fileName = "BoardConfig")]
    public sealed class BoardConfig : ScriptableObject
    {
        [Header("Board (plan §5.1)")]
        [Tooltip("Play area in world units, centred on the origin. Section 1's camera adapter reads this.")]
        public Rect worldBounds = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);

        [Tooltip("Where a new knife starts. Must keep ~1 u of clearance to the nearest wall (§5.6).")]
        public Vector2 launcherPosition = new Vector2(0f, -4.4f);

        [Tooltip("Where ingredients may spawn: the board minus edge margins, minus a reserved "
               + "launch corridor at the bottom and a matching band at the top, which the order "
               + "tickets sit over at 16:9.")]
        public Rect ingredientZone = new Rect(-9.1f, -3.3f, 18.2f, 6.6f);

        [Header("Knife (plan §5.2, §5.3)")]
        [Tooltip("Board units of drag that produce full power. Independently of pixels, so mouse "
               + "and touch feel identical.")]
        [Min(0.1f)] public float maxDragLength = 3.5f;

        [Tooltip("Drags shorter than this are taps and are discarded.")]
        [Min(0f)] public float deadZone = 0.35f;

        [Min(0.1f)] public float launchMinSpeed = 8f;
        [Min(0.1f)] public float launchMaxSpeed = 22f;

        [Tooltip("Physics guard applied after every bounce. Never reachable from one launch; it "
               + "exists so a future §3 multiplier cannot defeat CCD.")]
        [Min(0.1f)] public float speedHardCap = 40f;

        [Tooltip("Below this the knife is spent and returns to the launcher.")]
        [Min(0f)] public float minLiveSpeed = 3f;

        [Tooltip("Speed kept after each wall bounce.")]
        [Range(0.5f, 1f)] public float bounceRetention = 0.9f;

        [Min(1f)] public float maxKnifeLifetime = 15f;
        [Min(1)] public int maxKnifeBounces = 60;

        [Header("Ingredients (plan §5.4)")]
        [Min(1)] public int targetIngredientCount = 12;
        [Min(1)] public int workingMaxIngredientCount = 18;
        [Min(0.05f)] public float ingredientRespawnInterval = 0.6f;
        [Min(1f)] public float ingredientLifetime = 12f;
        [Min(0f)] public float ingredientMinSeparation = 0.6f;
        [Min(1)] public int maxSpawnsPerTick = 2;

        [Header("Orders (plan §6.2, §6.4)")]
        [Min(1)] public int maxActiveOrders = 4;
        [Min(0.5f)] public float orderSpawnInterval = 6f;
        [Min(1f)] public float basePatienceSeconds = 25f;
        [Range(0f, 1f)] public float patienceHurryThreshold = 0.6f;
        [Range(0f, 1f)] public float patienceCriticalThreshold = 0.3f;
        [Min(0)] public int startReputation = 3;

        [Tooltip("Fairness bias: each recipe ingredient missing from the live board adds this to "
               + "the dish's weight divisor. A small bias, never a hard rule (plan §6.2).")]
        [Min(0f)] public float missingIngredientPenalty = 0.35f;

        [Tooltip("For this many seconds of a run, dishes needing 3+ units are drawn less often, "
               + "keeping their share at or under ~40 % (tuning table, plan §9).")]
        [Min(0f)] public float earlyGameSeconds = 120f;
        [Range(0f, 1f)] public float earlyComplexDishWeight = 0.5f;

        [Header("Scaling seams (§3, §4)")]
        [Min(0f)] public float cashMultiplier = 1f;
        [Min(0f)] public float patienceUpgradeMultiplier = 1f;
        [Tooltip("Knife Split Chance upgrade: angle in degrees to fan out when splitting.")]
        [Range(5f, 60f)] public float splitAngleDegrees = 25f;
    }
}

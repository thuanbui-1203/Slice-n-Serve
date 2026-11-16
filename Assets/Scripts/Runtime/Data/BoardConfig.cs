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
        [Tooltip("What the camera frames, contain-fit (Section 1). The kitchen decor fills the space "
               + "between this and the play area.")]
        public Rect viewBounds = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);

        [Tooltip("Play area in world units: the walls sit on it. Smaller than the view, so the "
               + "cooking line runs along the bottom and shelves line the sides. The top edge stays "
               + "at the view's, under the order tickets.")]
        public Rect worldBounds = new Rect(-8.0f, -3.8f, 16.0f, 9.2f);

        [Tooltip("Where a new knife starts. Must keep ~1 u of clearance to the nearest wall (§5.6).")]
        public Vector2 launcherPosition = new Vector2(0f, -2.8f);

        [Tooltip("Where ingredients may spawn: the board minus edge margins, minus a reserved "
               + "launch corridor at the bottom and a matching band at the top, which the order "
               + "tickets sit over at 16:9.")]
        public Rect ingredientZone = new Rect(-7.5f, -1.8f, 15.0f, 5.1f);

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

        [Header("Difficulty ramp (§4)")]
        [Tooltip("Seconds of a run over which customers get less patient, from full patience down to minPatienceScale.")]
        [Min(1f)] public float patienceRampSeconds = 240f;
        [Range(0.1f, 1f)] public float minPatienceScale = 0.5f;

        [Tooltip("The floor: no customer's patience drops below this, before the Free Coffee upgrade "
               + "multiplies it. The ramp stops here, so a fully upgraded kitchen outgrows the rush.")]
        [Min(1f)] public float minPatienceSeconds = 9f;

        [Header("Demand growth (§4) — deliberately unbounded")]
        [Tooltip("Every this many seconds of a run, customers order one level bigger. There is no "
               + "cap: with patience floored, every run ends eventually, however upgraded.")]
        [Min(5f)] public float demandStepSeconds = 60f;
        [Tooltip("Each level adds this fraction of a recipe's counts (rounded up): 0.5 turns 2 cabbages into 3, then 4…")]
        [Min(0f)] public float demandGrowthPerLevel = 0.5f;
        [Tooltip("Each level adds this fraction of the dish's cash. Kept above the growth, so bigger orders pay better per ingredient.")]
        [Min(0f)] public float demandRewardPerLevel = 0.6f;

        [Header("Rush hour (§4) — customers arrive in waves")]
        [Min(0f)] public float rushFirstAt = 75f;
        [Tooltip("Seconds of normal service between the end of one rush and the start of the next.")]
        [Min(1f)] public float rushEvery = 90f;
        [Tooltip("A rush lasts at least this long: while time remains, each cleared wave brings the "
               + "next. Once it runs out, the rush ends with the wave in progress.")]
        [Min(1f)] public float rushDuration = 25f;
        [Min(1)] public int rushWaveMin = 6;
        [Min(1)] public int rushWaveMax = 7;
        [Tooltip("A wave forms a line: each place behind the front adds this much patience, so the "
               + "back of the line can wait for the front to be served.")]
        [Min(0f)] public float rushPatiencePerPlace = 8f;
        [Tooltip("Orders placed during a rush pay this much more.")]
        [Min(1f)] public float rushCashBonus = 1.5f;

        [Tooltip("Fairness bias: each recipe ingredient missing from the live board adds this to "
               + "the dish's weight divisor. A small bias, never a hard rule (plan §6.2).")]
        [Min(0f)] public float missingIngredientPenalty = 0.35f;

        [Tooltip("For this many seconds of a run, dishes needing 3+ units are drawn less often, "
               + "keeping their share at or under ~40 % (tuning table, plan §9).")]
        [Min(0f)] public float earlyGameSeconds = 120f;
        [Range(0f, 1f)] public float earlyComplexDishWeight = 0.5f;

        [Header("Upgrades (§3)")]
        [Tooltip("A split knife and its parent fan out by this much either side of the parent's path.")]
        [Range(0f, 60f)] public float splitAngleDegrees = 18f;

        [Tooltip("Seconds between throws for each sous-chef. Helpers are staggered across it.")]
        [Min(0.5f)] public float helperLaunchInterval = 4f;
        [Min(1f)] public float helperLaunchSpeed = 15f;

        [Tooltip("§1 budget: the frame-rate ceiling on live ingredients, whatever the density upgrade says.")]
        [Min(1)] public int ingredientCeiling = 120;

        [Header("Scaling seams (§3, §4)")]
        [Min(0f)] public float cashMultiplier = 1f;
        [Min(0f)] public float patienceUpgradeMultiplier = 1f;
    }
}

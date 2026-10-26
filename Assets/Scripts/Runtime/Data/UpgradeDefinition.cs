using UnityEngine;

namespace SliceNServe.Data
{
    /// <summary>The five upgrade directions from concept §3.</summary>
    public enum UpgradeKind
    {
        KnifeSpeed = 0,
        SplitChance = 1,
        IngredientSpawnRate = 2,
        CustomerPatience = 3,
        SousChef = 4
    }

    /// <summary>
    /// One shop upgrade. Cost grows geometrically per level; the effect grows linearly. Every
    /// number is here, so rebalancing the shop never needs a code change.
    /// </summary>
    [CreateAssetMenu(menuName = "Slice & Serve/Upgrade", fileName = "Upgrade")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        [Tooltip("Stable key used by the save file. Never the asset name.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField] private UpgradeKind kind;

        [SerializeField, Min(1)] private int maxLevel = 5;
        [SerializeField, Min(1)] private int baseCost = 30;

        [Tooltip("Cost multiplier per level owned. Idle games commonly sit between 1.1 and 1.6.")]
        [SerializeField, Min(1f)] private float costGrowth = 1.5f;

        [Tooltip("Effect added per level, in the kind's own unit: a fraction for percentages, a count for helpers.")]
        [SerializeField] private float effectPerLevel = 0.1f;

        [Tooltip("How the shop phrases the total effect. {0} is the effect × displayScale.")]
        [SerializeField] private string effectFormat = "+{0}%";
        [SerializeField] private float displayScale = 100f;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public UpgradeKind Kind => kind;
        public int MaxLevel => maxLevel;

        /// <summary>Price of going from <paramref name="ownedLevel"/> to the next level.</summary>
        public int CostAt(int ownedLevel) => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, ownedLevel));

        /// <summary>Total effect at a level — 0 at level 0.</summary>
        public float EffectAt(int level) => effectPerLevel * Mathf.Clamp(level, 0, maxLevel);

        public string Describe(int level) => string.Format(effectFormat, Mathf.RoundToInt(EffectAt(level) * displayScale));
    }
}

using System;
using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;

namespace SliceNServe.Economy
{
    /// <summary>
    /// The shop's rules: what each upgrade costs, buying it, and turning owned levels into
    /// <see cref="RuntimeTuning"/> values. Plain C#, no scene — every §3 upgrade is a write to
    /// tuning that §2 already reads (plan §3.1), so gameplay code never learns about the shop.
    /// </summary>
    public sealed class UpgradeService
    {
        private readonly IReadOnlyList<UpgradeDefinition> _catalog;
        private readonly SaveService _save;
        private readonly Wallet _wallet;
        private readonly RuntimeTuning _tuning;

        public UpgradeService(IReadOnlyList<UpgradeDefinition> catalog, SaveService save, Wallet wallet, RuntimeTuning tuning)
        {
            _catalog = catalog;
            _save = save;
            _wallet = wallet;
            _tuning = tuning;
            Apply();
        }

        public IReadOnlyList<UpgradeDefinition> Catalog => _catalog;

        /// <summary>Raised after a purchase has been paid for, applied and saved.</summary>
        public event Action<UpgradeDefinition> Purchased;

        public int LevelOf(UpgradeDefinition upgrade) => Math.Min(_save.Data.LevelOf(upgrade.Id), upgrade.MaxLevel);
        public bool IsMaxed(UpgradeDefinition upgrade) => LevelOf(upgrade) >= upgrade.MaxLevel;
        public int NextCost(UpgradeDefinition upgrade) => upgrade.CostAt(LevelOf(upgrade));
        public bool CanBuy(UpgradeDefinition upgrade) => !IsMaxed(upgrade) && _wallet.Amount >= NextCost(upgrade);

        public bool TryBuy(UpgradeDefinition upgrade)
        {
            if (!CanBuy(upgrade) || !_wallet.TrySpend(NextCost(upgrade))) return false;

            _save.Data.SetLevel(upgrade.Id, LevelOf(upgrade) + 1);
            _save.Data.cash = _wallet.Amount;
            Apply();
            _save.Save();
            Purchased?.Invoke(upgrade);
            return true;
        }

        /// <summary>Recomputes every upgrade-driven tuning value from scratch, so applying twice
        /// is harmless and a reset is just "apply with no levels".</summary>
        public void Apply()
        {
            _tuning.LaunchSpeedMultiplier = 1f + Effect(UpgradeKind.KnifeSpeed);
            _tuning.SplitChance = Effect(UpgradeKind.SplitChance);
            _tuning.IngredientSpawnRateMultiplier = 1f + Effect(UpgradeKind.IngredientSpawnRate);
            _tuning.IngredientDensityMultiplier = 1f + Effect(UpgradeKind.IngredientSpawnRate) * 0.5f;
            _tuning.PatienceMultiplier = 1f + Effect(UpgradeKind.CustomerPatience);
            _tuning.HelperCount = (int)Math.Round(Effect(UpgradeKind.SousChef));
        }

        private float Effect(UpgradeKind kind)
        {
            float total = 0f;
            for (int i = 0; i < _catalog.Count; i++)
                if (_catalog[i].Kind == kind) total += _catalog[i].EffectAt(LevelOf(_catalog[i]));
            return total;
        }
    }
}

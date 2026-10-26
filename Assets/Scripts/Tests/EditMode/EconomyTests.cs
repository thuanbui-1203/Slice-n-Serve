using NUnit.Framework;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Economy;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SliceNServe.Tests
{
    public sealed class EconomyTests
    {
        private MemorySaveStore _store;
        private SaveService _save;
        private Wallet _wallet;
        private RuntimeTuning _tuning;
        private UpgradeDefinition _speed, _split, _spawn, _patience, _chef;

        [SetUp]
        public void SetUp()
        {
            _store = new MemorySaveStore();
            _save = new SaveService(_store);
            _wallet = new Wallet();
            _tuning = TestData.Tuning();
            _speed = Upgrade("speed", UpgradeKind.KnifeSpeed, maxLevel: 5, cost: 30, growth: 1.5f, perLevel: 0.1f);
            _split = Upgrade("split", UpgradeKind.SplitChance, maxLevel: 5, cost: 60, growth: 1.5f, perLevel: 0.08f);
            _spawn = Upgrade("spawn", UpgradeKind.IngredientSpawnRate, maxLevel: 5, cost: 40, growth: 1.5f, perLevel: 0.2f);
            _patience = Upgrade("patience", UpgradeKind.CustomerPatience, maxLevel: 5, cost: 40, growth: 1.5f, perLevel: 0.1f);
            _chef = Upgrade("chef", UpgradeKind.SousChef, maxLevel: 2, cost: 100, growth: 2f, perLevel: 1f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var def in new[] { _speed, _split, _spawn, _patience, _chef }) Object.DestroyImmediate(def);
            TestData.Cleanup();
        }

        private UpgradeService Service()
            => new UpgradeService(new[] { _speed, _split, _spawn, _patience, _chef }, _save, _wallet, _tuning);

        private static UpgradeDefinition Upgrade(string id, UpgradeKind kind, int maxLevel, int cost, float growth, float perLevel)
        {
            var def = ScriptableObject.CreateInstance<UpgradeDefinition>();
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("maxLevel").intValue = maxLevel;
            so.FindProperty("baseCost").intValue = cost;
            so.FindProperty("costGrowth").floatValue = growth;
            so.FindProperty("effectPerLevel").floatValue = perLevel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        // ------------------------------------------------------------ wallet

        [Test]
        public void Wallet_SpendsOnlyWhatItHas()
        {
            var wallet = new Wallet(50);
            Assert.That(wallet.TrySpend(60), Is.False);
            Assert.That(wallet.Amount, Is.EqualTo(50));
            Assert.That(wallet.TrySpend(50), Is.True);
            Assert.That(wallet.Amount, Is.EqualTo(0));
            Assert.That(wallet.TrySpend(-5), Is.False);
        }

        // ------------------------------------------------------------ save

        [Test]
        public void Save_RoundTripsCashAndLevels()
        {
            _save.Data.cash = 123;
            _save.Data.SetLevel("speed", 3);
            _save.Save();

            var reloaded = new SaveService(_store);
            Assert.That(reloaded.Data.cash, Is.EqualTo(123));
            Assert.That(reloaded.Data.LevelOf("speed"), Is.EqualTo(3));
            Assert.That(reloaded.Data.LevelOf("unknown"), Is.EqualTo(0));
        }

        [Test]
        public void Save_MissingFile_StartsFresh()
        {
            Assert.That(new SaveService(new MemorySaveStore()).Data.cash, Is.EqualTo(0));
        }

        [Test]
        public void Save_CorruptFile_IsSetAside_NotCrashedOn()
        {
            var store = new MemorySaveStore { Json = "{ this is not json" };
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unreadable"));

            var save = new SaveService(store);

            Assert.That(save.Data.cash, Is.EqualTo(0));
            Assert.That(store.Json, Is.Null, "the corrupt file was set aside");
        }

        [Test]
        public void FileStore_WritesAndReplacesAtomically()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sns_{System.Guid.NewGuid():N}.json");
            try
            {
                var store = new FileSaveStore(path);
                var save = new SaveService(store);
                save.Data.cash = 7;
                save.Save();
                save.Data.cash = 9;
                save.Save();

                Assert.That(new SaveService(new FileSaveStore(path)).Data.cash, Is.EqualTo(9));
                Assert.That(System.IO.File.Exists(path + ".tmp"), Is.False);
            }
            finally
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        // ------------------------------------------------------------ shop rules

        [Test]
        public void Cost_GrowsGeometrically()
        {
            Assert.That(_speed.CostAt(0), Is.EqualTo(30));
            Assert.That(_speed.CostAt(1), Is.EqualTo(45));
            Assert.That(_speed.CostAt(2), Is.EqualTo(68));   // 67.5: Mathf.RoundToInt rounds half to even
        }

        [Test]
        public void Buying_PaysRaisesTheLevelAndSaves()
        {
            _wallet.Add(100);
            UpgradeService shop = Service();

            Assert.That(shop.TryBuy(_speed), Is.True);
            Assert.That(_wallet.Amount, Is.EqualTo(70));
            Assert.That(shop.LevelOf(_speed), Is.EqualTo(1));
            Assert.That(new SaveService(_store).Data.LevelOf("speed"), Is.EqualTo(1), "purchases are written immediately");
            Assert.That(new SaveService(_store).Data.cash, Is.EqualTo(70));
        }

        [Test]
        public void Buying_WithoutEnoughCash_ChangesNothing()
        {
            _wallet.Add(10);
            UpgradeService shop = Service();

            Assert.That(shop.CanBuy(_speed), Is.False);
            Assert.That(shop.TryBuy(_speed), Is.False);
            Assert.That(_wallet.Amount, Is.EqualTo(10));
            Assert.That(shop.LevelOf(_speed), Is.EqualTo(0));
        }

        [Test]
        public void MaxedUpgrades_CannotBeBought()
        {
            _wallet.Add(10000);
            UpgradeService shop = Service();

            Assert.That(shop.TryBuy(_chef), Is.True);
            Assert.That(shop.TryBuy(_chef), Is.True);
            Assert.That(shop.IsMaxed(_chef), Is.True);
            int left = _wallet.Amount;
            Assert.That(shop.TryBuy(_chef), Is.False);
            Assert.That(_wallet.Amount, Is.EqualTo(left));
        }

        [Test]
        public void Levels_AreAppliedToTuning()
        {
            _save.Data.SetLevel("speed", 2);
            _save.Data.SetLevel("split", 3);
            _save.Data.SetLevel("spawn", 1);
            _save.Data.SetLevel("patience", 4);
            _save.Data.SetLevel("chef", 2);

            Service();

            Assert.That(_tuning.LaunchSpeedMultiplier, Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(_tuning.SplitChance, Is.EqualTo(0.24f).Within(1e-5f));
            Assert.That(_tuning.IngredientSpawnRateMultiplier, Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(_tuning.IngredientDensityMultiplier, Is.EqualTo(1.1f).Within(1e-5f));
            Assert.That(_tuning.PatienceMultiplier, Is.EqualTo(1.4f).Within(1e-5f));
            Assert.That(_tuning.HelperCount, Is.EqualTo(2));
            Assert.That(_tuning.LaunchMaxSpeed, Is.EqualTo(22f * 1.2f).Within(1e-4f), "the launch envelope follows the upgrade");
        }

        [Test]
        public void SavedLevelsAboveTheMax_AreClamped()
        {
            _save.Data.SetLevel("chef", 99);
            UpgradeService shop = Service();

            Assert.That(shop.LevelOf(_chef), Is.EqualTo(2));
            Assert.That(_tuning.HelperCount, Is.EqualTo(2));
        }

        [Test]
        public void Density_NeverExceedsTheFrameBudgetCeiling()
        {
            _tuning.IngredientDensityMultiplier = 50f;
            Assert.That(_tuning.TargetIngredientCount, Is.EqualTo(_tuning.Config.ingredientCeiling));
            Assert.That(_tuning.WorkingMaxIngredientCount, Is.EqualTo(_tuning.Config.ingredientCeiling));
        }

        [Test]
        public void Describe_FormatsTheTotalEffect()
        {
            var so = new SerializedObject(_speed);
            so.FindProperty("effectFormat").stringValue = "+{0}% speed";
            so.FindProperty("displayScale").floatValue = 100f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(_speed.Describe(3), Is.EqualTo("+30% speed"));
        }
    }
}

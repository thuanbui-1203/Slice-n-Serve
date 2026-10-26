using System.Collections;
using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SliceNServe.Tests
{
    /// <summary>§3 in the real scene: sous-chefs, knife splitting, and progress surviving a restart.</summary>
    public sealed class IdleAndShopTests
    {
        private MemorySaveStore _store;
        private GameRunner _runner;
        private KnifeManager _knives;
        private IngredientSpawner _ingredients;
        private ServiceRegistry _services;
        private readonly System.Collections.Generic.List<KnifeLaunched> _launches = new System.Collections.Generic.List<KnifeLaunched>();

        private IEnumerator Load()
        {
            SaveService.StoreOverride = _store;
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            _runner = Object.FindAnyObjectByType<GameRunner>();
            _knives = Object.FindAnyObjectByType<KnifeManager>();
            _ingredients = Object.FindAnyObjectByType<IngredientSpawner>();
            _services = ServiceRegistry.Current;
            _launches.Clear();
            _services.Events.Subscribe<KnifeLaunched>(_launches.Add);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _store = new MemorySaveStore();
            yield return Load();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            SaveService.StoreOverride = null;
        }

        [UnityTest]
        public IEnumerator SousChefs_ThrowOnTheirOwn_OnlyWhileTheRunIsLive()
        {
            _services.Tuning.HelperCount = 2;

            Time.timeScale = 5f;
            float until = Time.time + _services.Tuning.HelperLaunchInterval * 1.5f;
            while (Time.time < until) yield return null;
            Assert.That(_launches.Count, Is.EqualTo(0), "no helper throws on the title card");

            _runner.StartRun();
            until = Time.time + _services.Tuning.HelperLaunchInterval * 1.2f;
            while (Time.time < until) yield return null;

            int helperThrows = _launches.FindAll(l => l.Source == LaunchSource.Helper).Count;
            Assert.That(helperThrows, Is.GreaterThanOrEqualTo(2), "each helper throws once per interval");
            Assert.That(_knives.CanLaunch, Is.True, "helper knives never block the player's knife");
        }

        [UnityTest]
        public IEnumerator SousChefs_AimAtWhatTheOldestOrderNeeds()
        {
            _runner.StartRun();
            for (int i = 0; i < 10 && _runner.Queue.ActiveCount == 0; i++) yield return null;
            OrderInstance order = _runner.Queue[0];
            IngredientDefinition needed = order.Lines[0].Ingredient;

            IngredientDefinition other = null;
            foreach (IngredientDefinition d in _ingredients.Definitions)
            {
                bool inOrder = false;
                foreach (OrderLine line in order.Lines) inOrder |= line.Ingredient == d;
                if (!inOrder) { other = d; break; }
            }
            Assert.That(other, Is.Not.Null);

            _ingredients.Clear();
            _ingredients.ForceSpawn(other, new Vector2(0f, -2f));     // closer to the launcher
            _ingredients.ForceSpawn(needed, new Vector2(6f, 3f));     // farther, but needed

            Vector2 from = _services.Tuning.LauncherPosition;
            int pick = SousChefCrew.PickTarget(_runner.Queue, _ingredients.Live, from);
            Assert.That(_ingredients.Live[pick].Definition, Is.SameAs(needed));
        }

        [UnityTest]
        public IEnumerator SplitChance_SplitsEachKnifeAtMostOnce()
        {
            _services.Tuning.SplitChance = 1f;
            Time.timeScale = 10f;

            KnifeController knife = _knives.Launch(new Vector2(0.4f, 1f).normalized * 20f, LaunchSource.Manual);
            int maxLive = 0;
            for (int i = 0; i < 300 && (knife.IsLive || _knives.LiveCount > 0); i++)
            {
                yield return new WaitForFixedUpdate();
                maxLive = Mathf.Max(maxLive, _knives.LiveCount);
            }

            Assert.That(maxLive, Is.EqualTo(2), "a guaranteed split yields exactly one extra knife: children never split");
            Assert.That(_launches.FindAll(l => l.Source == LaunchSource.Split).Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Purchases_AndCash_SurviveARestart()
        {
            _services.Wallet.Add(500);
            UpgradeDefinition chef = null;
            foreach (UpgradeDefinition u in _services.Upgrades.Catalog)
                if (u.Kind == UpgradeKind.SousChef) chef = u;
            Assert.That(chef, Is.Not.Null, "the scene ships a sous-chef upgrade");

            Assert.That(_services.Upgrades.TryBuy(chef), Is.True);
            int cashAfter = _services.Wallet.Amount;
            Assert.That(_services.Tuning.HelperCount, Is.EqualTo(1), "a purchase applies immediately");

            yield return Load();   // the same store, as after quitting and relaunching

            Assert.That(_services.Wallet.Amount, Is.EqualTo(cashAfter));
            Assert.That(_services.Upgrades.LevelOf(chef), Is.EqualTo(1));
            Assert.That(_services.Tuning.HelperCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EndOfRun_BanksTheCash()
        {
            _runner.StartRun();
            _services.Wallet.Add(42);

            Time.timeScale = 30f;
            float timeout = Time.realtimeSinceStartup + 30f;
            while (_runner.State != GameRunner.RunState.Over && Time.realtimeSinceStartup < timeout) yield return null;

            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Over));
            var reloaded = new SaveService(_store);
            Assert.That(reloaded.Data.cash, Is.EqualTo(_services.Wallet.Amount));
            Assert.That(reloaded.Data.runsPlayed, Is.EqualTo(1));
        }
    }
}

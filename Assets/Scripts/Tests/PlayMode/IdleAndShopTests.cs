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
        public IEnumerator SousChefs_WaitForThePlayersThrow()
        {
            _services.Tuning.HelperCount = 2;
            _runner.StartRun();
            _knives.BounceBudget = 50;   // not the die: these tests need one long throw

            Time.timeScale = 5f;
            float until = Time.time + _services.Tuning.HelperLaunchInterval * 1.5f;
            while (Time.time < until) yield return null;
            Assert.That(_launches.Count, Is.EqualTo(0), "no helper throws until the player throws");

            _knives.Launch(new Vector2(0.4f, 1f).normalized * 20f, LaunchSource.Manual);
            until = Time.time + SousChefCrew.FollowDelay * 4f;
            while (Time.time < until && _knives.PlayerKnifeLive) yield return null;

            int helperThrows = _launches.FindAll(l => l.Source == LaunchSource.Helper).Count;
            Assert.That(helperThrows, Is.EqualTo(2), "each helper follows the player's throw at once");
        }

        [UnityTest]
        public IEnumerator SousChefKnives_StopWhenThePlayersKnifeDoes()
        {
            _services.Tuning.HelperCount = 3;
            _runner.StartRun();
            _knives.BounceBudget = 50;   // not the die: these tests need one long throw
            Time.timeScale = 5f;

            _knives.Launch(new Vector2(0.4f, 1f).normalized * 20f, LaunchSource.Manual);
            float timeout = Time.realtimeSinceStartup + 20f;
            int maxLive = 0;
            while (_knives.PlayerKnifeLive && Time.realtimeSinceStartup < timeout)
            {
                maxLive = Mathf.Max(maxLive, _knives.LiveCount);
                yield return null;
            }

            Assert.That(maxLive, Is.GreaterThan(1), "the crew was throwing alongside the player");
            Assert.That(_knives.PlayerKnifeLive, Is.False);
            Assert.That(_knives.LiveCount, Is.EqualTo(0), "the player's knife landing recalls every helper knife");

            int before = _launches.Count;
            float wait = Time.time + _services.Tuning.HelperLaunchInterval * 1.5f;
            while (Time.time < wait) yield return null;
            Assert.That(_launches.Count, Is.EqualTo(before), "and the crew stays idle until the next throw");
        }

        [UnityTest]
        public IEnumerator SousChefs_AimAtWhatTheOldestOrderNeeds()
        {
            _runner.StartRun();
            _knives.BounceBudget = 50;   // not the die: these tests need one long throw
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
            _knives.BounceBudget = 50;   // not the die: these tests need one long throw
            _services.Wallet.Add(42);

            Time.timeScale = 30f;
            float timeout = Time.realtimeSinceStartup + 30f;
            while (_runner.State != GameRunner.RunState.Over && Time.realtimeSinceStartup < timeout) yield return null;

            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Over));
            var reloaded = new SaveService(_store);
            Assert.That(reloaded.Data.cash, Is.EqualTo(_services.Wallet.Amount));
            Assert.That(reloaded.Data.runsPlayed, Is.EqualTo(1));
        }

        private void MaxSpeedAndSplit()
        {
            _services.Wallet.Add(1000000);
            foreach (UpgradeDefinition u in _services.Upgrades.Catalog)
                if (u.Kind == UpgradeKind.KnifeSpeed || u.Kind == UpgradeKind.SplitChance)
                    while (_services.Upgrades.TryBuy(u)) { }
        }

        [UnityTest]
        public IEnumerator AutoThrow_ThrowsWhenTheKnifeIsIdle_AndTurnsTapsIntoRestocks()
        {
            bool before = _services.Settings.AutoThrow;
            try
            {
                Assert.That(_services.Upgrades.AutoThrowUnlocked, Is.False, "a new save has not unlocked it");
                MaxSpeedAndSplit();
                Assert.That(_services.Upgrades.AutoThrowUnlocked, Is.True);
                _services.Settings.AutoThrow = true;

                _runner.StartRun();
                float until = Time.time + DiceView.RollSeconds + _services.Tuning.Config.autoThrowPause + 1f;
                while (Time.time < until && !_launches.Exists(l => l.Source == LaunchSource.Auto)) yield return null;

                Assert.That(_launches.Exists(l => l.Source == LaunchSource.Auto), Is.True, "the knife throws itself");
                Assert.That(_knives.PlayerKnifeLive, Is.True, "an auto-throw is the player's knife: one at a time, sous-chefs follow it");
                Assert.That(Object.FindAnyObjectByType<AimInput>().TapMode, Is.True, "taps restock instead of aiming");

                _services.Settings.AutoThrow = false;
                yield return null;
                Assert.That(Object.FindAnyObjectByType<AimInput>().TapMode, Is.False, "switched off: back to aiming");
            }
            finally
            {
                _services.Settings.AutoThrow = before;
            }
        }

        [Test]
        public void RestockTap_SpawnsNearTheTap_UpToTheWorkingMaximum()
        {
            _ingredients.Clear();
            Vector2 at = _services.Tuning.IngredientZone.center;

            Ingredient spawned = _ingredients.SpawnNear(at);
            Assert.That(spawned, Is.Not.Null);
            Assert.That(Vector2.Distance(spawned.Position, at), Is.LessThan(0.01f), "on the spot when it is clear");

            int guard = 200;
            while (_ingredients.LiveCount < _services.Tuning.WorkingMaxIngredientCount && guard-- > 0)
                _ingredients.SpawnNear(new Vector2(Random.Range(-2f, 7f), Random.Range(-1.5f, 3f)));
            Assert.That(_ingredients.SpawnNear(at), Is.Null, "never past the working maximum");
        }
    }
}

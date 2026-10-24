using System.Collections;
using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Orders;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SliceNServe.Tests
{
    /// <summary>
    /// Runs against the real Game.unity, so the prefabs, layers and collision matrix under test
    /// are the shipping ones. Time is sped up; the physics step stays 0.02 s.
    ///
    /// Asserts invariants and bounds, never exact trajectories (plan §10).
    /// </summary>
    public sealed class GameSceneTests
    {
        private GameRunner _runner;
        private KnifeManager _knives;
        private IngredientSpawner _ingredients;
        private ServiceRegistry _services;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            _runner = Object.FindAnyObjectByType<GameRunner>();
            _knives = Object.FindAnyObjectByType<KnifeManager>();
            _ingredients = Object.FindAnyObjectByType<IngredientSpawner>();
            _services = ServiceRegistry.Current;
            Assert.That(_runner, Is.Not.Null);
            Assert.That(_services, Is.Not.Null);
        }

        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [Test]
        public void Scene_BootsIntoTheTitle_WithAFullBoard()
        {
            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Title));
            Assert.That(_ingredients.LiveCount, Is.EqualTo(_services.Tuning.TargetIngredientCount));
            Assert.That(_knives.CanLaunch, Is.True);
        }

        [UnityTest]
        public IEnumerator Knife_AboveTheDesignCap_NeverLeavesTheBoard_AndAlwaysTerminates()
        {
            Rect bounds = _services.Tuning.WorldBounds;
            const float radius = 0.18f;
            Rect allowed = new Rect(bounds.xMin - radius, bounds.yMin - radius, bounds.width + radius * 2f, bounds.height + radius * 2f);

            Time.timeScale = 20f;
            KnifeController knife = _knives.Launch(new Vector2(0.37f, 1f).normalized * 40f, LaunchSource.Manual);
            Assert.That(knife, Is.Not.Null);
            Assert.That(_knives.CanLaunch, Is.False, "a second manual launch is refused while one is live");
            Assert.That(_knives.Launch(Vector2.up * 10f, LaunchSource.Manual), Is.Null);

            int steps = 0;
            int limit = Mathf.CeilToInt((_services.Tuning.MaxKnifeLifetime + 1f) / Time.fixedDeltaTime);
            while (knife.IsLive && steps < limit)
            {
                yield return new WaitForFixedUpdate();
                steps++;
                Assert.That(allowed.Contains(knife.Body.position), Is.True, $"knife escaped at step {steps}: {knife.Body.position}");
            }

            Assert.That(knife.IsLive, Is.False, "the knife must terminate within maxKnifeLifetime + 1 s");
            Assert.That(knife.BounceCount, Is.GreaterThan(3));
            Assert.That(_knives.CanLaunch, Is.True);
        }

        [UnityTest]
        public IEnumerator Bounces_LoseExactlyTheRetentionFactor()
        {
            Time.timeScale = 10f;
            const float v0 = 20f;
            KnifeController knife = _knives.Launch(new Vector2(0.45f, 1f).normalized * v0, LaunchSource.Manual);

            int checkedBounces = 0;
            int lastBounce = 0;
            for (int i = 0; i < 400 && knife.IsLive && checkedBounces < 5; i++)
            {
                yield return new WaitForFixedUpdate();
                if (knife.BounceCount == lastBounce) continue;

                lastBounce = knife.BounceCount;
                float expected = v0 * Mathf.Pow(_services.Tuning.BounceRetention, lastBounce);
                Assert.That(knife.Body.linearVelocity.magnitude, Is.EqualTo(expected).Within(expected * 0.03f),
                    $"speed after bounce {lastBounce}");
                checkedBounces++;
            }

            Assert.That(checkedBounces, Is.GreaterThanOrEqualTo(3));
        }

        [UnityTest]
        public IEnumerator FastKnife_CollectsAnIngredientOnItsPath_WithoutDeflecting()
        {
            _ingredients.Clear();
            Ingredient target = _ingredients.ForceSpawn(_ingredients.Definitions[0], new Vector2(0f, 0f));
            Assert.That(target, Is.Not.Null);
            yield return new WaitForFixedUpdate();   // let the moved collider sync into physics

            Ingredient sliced = null;
            System.Action<IngredientSliced> onSliced = e => { if (e.Position == Vector2.zero) sliced = target; };
            _services.Events.Subscribe(onSliced);

            KnifeController knife = _knives.Launch(Vector2.up * 40f, LaunchSource.Manual);
            Vector2 launchDir = knife.Body.linearVelocity.normalized;

            // The knife starts 4.4 u below the ingredient and moves 0.8 u per step: it must be
            // collected in the step that crosses it, not a later one.
            for (int i = 0; i < 10 && sliced == null; i++)
            {
                yield return new WaitForFixedUpdate();
                if (knife.BounceCount == 0)
                    Assert.That(Vector2.Dot(knife.Body.linearVelocity.normalized, launchDir), Is.GreaterThan(0.9999f),
                        "ingredients must never deflect the knife");
                if (sliced == null)
                    Assert.That(knife.Body.position.y, Is.LessThan(0.35f + 0.18f + 0.81f), "passed through without collecting");
            }

            _services.Events.Unsubscribe(onSliced);
            Assert.That(sliced, Is.SameAs(target));
            Assert.That(target.gameObject.activeSelf, Is.False);
            _knives.ReturnAllKnives();
        }

        [UnityTest]
        public IEnumerator TheLoopCloses_SlicesServeOrders_AndPayCash()
        {
            _runner.StartRun();
            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Playing));

            // The first customer arrives immediately.
            for (int i = 0; i < 10 && _runner.Queue.ActiveCount == 0; i++) yield return null;
            Assert.That(_runner.Queue.ActiveCount, Is.GreaterThan(0));

            OrderInstance order = _runner.Queue[0];
            int cashBefore = _services.Wallet.Amount;

            // Feed it through the event bus, exactly as the knife's slices arrive.
            for (int l = 0; l < order.Lines.Length; l++)
                for (int n = order.Lines[l].Fulfilled; n < order.Lines[l].Required; n++)
                    _services.Events.Publish(new IngredientSliced(order.Lines[l].Ingredient, Vector2.zero, 1, 0, 1));

            Assert.That(order.State, Is.EqualTo(PatienceState.Served));
            Assert.That(_services.Wallet.Amount, Is.EqualTo(cashBefore + order.Dish.BaseCash));
        }

        [UnityTest]
        public IEnumerator ExpiredOrders_CostReputation_AndEndTheRun()
        {
            _runner.StartRun();
            int start = _runner.Reputation.Reputation;

            Time.timeScale = 30f;
            float timeout = Time.realtimeSinceStartup + 30f;
            while (_runner.State != GameRunner.RunState.Over && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Over));
            Assert.That(_runner.Reputation.Reputation, Is.EqualTo(0));
            Assert.That(start, Is.GreaterThan(0));
        }
    }
}

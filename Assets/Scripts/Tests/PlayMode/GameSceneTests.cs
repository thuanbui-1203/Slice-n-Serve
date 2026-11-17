using System.Collections;
using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Orders;
using SliceNServe.UI;
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
            SaveService.StoreOverride = new MemorySaveStore();   // never the player's real save
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
        public IEnumerator Knife_BrakesAfterItsLastBounce_AndTheNextThrowStartsWhereItStopped()
        {
            _knives.BounceBudget = 2;
            Time.timeScale = 5f;
            const float v0 = 15f;
            Vector2 launcher = _knives.LaunchPoint;
            KnifeController knife = _knives.Launch(new Vector2(0.45f, 1f).normalized * v0, LaunchSource.Manual);
            Assert.That(knife.BounceBudget, Is.EqualTo(2));

            float minBefore = float.MaxValue;
            bool braked = false;
            Vector2 last = knife.Body.position;
            for (int i = 0; i < 900 && knife.IsLive; i++)
            {
                yield return new WaitForFixedUpdate();
                if (!knife.IsLive) break;
                last = knife.Body.position;
                if (!knife.IsBraking) minBefore = Mathf.Min(minBefore, knife.Body.linearVelocity.magnitude);
                else braked = true;
            }

            Assert.That(knife.IsLive, Is.False, "the knife comes to rest");
            Assert.That(braked, Is.True, "it braked after its last bounce");
            Assert.That(knife.BounceCount, Is.InRange(2, 3), "two bounces, then it stops (on a third wall at most)");
            Assert.That(minBefore, Is.EqualTo(v0).Within(v0 * 0.03f), "no friction until the last bounce");

            Vector2 next = _knives.LaunchPoint;
            Assert.That(next, Is.Not.EqualTo(launcher), "the next throw starts where it stopped");
            Assert.That(Vector2.Distance(next, _knives.ClearOfWalls(last)), Is.LessThan(0.6f));
            Rect play = _services.Tuning.Config.worldBounds;
            float c = _services.Tuning.Config.launchWallClearance - 1e-3f;
            Assert.That(next.x >= play.xMin + c && next.x <= play.xMax - c && next.y >= play.yMin + c && next.y <= play.yMax - c,
                        Is.True, "kept clear of the walls");

            // And the next knife really leaves from there.
            KnifeController again = _knives.Launch(Vector2.up * 5f, LaunchSource.Manual);
            Assert.That(Vector2.Distance(again.Body.position, next), Is.LessThan(0.01f));
            _knives.ReturnAllKnives();
        }

        [Test]
        public void Sounds_AreNeverPlayedTheSameWayTwice()
        {
            var audio = Object.FindAnyObjectByType<SliceNServe.UI.GameAudio>();
            for (int i = 0; i < 8; i++)
                _services.Events.Publish(new IngredientSliced(_ingredients.Definitions[0], Vector2.zero, 1, 0, 1));

            var pitches = new System.Collections.Generic.HashSet<float>();
            var pans = new System.Collections.Generic.HashSet<float>();
            foreach (AudioSource s in audio.GetComponents<AudioSource>())
            {
                if (s.loop) continue;   // the music
                pitches.Add(s.pitch);
                pans.Add(s.panStereo);
            }
            Assert.That(pitches.Count, Is.GreaterThan(4), "each play gets its own pitch");
            Assert.That(pans.Count, Is.GreaterThan(4), "and its own place left-right");
        }

        [Test]
        public void SoundFiles_AreWiredByName_AndLevelled()
        {
            var audio = Object.FindAnyObjectByType<SliceNServe.UI.GameAudio>();
            AudioClip Field(string name)
                => (AudioClip)typeof(SliceNServe.UI.GameAudio).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(audio);

            AudioClip rushStart = Field("rushStartClip");
            if (rushStart == null) Assert.Ignore("No sound files in Assets/Audio: the synthesised sounds play.");

            var expected = new (string field, string file)[]
            {
                ("runEndClip", "EndingTheRun_SFX"), ("orderServedClip", "FinishAnOrder_SFX"), ("knifeBounceClip", "KnifeBouncing_SFX"),
                ("knifeLaunchClip", "LaunchingTheKnife_SFX"), ("rushStartClip", "RushHourStart_SFX"), ("rushEndClip", "RushHourEnd_SFX"),
            };
            foreach ((string field, string file) in expected)
                Assert.That(Field(field)?.name, Is.EqualTo(file), field);

            var dice = (AudioClip[])typeof(SliceNServe.UI.GameAudio).GetField("diceRollClips", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(audio);
            Assert.That(dice.Length, Is.EqualTo(2), "both dice rolls are in the pool");

            Assert.That(audio.GainOf(rushStart), Is.LessThan(0.6f), "the loudest file is turned down");
            foreach (AudioClip c in dice) Assert.That(audio.GainOf(c), Is.InRange(0.01f, 1f), "never turned up");
        }

        [UnityTest]
        public IEnumerator EveryThrow_RollsTheDieForTheNext()
        {
            var rolls = new System.Collections.Generic.List<int>();
            System.Action<DiceRolled> onRoll = e => rolls.Add(e.Value);
            _services.Events.Subscribe(onRoll);
            var dice = Object.FindAnyObjectByType<DiceView>();

            _runner.StartRun();
            Assert.That(rolls.Count, Is.EqualTo(1), "the first knife's roll");
            Assert.That(_knives.BounceBudget, Is.InRange(1, 6));
            Assert.That(dice.Value, Is.EqualTo(_knives.BounceBudget));

            Time.timeScale = 5f;
            KnifeController knife = _knives.Launch(new Vector2(0.45f, 1f).normalized * 20f, LaunchSource.Manual);
            Assert.That(knife.BounceBudget, Is.EqualTo(rolls[0]));
            for (int i = 0; i < 900 && knife.IsLive; i++) yield return new WaitForFixedUpdate();
            yield return null;

            _services.Events.Unsubscribe(onRoll);
            Assert.That(knife.BounceCount, Is.InRange(rolls[0], rolls[0] + 1));
            Assert.That(rolls.Count, Is.EqualTo(2), "the knife landed: roll for the next one");
            Assert.That(_knives.BounceBudget, Is.EqualTo(rolls[1]));
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
            // Straight above the launcher, wherever the layout puts it.
            Vector2 at = new Vector2(_services.Tuning.Config.launcherPosition.x, 0f);
            Ingredient target = _ingredients.ForceSpawn(_ingredients.Definitions[0], at);
            Assert.That(target, Is.Not.Null);
            yield return new WaitForFixedUpdate();   // let the moved collider sync into physics

            Ingredient sliced = null;
            System.Action<IngredientSliced> onSliced = e => { if (e.Position == at) sliced = target; };
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

            // Complete is not served: the dish waits until the player hands it over.
            Assert.That(order.IsReady, Is.True);
            Assert.That(_services.Wallet.Amount, Is.EqualTo(cashBefore));

            // Tap the customers, then the ready ticket in the orders panel.
            var panel = Object.FindAnyObjectByType<OrdersPanel>();
            var hud = Object.FindAnyObjectByType<OrderTicketHud>();
            _runner.ToggleOrders();
            Assert.That(panel.IsOpen, Is.True);
            yield return null;
            hud.Ticket(order.OrderId).OnTapped();

            Assert.That(order.State, Is.EqualTo(PatienceState.Served));
            Assert.That(_services.Wallet.Amount, Is.EqualTo(cashBefore + order.Dish.BaseCash));
        }

        [UnityTest]
        public IEnumerator Customers_WalkInForEachOrder_AndTappingThemOpensTheOrders()
        {
            var line = Object.FindAnyObjectByType<CustomerLine>();
            var panel = Object.FindAnyObjectByType<OrdersPanel>();
            var aim = Object.FindAnyObjectByType<AimInput>();
            _runner.StartRun();
            for (int i = 0; i < 10 && _runner.Queue.ActiveCount == 0; i++) yield return null;

            Assert.That(line.Waiting, Is.EqualTo(_runner.Queue.ActiveCount), "one customer in line per order");
            Assert.That(line.Area.xMax, Is.LessThan(_services.Tuning.Config.worldBounds.xMin), "the dining room is left of the board");

            // A press in the dining room is claimed: it opens the panel and never starts a throw.
            Assert.That(aim.PressFilter(line.Area.center), Is.True);
            Assert.That(panel.IsOpen, Is.True);
            Assert.That(aim.PressFilter(_services.Tuning.Config.worldBounds.center), Is.False, "the board still aims");

            // Once served, the customer walks out.
            OrderInstance order = _runner.Queue[0];
            for (int l = 0; l < order.Lines.Length; l++)
                for (int n = order.Lines[l].Fulfilled; n < order.Lines[l].Required; n++)
                    _services.Events.Publish(new IngredientSliced(order.Lines[l].Ingredient, Vector2.zero, 1, 0, 1));
            int waiting = line.Waiting;
            Assert.That(_runner.Queue.TryServe(order.OrderId), Is.True);
            Assert.That(line.Waiting, Is.EqualTo(waiting - 1));
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

using System.Collections;
using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Orders;
using SliceNServe.Pvp;
using SliceNServe.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SliceNServe.Tests
{
    /// <summary>§5 "Play with Friends" in the real scene: two boards, each with its own customers.</summary>
    public sealed class PvpSceneTests
    {
        private PvpRunner _pvp;
        private ServiceRegistry _services;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            SaveService.StoreOverride = new MemorySaveStore();
            MatchSetup.BeginLocal(seed: 42, duration: 30f);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            _pvp = Object.FindAnyObjectByType<PvpRunner>();
            _services = ServiceRegistry.Current;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            MatchSetup.Clear();
            SaveService.StoreOverride = null;
        }

        [Test]
        public void LocalMatch_HasTwoBoards_AndTheSoloRunnerStandsAside()
        {
            Assert.That(_pvp.enabled, Is.True);
            Assert.That(Object.FindAnyObjectByType<GameRunner>().enabled, Is.False);
            Assert.That(_pvp.PlayerCount, Is.EqualTo(2));
            Assert.That(Object.FindObjectsByType<KnifeManager>(FindObjectsSortMode.None).Length, Is.EqualTo(2));

            Rect left = _pvp.AimOf(0).Region.Value, right = _pvp.AimOf(1).Region.Value;
            Assert.That(left.Overlaps(right), Is.False, "each player aims on their own half");
            Assert.That(_pvp.IngredientsOf(0).LiveCount, Is.GreaterThan(0));
            Assert.That(_pvp.IngredientsOf(1).LiveCount, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator BothPlayersThrow_AtTheSameTime()
        {
            _pvp.BeginPlay();
            yield return null;

            KnifeController k1 = _pvp.KnivesOf(0).Launch(new Vector2(0.3f, 1f).normalized * 15f, LaunchSource.Manual);
            KnifeController k2 = _pvp.KnivesOf(1).Launch(new Vector2(-0.3f, 1f).normalized * 15f, LaunchSource.Manual);
            Assert.That(k1, Is.Not.Null);
            Assert.That(k2, Is.Not.Null);
            Assert.That(k1.KnifeId, Is.Not.EqualTo(k2.KnifeId));

            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            Assert.That(PvpLayout.Half(_services.Tuning.Config, true).worldBounds.Contains(k1.Body.position) || !k1.IsLive, Is.True);
            Assert.That(PvpLayout.Half(_services.Tuning.Config, false).worldBounds.Contains(k2.Body.position) || !k2.IsLive, Is.True);
        }

        [UnityTest]
        public IEnumerator EachPlayer_ServesTheirOwnCustomers()
        {
            _pvp.BeginPlay();
            for (int i = 0; i < 20 && (_pvp.QueueOf(0).ActiveCount == 0 || _pvp.QueueOf(1).ActiveCount == 0); i++) yield return null;
            Assert.That(_pvp.QueueOf(0).ActiveCount, Is.GreaterThan(0), "player 1's first customer arrived");
            Assert.That(_pvp.QueueOf(1).ActiveCount, Is.GreaterThan(0), "player 2's first customer arrived");

            OrderInstance theirs = _pvp.QueueOf(1)[0];
            OrderInstance mine = _pvp.QueueOf(0)[0];
            Assert.That(theirs, Is.Not.SameAs(mine), "each player has their own customers");
            int mineBefore = mine.Lines[0].Fulfilled;

            // Player 2 slices everything their order needs, on their own board.
            foreach (OrderLine line in theirs.Lines)
                for (int n = 0; n < line.Required; n++)
                    _services.Events.Publish(new IngredientSliced(line.Ingredient, Vector2.zero, 1, 0, 1, playerId: 1));

            Assert.That(theirs.IsReady, Is.True);
            Assert.That(mine.Lines[0].Fulfilled, Is.EqualTo(mineBefore), "player 1's customer is untouched");

            Assert.That(_pvp.QueueOf(1).TryServe(theirs.OrderId), Is.True);
            Assert.That(_pvp.Match.Score(1), Is.GreaterThan(0));
            Assert.That(_pvp.Match.Score(0), Is.EqualTo(0));
            Assert.That(_pvp.QueueOf(0)[0], Is.SameAs(mine), "player 1's line is unaffected");
            Assert.That(mine.IsClosed, Is.False);
        }

        [Test]
        public void EachPlayer_HasTheirOwnCustomerSpawner()
        {
            Assert.That(Object.FindObjectsByType<OrderSpawner>(FindObjectsSortMode.None).Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator TheClockEndsTheMatch_WithAResultsCard()
        {
            _pvp.BeginPlay();
            Time.timeScale = 20f;
            float until = Time.realtimeSinceStartup + 10f;
            while (!_pvp.Match.Finished && Time.realtimeSinceStartup < until) yield return null;
            Time.timeScale = 1f;

            Assert.That(_pvp.Match.Finished, Is.True);
            var overlay = Object.FindAnyObjectByType<RunOverlay>();
            Assert.That(overlay, Is.Not.Null, "the results card is up");
        }
    }

    /// <summary>Play with CPU: player 2 is the auto-player.</summary>
    public sealed class CpuMatchTests
    {
        private PvpRunner _pvp;
        private ServiceRegistry _services;
        private readonly System.Collections.Generic.List<KnifeLaunched> _launches = new System.Collections.Generic.List<KnifeLaunched>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            SaveService.StoreOverride = new MemorySaveStore();
            MatchSetup.BeginLocal(seed: 7, duration: 60f, vsCpu: true);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            _pvp = Object.FindAnyObjectByType<PvpRunner>();
            _services = ServiceRegistry.Current;
            _launches.Clear();
            _services.Events.Subscribe<KnifeLaunched>(_launches.Add);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            MatchSetup.Clear();
            SaveService.StoreOverride = null;
        }

        [UnityTest]
        public IEnumerator TheCpu_ThrowsByItself_AndTakesNoInput()
        {
            Assert.That(_pvp.VsCpu, Is.True);
            _pvp.BeginPlay();
            Assert.That(_pvp.AimOf(1).Accepting, Is.False, "nobody aims for the CPU");
            Assert.That(_pvp.AimOf(0).Accepting, Is.True);

            float until = Time.time + DiceView.RollSeconds + _services.Tuning.Config.autoThrowPause + 1.5f;
            while (Time.time < until && !_launches.Exists(l => l.PlayerId == 1 && l.Source == LaunchSource.Auto)) yield return null;

            Assert.That(_launches.Exists(l => l.PlayerId == 1 && l.Source == LaunchSource.Auto), Is.True, "the CPU threw");
            Assert.That(_launches.Exists(l => l.PlayerId == 0), Is.False, "and never for you");
        }

        [UnityTest]
        public IEnumerator TheCpu_ServesItsReadyOrders()
        {
            _pvp.BeginPlay();
            for (int i = 0; i < 30 && _pvp.QueueOf(1).ActiveCount == 0; i++) yield return null;
            OrderInstance order = _pvp.QueueOf(1)[0];
            foreach (OrderLine line in order.Lines)
                for (int n = line.Fulfilled; n < line.Required; n++)
                    _services.Events.Publish(new IngredientSliced(line.Ingredient, Vector2.zero, 1, 0, 1, playerId: 1));
            Assert.That(order.IsReady || order.State == PatienceState.Served, Is.True);

            float until = Time.time + 4f;
            while (Time.time < until && _pvp.Match.Score(1) == 0) yield return null;
            Assert.That(_pvp.Match.Score(1), Is.GreaterThan(0), "served after a short reaction time");
        }
    }

    /// <summary>The menu's PvP entries.</summary>
    public sealed class PvpMenuTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveService.StoreOverride = new MemorySaveStore();
            MatchSetup.Clear();
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            MatchSetup.Clear();
            SaveService.StoreOverride = null;
        }

        private static Button MenuButton(string name)
            => Object.FindAnyObjectByType<MainMenu>().transform.Find($"Card/{name}").GetComponent<Button>();

        [UnityTest]
        public IEnumerator PlayWithFriends_StartsALocalMatch()
        {
            Assert.That(MenuButton("Friends").gameObject.activeInHierarchy, Is.True);
            Assert.That(MenuButton("Pvp").gameObject.activeInHierarchy, Is.True);

            MenuButton("Friends").onClick.Invoke();
            yield return null;
            var chooser = Object.FindAnyObjectByType<VersusChooser>(FindObjectsInactive.Include);
            Assert.That(chooser.IsOpen, Is.True, "first: with a friend, or with the CPU?");

            chooser.transform.Find("Card/Friend").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
            yield return null;

            Assert.That(MatchSetup.Mode, Is.EqualTo(MatchMode.LocalVersus));
            Assert.That(MatchSetup.VsCpu, Is.False);
            Assert.That(Object.FindAnyObjectByType<PvpRunner>().PlayerCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator PlayWithFriends_ThenCpu_StartsAMatchAgainstTheCpu()
        {
            MenuButton("Friends").onClick.Invoke();
            yield return null;
            var chooser = Object.FindAnyObjectByType<VersusChooser>(FindObjectsInactive.Include);
            chooser.transform.Find("Card/Cpu").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
            yield return null;

            Assert.That(MatchSetup.VsCpu, Is.True);
            Assert.That(Object.FindAnyObjectByType<PvpRunner>().VsCpu, Is.True);
        }

        [UnityTest]
        public IEnumerator Chooser_BackReturnsToTheMenu()
        {
            MenuButton("Friends").onClick.Invoke();
            yield return null;
            Object.FindAnyObjectByType<VersusChooser>(FindObjectsInactive.Include).Close();
            yield return null;
            Assert.That(Object.FindAnyObjectByType<MainMenu>().IsVisible, Is.True);
            Assert.That(MatchSetup.Mode, Is.EqualTo(MatchMode.Solo));
        }

        [UnityTest]
        public IEnumerator EnterPvpMatch_OpensTheLobby_AndBackReturns()
        {
            MenuButton("Pvp").onClick.Invoke();
            yield return null;
            var lobby = Object.FindAnyObjectByType<PvpLobby>(FindObjectsInactive.Include);
            Assert.That(lobby.IsOpen, Is.True);

            lobby.Close();
            yield return null;
            Assert.That(Object.FindAnyObjectByType<MainMenu>().IsVisible, Is.True);
        }
    }
}

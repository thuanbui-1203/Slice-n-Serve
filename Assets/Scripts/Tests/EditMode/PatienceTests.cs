using System.Collections.Generic;
using NUnit.Framework;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using UnityEngine;

namespace SliceNServe.Tests
{
    public sealed class PatienceTests
    {
        private sealed class FixedDifficulty : IDifficultyProvider
        {
            public float PatienceScale { get; set; } = 1f;
            public float OrderSpawnIntervalScale => 1f;
        }

        private RuntimeTuning _tuning;
        private IngredientDefinition _tomato;

        [SetUp]
        public void SetUp()
        {
            _tuning = TestData.Tuning();
            _tomato = TestData.Ingredient("tomato");
        }

        [TearDown] public void TearDown() => TestData.Cleanup();

        private OrderInstance Order(float duration)
            => OrderInstance.Create(1, TestData.Dish("d", 10, (_tomato, 1)), TestData.Customer("c"), 0f, duration);

        [Test]
        public void Duration_IsBaseTimesCustomerTimesDifficultyTimesUpgrade()
        {
            CustomerDefinition patient = TestData.Customer("p", patience: 1.4f);
            var difficulty = new FixedDifficulty { PatienceScale = 0.5f };
            _tuning.PatienceMultiplier = 2f;

            // 25 × 1.4 × 0.5 × 2
            Assert.That(PatienceSystem.ResolveDuration(_tuning, patient, difficulty), Is.EqualTo(35f).Within(1e-4f));
        }

        [Test]
        public void Bands_ChangeAtExactly60And30Percent()
        {
            Assert.That(PatienceSystem.Band(0.61f, _tuning), Is.EqualTo(PatienceState.Normal));
            Assert.That(PatienceSystem.Band(0.60f, _tuning), Is.EqualTo(PatienceState.Hurry));
            Assert.That(PatienceSystem.Band(0.30f, _tuning), Is.EqualTo(PatienceState.Hurry));
            Assert.That(PatienceSystem.Band(0.2999f, _tuning), Is.EqualTo(PatienceState.Critical));
        }

        [Test]
        public void Tick_DepletesLinearly_AndBands()
        {
            OrderInstance o = Order(10f);

            PatienceSystem.Tick(o, 3f, _tuning);
            Assert.That(o.RemainingPatience, Is.EqualTo(7f).Within(1e-5f));
            Assert.That(o.State, Is.EqualTo(PatienceState.Normal));

            PatienceSystem.Tick(o, 2f, _tuning);
            Assert.That(o.State, Is.EqualTo(PatienceState.Hurry));

            PatienceSystem.Tick(o, 3f, _tuning);
            Assert.That(o.State, Is.EqualTo(PatienceState.Critical));
        }

        [Test]
        public void Tick_ExpiresAtExactlyZero_AndThenStops()
        {
            OrderInstance o = Order(10f);

            // Binary-exact steps, so "exactly zero" is exact.
            PatienceSystem.Tick(o, 9.5f, _tuning);
            Assert.That(o.State, Is.Not.EqualTo(PatienceState.Expired));

            PatienceSystem.Tick(o, 0.5f, _tuning);
            Assert.That(o.State, Is.EqualTo(PatienceState.Expired));
            Assert.That(o.RemainingPatience, Is.EqualTo(0f));

            PatienceSystem.Tick(o, 5f, _tuning);
            Assert.That(o.RemainingPatience, Is.EqualTo(0f), "an expired order stops depleting");
        }

        [Test]
        public void ServedOrders_StopDepleting()
        {
            OrderInstance o = Order(10f);
            o.State = PatienceState.Served;

            PatienceSystem.Tick(o, 20f, _tuning);
            Assert.That(o.RemainingPatience, Is.EqualTo(10f));
            Assert.That(o.State, Is.EqualTo(PatienceState.Served));
        }

        [Test]
        public void ExpiredOrder_LeavesTheQueue_AndNeverServes()
        {
            var events = new EventBus();
            var expired = new List<OrderExpired>();
            var served = new List<OrderServed>();
            events.Subscribe<OrderExpired>(expired.Add);
            events.Subscribe<OrderServed>(served.Add);
            var queue = new LocalOrderQueue(events, _tuning);
            queue.Add(Order(1f));

            queue.Tick(1.5f);
            queue.SubmitIngredient(_tomato, Vector2.zero);

            Assert.That(expired.Count, Is.EqualTo(1));
            Assert.That(served, Is.Empty);
            Assert.That(queue.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void PausedClock_YieldsZeroDelta()
        {
            var clock = new RunClock();
            clock.SetPaused(true);
            Assert.That(clock.DeltaTime, Is.EqualTo(0f));
        }

        [Test]
        public void Reputation_DecrementsAndEndsTheRunAtZero()
        {
            var events = new EventBus();
            int gameOvers = 0;
            events.Subscribe<GameOver>(_ => gameOvers++);
            var rep = new ReputationService(events, _tuning);

            rep.Penalise(); rep.Penalise();
            Assert.That(rep.IsGameOver, Is.False);
            rep.Penalise();
            Assert.That(rep.IsGameOver, Is.True);
            rep.Penalise();
            Assert.That(rep.Reputation, Is.EqualTo(0));
            Assert.That(gameOvers, Is.EqualTo(1));
        }
    }
}

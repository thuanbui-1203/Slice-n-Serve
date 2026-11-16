using NUnit.Framework;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;

namespace SliceNServe.Tests
{
    public sealed class DifficultyTests
    {
        private sealed class RampDifficulty : IDifficultyProvider
        {
            public float PatienceScale { get; set; } = 1f;
            public float OrderSpawnIntervalScale => 1f;
        }

        [TearDown] public void TearDown() => TestData.Cleanup();

        [Test]
        public void ConstantProvider_IsNeutral()
        {
            var d = new ConstantDifficultyProvider();
            Assert.That(d.PatienceScale, Is.EqualTo(1f));
            Assert.That(d.OrderSpawnIntervalScale, Is.EqualTo(1f));
        }

        [Test]
        public void DifficultyApplies_AtSpawn_NotToLiveOrders()
        {
            RuntimeTuning tuning = TestData.Tuning();
            CustomerDefinition customer = TestData.Customer("c");
            DishDefinition dish = TestData.Dish("d", 10, (TestData.Ingredient("x"), 1));
            var difficulty = new RampDifficulty();

            OrderInstance early = OrderInstance.Create(1, dish, customer, 0f,
                PatienceSystem.ResolveDuration(tuning, customer, difficulty));

            difficulty.PatienceScale = 0.5f;
            OrderInstance late = OrderInstance.Create(2, dish, customer, 1f,
                PatienceSystem.ResolveDuration(tuning, customer, difficulty));

            Assert.That(late.PatienceDuration, Is.EqualTo(12.5f).Within(1e-4f));
            Assert.That(early.PatienceDuration, Is.EqualTo(25f).Within(1e-4f), "live orders keep their spawn-time patience");
        }

        [Test]
        public void Ramp_FallsLinearly_ThenHolds()
        {
            BoardConfig config = TestData.Tuning(c => { c.patienceRampSeconds = 200f; c.minPatienceScale = 0.5f; }).Config;

            Assert.That(TimeRampDifficultyProvider.PatienceScaleAt(0f, config), Is.EqualTo(1f));
            Assert.That(TimeRampDifficultyProvider.PatienceScaleAt(100f, config), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(TimeRampDifficultyProvider.PatienceScaleAt(200f, config), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(TimeRampDifficultyProvider.PatienceScaleAt(5000f, config), Is.EqualTo(0.5f).Within(1e-5f), "the ramp stops at its minimum");
        }

        [Test]
        public void Ramp_FollowsTheRunClock()
        {
            var clock = new RunClock();
            var ramp = new TimeRampDifficultyProvider(clock, TestData.Tuning().Config);
            Assert.That(ramp.PatienceScale, Is.EqualTo(1f), "a new run starts at full patience");
            Assert.That(ramp.OrderSpawnIntervalScale, Is.EqualTo(1f));
        }

        [Test]
        public void Demand_GrowsWithoutLimit_AndPaysMoreThanItAsks()
        {
            BoardConfig config = TestData.Tuning(c => { c.demandStepSeconds = 60f; c.demandGrowthPerLevel = 0.5f; c.demandRewardPerLevel = 0.6f; }).Config;

            Assert.That(TimeRampDifficultyProvider.DemandLevelAt(59f, config), Is.EqualTo(0));
            Assert.That(TimeRampDifficultyProvider.DemandLevelAt(60f, config), Is.EqualTo(1));
            Assert.That(TimeRampDifficultyProvider.DemandLevelAt(6000f, config), Is.EqualTo(100), "no ceiling");

            Assert.That(TimeRampDifficultyProvider.RequiredAt(1, 0, config), Is.EqualTo(1));
            Assert.That(TimeRampDifficultyProvider.RequiredAt(1, 1, config), Is.EqualTo(2), "rounded up");
            Assert.That(TimeRampDifficultyProvider.RequiredAt(2, 2, config), Is.EqualTo(4));
            Assert.That(TimeRampDifficultyProvider.RequiredAt(2, 20, config), Is.EqualTo(22));

            for (int level = 1; level < 30; level++)
            {
                Assert.That(TimeRampDifficultyProvider.RequiredAt(1, level, config),
                            Is.GreaterThanOrEqualTo(TimeRampDifficultyProvider.RequiredAt(1, level - 1, config)));
                Assert.That(TimeRampDifficultyProvider.RewardScaleAt(level, config),
                            Is.GreaterThan(TimeRampDifficultyProvider.RewardScaleAt(level - 1, config)));
            }
        }

        [Test]
        public void Demand_ScalesTheOrderItSpawns()
        {
            DishDefinition dish = TestData.Dish("d", 10, (TestData.Ingredient("x"), 2), (TestData.Ingredient("y"), 1));
            BoardConfig config = TestData.Tuning().Config;

            OrderInstance o = OrderInstance.Create(1, dish, TestData.Customer("c"), 0f, 20f,
                                                   n => TimeRampDifficultyProvider.RequiredAt(n, 2, config));

            Assert.That(o.Lines[0].Required, Is.EqualTo(4));
            Assert.That(o.Lines[1].Required, Is.EqualTo(2));
        }

        private static BoardConfig RushConfig()
            => TestData.Tuning(c => { c.rushFirstAt = 75f; c.rushEvery = 90f; c.rushDuration = 25f; c.rushWaveMin = 6; c.rushWaveMax = 7; }).Config;

        [Test]
        public void RushHour_StartsWithAWaveOfSixOrSeven()
        {
            var rush = new RushHour(RushConfig(), seed: 1);

            Assert.That(rush.Tick(74f, 0), Is.EqualTo(0));
            Assert.That(rush.IsActive, Is.False);
            Assert.That(rush.UntilNext, Is.EqualTo(1f).Within(1e-4f));

            int wave = rush.Tick(1f, 0);
            Assert.That(rush.IsActive, Is.True);
            Assert.That(wave, Is.InRange(6, 7));
        }

        [Test]
        public void RushHour_SendsTheNextWave_OnlyWhenTheLineIsClear()
        {
            var rush = new RushHour(RushConfig(), seed: 2);
            rush.Tick(75f, 0);

            Assert.That(rush.Tick(5f, 4), Is.EqualTo(0), "no new wave while customers are still in line");
            Assert.That(rush.Tick(1f, 0), Is.InRange(6, 7), "line cleared with time left: the next wave");
        }

        [Test]
        public void RushHour_EndsWithTheLastWave_ThenWaitsForTheNext()
        {
            var rush = new RushHour(RushConfig(), seed: 3);
            rush.Tick(75f, 0);

            rush.Tick(30f, 3);   // time is up, but the wave is still being served
            Assert.That(rush.IsActive, Is.True, "a rush never abandons a line mid-wave");
            Assert.That(rush.TimeLeft, Is.EqualTo(0f));

            Assert.That(rush.Tick(1f, 0), Is.EqualTo(0));
            Assert.That(rush.IsActive, Is.False);
            Assert.That(rush.UntilNext, Is.EqualTo(90f).Within(1e-4f));
        }

        [Test]
        public void Patience_NeverFallsBelowTheFloor()
        {
            RuntimeTuning tuning = TestData.Tuning(c => { c.basePatienceSeconds = 25f; c.minPatienceSeconds = 9f; });
            CustomerDefinition hasty = TestData.Customer("hasty", patience: 0.6f);
            var difficulty = new RampDifficulty { PatienceScale = 0.1f };   // far harsher than the ramp allows

            Assert.That(PatienceSystem.ResolveDuration(tuning, hasty, difficulty), Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void PatienceUpgrade_StillPaysOff_AtTheFloor()
        {
            RuntimeTuning tuning = TestData.Tuning(c => { c.basePatienceSeconds = 25f; c.minPatienceSeconds = 9f; });
            tuning.PatienceMultiplier = 1.5f;   // Free Coffee, maxed
            CustomerDefinition hasty = TestData.Customer("hasty", patience: 0.6f);
            var difficulty = new RampDifficulty { PatienceScale = 0.1f };

            Assert.That(PatienceSystem.ResolveDuration(tuning, hasty, difficulty), Is.EqualTo(13.5f).Within(1e-4f),
                        "the upgrade multiplies after the floor, so a maxed kitchen outgrows the hardest customers");
        }
    }
}

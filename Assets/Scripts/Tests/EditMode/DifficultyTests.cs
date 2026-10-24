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
    }
}

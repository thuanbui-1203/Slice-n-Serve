using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Data;
using SliceNServe.Orders;

namespace SliceNServe.Tests
{
    public sealed class SpawnTableTests
    {
        [TearDown] public void TearDown() => TestData.Cleanup();

        [Test]
        public void WeightedDraw_MatchesWeights_AndNeverDrawsZeroWeight()
        {
            IngredientDefinition a = TestData.Ingredient("a", 1f);
            IngredientDefinition b = TestData.Ingredient("b", 3f);
            IngredientDefinition never = TestData.Ingredient("never", 0f);
            var table = new[] { a, never, b };

            var rng = new System.Random(1234);
            int countA = 0, countB = 0, countNever = 0;
            const int draws = 20000;
            for (int i = 0; i < draws; i++)
            {
                IngredientDefinition pick = IngredientSpawner.PickWeighted(table, (float)rng.NextDouble());
                if (pick == a) countA++;
                else if (pick == b) countB++;
                else if (pick == never) countNever++;
            }

            Assert.That(countNever, Is.EqualTo(0));
            Assert.That(countA / (float)draws, Is.EqualTo(0.25f).Within(0.02f));
            Assert.That(countB / (float)draws, Is.EqualTo(0.75f).Within(0.02f));
        }

        [Test]
        public void WeightedDraw_BoundaryRolls_NeverPickZeroWeight()
        {
            IngredientDefinition never = TestData.Ingredient("never", 0f);
            IngredientDefinition a = TestData.Ingredient("a", 1f);
            var table = new[] { never, a, TestData.Ingredient("never2", 0f) };

            Assert.That(IngredientSpawner.PickWeighted(table, 0f), Is.EqualTo(a));
            Assert.That(IngredientSpawner.PickWeighted(table, 0.99999f), Is.EqualTo(a));
        }

        [Test]
        public void FairnessBias_PrefersDishesWhoseIngredientsAreOnTheBoard()
        {
            var tuning = TestData.Tuning();
            DishDefinition dish = TestData.Dish("d", 10, (TestData.Ingredient("x"), 1), (TestData.Ingredient("y"), 1));

            float available = OrderSpawner.DishWeight(dish, 0, 999f, tuning);
            float missingOne = OrderSpawner.DishWeight(dish, 1, 999f, tuning);
            float missingTwo = OrderSpawner.DishWeight(dish, 2, 999f, tuning);

            Assert.That(missingOne, Is.LessThan(available));
            Assert.That(missingTwo, Is.LessThan(missingOne));
            Assert.That(missingTwo, Is.GreaterThan(0f), "a bias, never a hard rule");
        }

        [Test]
        public void ComplexDishes_AreDampedOnlyEarly()
        {
            var tuning = TestData.Tuning();
            IngredientDefinition x = TestData.Ingredient("x");
            DishDefinition complex = TestData.Dish("c", 10, (x, 3));

            Assert.That(OrderSpawner.DishWeight(complex, 0, 10f, tuning), Is.EqualTo(tuning.Config.earlyComplexDishWeight));
            Assert.That(OrderSpawner.DishWeight(complex, 0, tuning.Config.earlyGameSeconds + 1f, tuning), Is.EqualTo(1f));
        }
    }
}

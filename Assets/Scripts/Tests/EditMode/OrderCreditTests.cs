using System.Collections.Generic;
using NUnit.Framework;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using UnityEngine;

namespace SliceNServe.Tests
{
    public sealed class OrderCreditTests
    {
        private EventBus _events;
        private RuntimeTuning _tuning;
        private LocalOrderQueue _queue;
        private IngredientDefinition _tomato, _cheese, _egg;
        private CustomerDefinition _customer;
        private readonly List<OrderServed> _served = new List<OrderServed>();
        private readonly List<IngredientWasted> _wasted = new List<IngredientWasted>();
        private int _nextId;

        [SetUp]
        public void SetUp()
        {
            _events = new EventBus();
            _tuning = TestData.Tuning();
            _queue = new LocalOrderQueue(_events, _tuning);
            _tomato = TestData.Ingredient("tomato");
            _cheese = TestData.Ingredient("cheese");
            _egg = TestData.Ingredient("egg");
            _customer = TestData.Customer("c");
            _served.Clear();
            _wasted.Clear();
            _nextId = 1;
            _events.Subscribe<OrderServed>(_served.Add);
            _events.Subscribe<IngredientWasted>(_wasted.Add);
        }

        [TearDown] public void TearDown() => TestData.Cleanup();

        private OrderInstance Order(float spawnTime, params (IngredientDefinition, int)[] recipe)
        {
            OrderInstance o = OrderInstance.Create(_nextId++, TestData.Dish("d", 10, recipe), _customer, spawnTime, 25f);
            _queue.Add(o);
            return o;
        }

        [Test]
        public void Credit_GoesToTheOldestDemandingOrder()
        {
            OrderInstance newer = Order(5f, (_tomato, 2));
            OrderInstance older = Order(1f, (_tomato, 2));

            _queue.SubmitIngredient(_tomato, Vector2.zero);

            Assert.That(older.Lines[0].Fulfilled, Is.EqualTo(1));
            Assert.That(newer.Lines[0].Fulfilled, Is.EqualTo(0));
        }

        [Test]
        public void Credit_SkipsOlderOrdersThatDoNotNeedIt()
        {
            OrderInstance older = Order(1f, (_cheese, 1));
            OrderInstance newer = Order(2f, (_tomato, 1), (_egg, 1));

            _queue.SubmitIngredient(_tomato, Vector2.zero);

            Assert.That(newer.Lines[0].Fulfilled, Is.EqualTo(1));
            Assert.That(older.Lines[0].Fulfilled, Is.EqualTo(0));
        }

        [Test]
        public void Credit_TieBreaksOnFewestRemainingUnits()
        {
            OrderInstance big = Order(3f, (_tomato, 1), (_cheese, 3));
            OrderInstance small = Order(3f, (_tomato, 1), (_cheese, 1));

            _queue.SubmitIngredient(_tomato, Vector2.zero);

            Assert.That(small.Lines[0].Fulfilled, Is.EqualTo(1));
            Assert.That(big.Lines[0].Fulfilled, Is.EqualTo(0));
        }

        [Test]
        public void MultiUnitRecipe_NeedsEveryUnit()
        {
            OrderInstance o = Order(1f, (_tomato, 2), (_cheese, 1));

            _queue.SubmitIngredient(_tomato, Vector2.zero);
            _queue.SubmitIngredient(_cheese, Vector2.zero);
            Assert.That(_served, Is.Empty);
            Assert.That(o.IsComplete, Is.False);

            _queue.SubmitIngredient(_tomato, Vector2.zero);
            Assert.That(_served.Count, Is.EqualTo(1));
            Assert.That(o.State, Is.EqualTo(PatienceState.Served));
        }

        [Test]
        public void NoMatch_PublishesExactlyOneWaste()
        {
            Order(1f, (_cheese, 1));
            var at = new Vector2(2f, 3f);

            _queue.SubmitIngredient(_tomato, at);

            Assert.That(_wasted.Count, Is.EqualTo(1));
            Assert.That(_wasted[0].IngredientId, Is.EqualTo("tomato"));
            Assert.That(_wasted[0].Position, Is.EqualTo(at));
        }

        [Test]
        public void ServedOrder_FreesItsSlot_AndIsNeverCreditedAgain()
        {
            OrderInstance first = Order(1f, (_tomato, 1));
            OrderInstance second = Order(2f, (_tomato, 1));

            _queue.SubmitIngredient(_tomato, Vector2.zero);
            Assert.That(_queue.ActiveCount, Is.EqualTo(1));
            Assert.That(_queue.Find(first.OrderId), Is.Null);

            _queue.SubmitIngredient(_tomato, Vector2.zero);
            Assert.That(first.Lines[0].Fulfilled, Is.EqualTo(1), "a served order is never credited again");
            Assert.That(second.State, Is.EqualTo(PatienceState.Served));
            Assert.That(_queue.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Serving_PaysCashTimesTipTimesCashMultiplier()
        {
            _customer = TestData.Customer("tipper", tip: 1.5f);
            _tuning.CashMultiplier = 2f;
            Order(1f, (_tomato, 1));

            _queue.SubmitIngredient(_tomato, Vector2.zero);

            Assert.That(_served[0].Cash, Is.EqualTo(30));
        }
    }
}

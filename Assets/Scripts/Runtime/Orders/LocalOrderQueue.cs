using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Orders
{
    /// <summary>
    /// The auto-credit rule from plan §6.3. Deliberately a plain class over a list of orders: it
    /// is the highest-value thing in Section 2 to unit-test, and it must run with no scene at all.
    /// </summary>
    public sealed class LocalOrderQueue : IOrderQueue
    {
        private readonly List<OrderInstance> _orders;
        private readonly EventBus _events;
        private readonly RuntimeTuning _tuning;

        public LocalOrderQueue(EventBus events, RuntimeTuning tuning, int capacity = 8)
        {
            _events = events;
            _tuning = tuning;
            _orders = new List<OrderInstance>(capacity);
        }

        public int ActiveCount => _orders.Count;
        public OrderInstance this[int index] => _orders[index];

        public OrderInstance Find(int orderId)
        {
            for (int i = 0; i < _orders.Count; i++)
                if (_orders[i].OrderId == orderId) return _orders[i];

            return null;
        }

        public void Add(OrderInstance order)
        {
            _orders.Add(order);
            _events.Publish(new OrderCreated(order.OrderId, order.Dish.Id, order.Customer.Id));
        }

        public void SubmitIngredient(IngredientDefinition ingredient, Vector2 position)
        {
            OrderInstance target = FindTarget(ingredient);
            if (target == null)
            {
                // Discarded, with feedback (decision D11). A pantry is a Section 3 idea.
                _events.Publish(new IngredientWasted(ingredient.Id, position));
                return;
            }

            Credit(target, ingredient, position);
            if (target.IsComplete) Serve(target);
        }

        /// <summary>Oldest customer first; tie-break on fewest units remaining, so nearly-done
        /// dishes finish first.</summary>
        private OrderInstance FindTarget(IngredientDefinition ingredient)
        {
            OrderInstance best = null;

            for (int i = 0; i < _orders.Count; i++)
            {
                OrderInstance o = _orders[i];
                if (o.IsClosed || !o.Needs(ingredient)) continue;

                if (best == null
                    || o.SpawnTime < best.SpawnTime
                    || (Mathf.Approximately(o.SpawnTime, best.SpawnTime) && o.RemainingUnits < best.RemainingUnits))
                    best = o;
            }

            return best;
        }

        private void Credit(OrderInstance order, IngredientDefinition ingredient, Vector2 position)
        {
            for (int i = 0; i < order.Lines.Length; i++)
            {
                OrderLine line = order.Lines[i];
                if (line.Ingredient != ingredient || line.Satisfied) continue;

                line.Fulfilled++;
                _events.Publish(new OrderProgressed(order.OrderId, ingredient.Id, line.Fulfilled, line.Required, position));
                return;
            }
        }

        /// <summary>Driven by <see cref="RunClock"/>'s delta, so §4 can scale patience and §5 can
        /// pause a round without touching the physics step.</summary>
        public void Tick(float delta)
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                OrderInstance o = _orders[i];
                PatienceSystem.Tick(o, delta, _tuning);
                if (o.State != PatienceState.Expired) continue;

                _orders.RemoveAt(i);
                _events.Publish(new OrderExpired(o.OrderId, o.Customer.Id));
            }
        }

        private void Serve(OrderInstance order)
        {
            order.State = PatienceState.Served;
            _orders.Remove(order);

            int cash = Mathf.RoundToInt(order.Dish.BaseCash * order.Customer.TipMultiplier * _tuning.TotalCashMultiplier);
            _events.Publish(new OrderServed(order.OrderId, cash));
        }
    }
}

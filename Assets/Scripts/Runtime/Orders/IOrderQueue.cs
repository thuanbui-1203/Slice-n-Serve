using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Orders
{
    /// <summary>
    /// The order queue behind an interface so §5's shared 1v1 / 2v2 queue can be a second
    /// implementation (plan §3.3). Section 2 ships <see cref="LocalOrderQueue"/>.
    /// </summary>
    public interface IOrderQueue
    {
        int ActiveCount { get; }
        OrderInstance this[int index] { get; }

        OrderInstance Find(int orderId);
        void Add(OrderInstance order);

        /// <summary>Credits one gathered unit to the right order, or discards it as waste. A
        /// completed order is only <i>ready</i>: it waits to be served by <see cref="TryServe"/>.</summary>
        void SubmitIngredient(IngredientDefinition ingredient, Vector2 position);

        /// <summary>The player hands over a ready order and is paid. False if the order is not
        /// complete, or already gone.</summary>
        bool TryServe(int orderId);

        /// <summary>The oldest order still cooking (open and not yet complete): the one a rush feeds.</summary>
        OrderInstance Front();

        void Tick(float delta);

        /// <summary>Rush hour: credit only the front customer; anything they don't need is wasted.</summary>
        bool FrontOnly { get; set; }
    }
}

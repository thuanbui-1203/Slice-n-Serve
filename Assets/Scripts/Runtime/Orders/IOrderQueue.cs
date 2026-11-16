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

        /// <summary>Credits one gathered unit to the right order, or discards it as waste.</summary>
        void SubmitIngredient(IngredientDefinition ingredient, Vector2 position);

        void Tick(float delta);

        /// <summary>Rush hour: credit only the front customer; anything they don't need is wasted.</summary>
        bool FrontOnly { get; set; }
    }
}

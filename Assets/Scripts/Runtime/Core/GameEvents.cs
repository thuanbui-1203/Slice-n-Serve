using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Core
{
    public enum LaunchSource { Manual = 0, Helper = 1, Split = 2 }
    public enum GameOverReason { ReputationDepleted = 0, Quit = 1 }

    public readonly struct KnifeLaunched
    {
        public readonly int KnifeId;
        public readonly LaunchSource Source;
        public KnifeLaunched(int knifeId, LaunchSource source) { KnifeId = knifeId; Source = source; }
    }

    /// <summary>The §5 sabotage trigger is "specific bounce combos", so every bounce is published.</summary>
    public readonly struct KnifeBounced
    {
        public readonly int KnifeId;
        public readonly int BounceIndex;
        public readonly Vector2 Position;
        public KnifeBounced(int knifeId, int bounceIndex, Vector2 position)
        { KnifeId = knifeId; BounceIndex = bounceIndex; Position = position; }
    }

    public readonly struct KnifeReturned
    {
        public readonly int KnifeId;
        public readonly int SliceCount;
        public KnifeReturned(int knifeId, int sliceCount) { KnifeId = knifeId; SliceCount = sliceCount; }
    }

    /// <summary>
    /// Fired once per sliced ingredient — the single source of truth for collection. Carries the
    /// definition rather than only its id so the order queue can credit it without a lookup.
    /// </summary>
    public readonly struct IngredientSliced
    {
        public readonly IngredientDefinition Ingredient;
        public readonly Vector2 Position;
        public readonly int KnifeId;
        public readonly int BounceIndex;
        public readonly int Combo;
        public IngredientSliced(IngredientDefinition ingredient, Vector2 position, int knifeId, int bounceIndex, int combo)
        { Ingredient = ingredient; Position = position; KnifeId = knifeId; BounceIndex = bounceIndex; Combo = combo; }
    }

    public readonly struct IngredientWasted
    {
        public readonly string IngredientId;
        public readonly Vector2 Position;
        public IngredientWasted(string ingredientId, Vector2 position)
        { IngredientId = ingredientId; Position = position; }
    }

    public readonly struct OrderCreated
    {
        public readonly int OrderId;
        public readonly string DishId;
        public readonly string CustomerId;
        public OrderCreated(int orderId, string dishId, string customerId)
        { OrderId = orderId; DishId = dishId; CustomerId = customerId; }
    }

    public readonly struct OrderProgressed
    {
        public readonly int OrderId;
        public readonly string IngredientId;
        public readonly int Fulfilled;
        public readonly int Required;
        public readonly Vector2 Position;
        public OrderProgressed(int orderId, string ingredientId, int fulfilled, int required, Vector2 position)
        { OrderId = orderId; IngredientId = ingredientId; Fulfilled = fulfilled; Required = required; Position = position; }
    }

    public readonly struct OrderServed
    {
        public readonly int OrderId;
        public readonly int Cash;
        public OrderServed(int orderId, int cash) { OrderId = orderId; Cash = cash; }
    }

    public readonly struct OrderExpired
    {
        public readonly int OrderId;
        public readonly string CustomerId;
        public OrderExpired(int orderId, string customerId) { OrderId = orderId; CustomerId = customerId; }
    }

    public readonly struct ReputationChanged
    {
        public readonly int Reputation;
        public readonly int Delta;
        public ReputationChanged(int reputation, int delta) { Reputation = reputation; Delta = delta; }
    }

    public readonly struct GameOver
    {
        public readonly GameOverReason Reason;
        public GameOver(GameOverReason reason) { Reason = reason; }
    }
}

using SliceNServe.Data;

namespace SliceNServe.Orders
{
    public enum PatienceState { Normal = 0, Hurry = 1, Critical = 2, Expired = 3, Served = 4 }

    /// <summary>The runtime mirror of an <see cref="IngredientRequirement"/>, with a mutable counter.</summary>
    public sealed class OrderLine
    {
        public IngredientDefinition Ingredient;
        public int Required;
        public int Fulfilled;

        public bool Satisfied => Fulfilled >= Required;
    }

    /// <summary>Plain C#, not a MonoBehaviour — which is what makes §2.2 testable without a scene.</summary>
    public sealed class OrderInstance
    {
        public int OrderId;
        public DishDefinition Dish;
        public CustomerDefinition Customer;
        public OrderLine[] Lines;
        public float SpawnTime;
        public float PatienceDuration;
        public float RemainingPatience;
        public PatienceState State = PatienceState.Normal;

        /// <summary>Multiplies the dish's cash: demand growth and rush hour both raise it. Fixed at spawn.</summary>
        public float RewardScale = 1f;

        /// <summary>Placed during a rush hour: pays the rush bonus, for the HUD to mark.</summary>
        public bool RushOrder;

        public bool IsClosed => State == PatienceState.Served || State == PatienceState.Expired;

        public float PatienceFraction => PatienceDuration <= 0f ? 0f : RemainingPatience / PatienceDuration;

        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < Lines.Length; i++)
                    if (!Lines[i].Satisfied) return false;

                return true;
            }
        }

        public bool Needs(IngredientDefinition ingredient)
        {
            for (int i = 0; i < Lines.Length; i++)
                if (Lines[i].Ingredient == ingredient && !Lines[i].Satisfied) return true;

            return false;
        }

        /// <summary>Used as the tie-break when two orders are equally old.</summary>
        public int RemainingUnits
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Lines.Length; i++) n += Lines[i].Required - Lines[i].Fulfilled;
                return n;
            }
        }

        /// <summary>Builds an order from a recipe. Allocates once per order, never per frame.</summary>
        /// <param name="requiredCount">Maps a recipe count to this order's count — demand growth.
        /// Null orders the recipe as written.</param>
        public static OrderInstance Create(int orderId, DishDefinition dish, CustomerDefinition customer,
                                           float spawnTime, float patienceDuration, System.Func<int, int> requiredCount = null)
        {
            var lines = new OrderLine[dish.Ingredients.Count];
            for (int i = 0; i < lines.Length; i++)
            {
                IngredientRequirement r = dish.Ingredients[i];
                int required = requiredCount != null ? System.Math.Max(r.count, requiredCount(r.count)) : r.count;
                lines[i] = new OrderLine { Ingredient = r.ingredient, Required = required, Fulfilled = 0 };
            }

            return new OrderInstance
            {
                OrderId = orderId,
                Dish = dish,
                Customer = customer,
                Lines = lines,
                SpawnTime = spawnTime,
                PatienceDuration = patienceDuration,
                RemainingPatience = patienceDuration
            };
        }
    }
}

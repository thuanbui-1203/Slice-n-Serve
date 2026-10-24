using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Orders
{
    /// <summary>
    /// Generates customers and their orders on a timer, and ticks patience. Runs only while
    /// <see cref="Running"/> is set, so the run owner decides when a service starts and stops.
    /// </summary>
    public sealed class OrderSpawner : MonoBehaviour
    {
        /// <summary>A dish needing at least this many units counts as "multi-ingredient" early on.</summary>
        public const int ComplexDishUnits = 3;

        [SerializeField] private DishDefinition[] dishes;
        [SerializeField] private CustomerDefinition[] customers;

        private IOrderQueue _queue;
        private RunClock _clock;
        private RuntimeTuning _tuning;
        private IDifficultyProvider _difficulty;
        private IngredientSpawner _board;

        private float[] _weights;
        private float _untilNextSpawn;
        private int _nextOrderId = 1;

        public bool Running { get; set; }

        public void Initialize(IOrderQueue queue, RunClock clock, RuntimeTuning tuning,
                               IDifficultyProvider difficulty, IngredientSpawner board)
        {
            _queue = queue;
            _clock = clock;
            _tuning = tuning;
            _difficulty = difficulty;
            _board = board;

            _weights = new float[dishes.Length];   // sized once: no per-spawn allocation
            _untilNextSpawn = 0f;                  // the first customer arrives immediately
        }

        private void Update()
        {
            if (_queue == null || !Running) return;

            float dt = _clock.DeltaTime;
            _queue.Tick(dt);

            if (_queue.ActiveCount >= _tuning.MaxActiveOrders) return;

            _untilNextSpawn -= dt;
            if (_untilNextSpawn > 0f) return;

            // Resolved per spawn, so §4 can change difficulty mid-run without touching live orders.
            _untilNextSpawn = _tuning.OrderSpawnInterval * _difficulty.OrderSpawnIntervalScale;
            Spawn();
        }

        private void Spawn()
        {
            if (dishes.Length == 0 || customers.Length == 0) return;

            CustomerDefinition customer = PickCustomer();
            DishDefinition dish = PickDish();
            float duration = PatienceSystem.ResolveDuration(_tuning, customer, _difficulty);

            _queue.Add(OrderInstance.Create(_nextOrderId++, dish, customer, _clock.Elapsed, duration));
        }

        private CustomerDefinition PickCustomer()
        {
            float total = 0f;
            for (int i = 0; i < customers.Length; i++) total += customers[i].SpawnWeight;

            float r = Random.value * total;
            for (int i = 0; i < customers.Length; i++)
            {
                r -= customers[i].SpawnWeight;
                if (r <= 0f) return customers[i];
            }
            return customers[customers.Length - 1];
        }

        private DishDefinition PickDish()
        {
            float total = 0f;
            for (int i = 0; i < dishes.Length; i++)
            {
                _weights[i] = DishWeight(dishes[i], MissingIngredients(dishes[i]), _clock.Elapsed, _tuning);
                total += _weights[i];
            }
            if (total <= 0f) return dishes[0];

            float r = Random.value * total;
            for (int i = 0; i < dishes.Length; i++)
            {
                r -= _weights[i];
                if (r <= 0f) return dishes[i];
            }
            return dishes[dishes.Length - 1];
        }

        private int MissingIngredients(DishDefinition dish)
        {
            int missing = 0;
            for (int i = 0; i < dish.Ingredients.Count; i++)
                if (_board.CountLive(dish.Ingredients[i].ingredient) == 0) missing++;

            return missing;
        }

        /// <summary>
        /// baseOrderWeight / (1 + missingIngredientPenalty), so the run is not lost to RNG — a
        /// small, tunable bias, never a hard rule (plan §6.2) — and damped for complex dishes in
        /// the opening minutes. Pure, so the bias is testable.
        /// </summary>
        public static float DishWeight(DishDefinition dish, int missingIngredients, float runTime, RuntimeTuning tuning)
        {
            float weight = dish.BaseOrderWeight / (1f + missingIngredients * tuning.Config.missingIngredientPenalty);

            int units = 0;
            for (int i = 0; i < dish.Ingredients.Count; i++) units += dish.Ingredients[i].count;

            if (units >= ComplexDishUnits && runTime < tuning.Config.earlyGameSeconds)
                weight *= tuning.Config.earlyComplexDishWeight;

            return weight;
        }
    }
}

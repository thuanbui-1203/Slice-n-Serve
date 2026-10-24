using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Owns the ingredient pool and acts as the registry the knife's sweep resolves against.
    /// Reads <see cref="RuntimeTuning"/> every tick and never caches it, so Section 3's
    /// spawn-rate upgrade is an assignment rather than a code change (plan §3.1).
    /// </summary>
    public sealed class IngredientSpawner : MonoBehaviour
    {
        private const int InitialCapacity = 32;
        private const int PlacementAttempts = 8;

        [SerializeField] private Ingredient ingredientPrefab;
        [SerializeField] private IngredientDefinition[] definitions;
        [SerializeField] private Transform poolRoot;

        private readonly Dictionary<int, Ingredient> _byCollider = new Dictionary<int, Ingredient>(InitialCapacity);
        private readonly List<Ingredient> _live = new List<Ingredient>(InitialCapacity);
        private readonly List<Ingredient> _pool = new List<Ingredient>(InitialCapacity);

        private RuntimeTuning _tuning;
        private EventBus _events;
        private float _nextSpawnTime;

        public IReadOnlyList<IngredientDefinition> Definitions => definitions;
        public int LiveCount => _live.Count;

        public void Initialize(RuntimeTuning tuning, EventBus events)
        {
            _tuning = tuning;
            _events = events;
            _nextSpawnTime = Time.time;

            // Warm the pool to the working max so a run never instantiates.
            for (int i = 0; i < tuning.WorkingMaxIngredientCount; i++)
            {
                Ingredient created = Instantiate(ingredientPrefab, poolRoot);
                created.gameObject.SetActive(false);
                _pool.Add(created);
            }
        }

        /// <summary>How many of this kind are on the board. Used by the order spawner to bias
        /// dishes towards ingredients that actually exist (plan §6.2).</summary>
        public int CountLive(IngredientDefinition definition)
        {
            int n = 0;
            for (int i = 0; i < _live.Count; i++)
                if (_live[i].Definition == definition) n++;
            return n;
        }

        private void Update()
        {
            if (_tuning == null) return;

            Expire();

            if (_live.Count >= _tuning.TargetIngredientCount) return;
            if (Time.time < _nextSpawnTime) return;

            _nextSpawnTime = Time.time + _tuning.IngredientRespawnInterval;
            int budget = Mathf.Min(_tuning.MaxSpawnsPerTick, _tuning.TargetIngredientCount - _live.Count);
            for (int i = 0; i < budget; i++) TrySpawnOne(PickDefinition());
        }

        /// <summary>Fills the board to its target at once, for the start of a run.</summary>
        public void FillImmediately()
        {
            int guard = _tuning.TargetIngredientCount * PlacementAttempts;
            while (_live.Count < _tuning.TargetIngredientCount && guard-- > 0) TrySpawnOne(PickDefinition());
        }

        /// <summary>
        /// The external "spawn this here" request §5 sabotage needs (plan §3.3). Bypasses the
        /// spawn table and the separation rule, but not the working maximum.
        /// </summary>
        public Ingredient ForceSpawn(IngredientDefinition definition, Vector2 position)
        {
            if (definition == null || _live.Count >= _tuning.WorkingMaxIngredientCount) return null;
            return Place(definition, position);
        }

        private void TrySpawnOne(IngredientDefinition def)
        {
            if (def == null || _live.Count >= _tuning.WorkingMaxIngredientCount) return;

            Rect zone = _tuning.IngredientZone;
            float r = def.Radius;

            for (int attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                var p = new Vector2(Random.Range(zone.xMin + r, zone.xMax - r), Random.Range(zone.yMin + r, zone.yMax - r));
                if (!IsClear(p, r)) continue;

                Place(def, p);
                return;   // one success per call; failing every attempt defers to the next tick
            }
        }

        /// <summary>
        /// Separation is checked against the live list, not Physics2D.OverlapCircle: a collider
        /// placed earlier this frame is not visible to physics queries until the next step, so
        /// two spawns in one tick could otherwise stack.
        /// </summary>
        private bool IsClear(Vector2 p, float radius)
        {
            float separation = _tuning.IngredientMinSeparation;
            for (int i = 0; i < _live.Count; i++)
            {
                Ingredient other = _live[i];
                float min = radius + other.Definition.Radius + separation;
                if ((other.Position - p).sqrMagnitude < min * min) return false;
            }
            return true;
        }

        private Ingredient Place(IngredientDefinition def, Vector2 p)
        {
            Ingredient ing = Rent();
            ing.Present(def, p, _tuning.IngredientLifetime);
            _byCollider[ing.ColliderId] = ing;
            _live.Add(ing);
            return ing;
        }

        private Ingredient Rent()
        {
            int last = _pool.Count - 1;
            if (last < 0) return Instantiate(ingredientPrefab, poolRoot);

            Ingredient pooled = _pool[last];
            _pool.RemoveAt(last);
            return pooled;
        }

        private void Expire()
        {
            float now = Time.time;
            for (int i = _live.Count - 1; i >= 0; i--)
                if (now >= _live[i].ExpiresAt) Remove(i);
        }

        public bool TryResolve(Collider2D collider, out Ingredient ingredient)
            => _byCollider.TryGetValue(collider.GetInstanceID(), out ingredient);

        /// <summary>
        /// The single entry point for collection. Keeping it here — rather than letting each
        /// caller unregister and recycle — is what stops the registry and the pool drifting apart.
        /// </summary>
        public void Slice(Ingredient ingredient, int knifeId, int bounceIndex, int combo)
        {
            int index = _live.IndexOf(ingredient);
            if (index < 0) return;

            IngredientDefinition def = ingredient.Definition;
            Vector2 position = ingredient.Position;
            Remove(index);

            _events.Publish(new IngredientSliced(def, position, knifeId, bounceIndex, combo));
        }

        /// <summary>Clears the board, for a run reset.</summary>
        public void Clear()
        {
            for (int i = _live.Count - 1; i >= 0; i--) Remove(i);
        }

        private void Remove(int index)
        {
            Ingredient ing = _live[index];
            _live.RemoveAt(index);
            _byCollider.Remove(ing.ColliderId);
            ing.gameObject.SetActive(false);
            _pool.Add(ing);
        }

        private IngredientDefinition PickDefinition() => PickWeighted(definitions, Random.value);

        /// <summary>Weighted draw over <see cref="IngredientDefinition.SpawnWeight"/>. Pure, so the
        /// distribution is testable with a seeded roll in [0, 1).</summary>
        public static IngredientDefinition PickWeighted(IReadOnlyList<IngredientDefinition> table, float roll01)
        {
            float total = 0f;
            for (int i = 0; i < table.Count; i++) total += table[i].SpawnWeight;
            if (total <= 0f) return null;

            float r = roll01 * total;
            for (int i = 0; i < table.Count; i++)
            {
                float w = table[i].SpawnWeight;
                if (w <= 0f) continue;   // never spawns naturally, even on a boundary roll
                r -= w;
                if (r <= 0f) return table[i];
            }

            for (int i = table.Count - 1; i >= 0; i--)
                if (table[i].SpawnWeight > 0f) return table[i];
            return null;
        }
    }
}

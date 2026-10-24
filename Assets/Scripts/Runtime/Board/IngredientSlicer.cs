using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Speed-proof ingredient collection: every physics step, sweep the knife's own collider along
    /// the segment it is about to travel and collect whatever it crosses.
    ///
    /// This exists because Continuous collision detection does <b>not</b> sweep triggers, so a fast
    /// knife can pass an ingredient between steps. Section 3's Knife Speed upgrade exists precisely
    /// to make the knife faster, so this cannot be left to chance.
    /// </summary>
    public sealed class IngredientSlicer : MonoBehaviour
    {
        private readonly RaycastHit2D[] _results = new RaycastHit2D[16];

        private Rigidbody2D _rb;
        private ContactFilter2D _filter;
        private IngredientSpawner _spawner;

        public void Initialize(Rigidbody2D rb, IngredientSpawner spawner)
        {
            _rb = rb;
            _spawner = spawner;
            _filter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true,
                layerMask = 1 << LayerMask.NameToLayer("Ingredient")
            };
        }

        /// <summary>Returns how many ingredients were collected this step.</summary>
        public int Sweep(int knifeId, int bounceIndex, int comboSoFar, float deltaTime)
        {
            Vector2 v = _rb.linearVelocity;
            if (v.sqrMagnitude < 1e-6f) return 0;   // a zero-length cast direction returns nothing

            int hits = _rb.Cast(v.normalized, _filter, _results, v.magnitude * deltaTime);

            int collected = 0;
            for (int i = 0; i < hits; i++)
            {
                // The spawner unregisters on collection, so a repeat hit for the same collider
                // within this step resolves false. That is the within-step dedupe.
                if (!_spawner.TryResolve(_results[i].collider, out Ingredient ing)) continue;

                collected++;
                _spawner.Slice(ing, knifeId, bounceIndex, comboSoFar + collected);
            }
            return collected;
        }
    }
}

using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// A pickup. Non-solid by design: the collision matrix keeps it out of every physical pair,
    /// so it can never nudge the knife, and collection happens through the knife's swept cast.
    ///
    /// The collider never moves — only the visual child animates. A static collider that moves
    /// forces broadphase rebuilds every frame, and a Rigidbody2D per ingredient would buy nothing
    /// when nothing can push anything.
    /// </summary>
    public sealed class Ingredient : MonoBehaviour
    {
        private const float PopInSeconds = 0.25f;
        private const float FadeOutSeconds = 1f;

        [SerializeField] private CircleCollider2D body;
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private SpriteRenderer shadow;

        [Tooltip("Visual diameter relative to the collider's.")]
        [SerializeField] private float visualScale = 1.25f;

        public IngredientDefinition Definition { get; private set; }
        public int ColliderId { get; private set; }
        public float SpawnedAt { get; private set; }
        public float ExpiresAt { get; private set; }
        public Vector2 Position => transform.position;

        private float _baseScale = 1f;
        private float _phase;

        private void Awake() => ColliderId = body.GetInstanceID();

        public void Present(IngredientDefinition definition, Vector2 position, float lifetime)
        {
            Definition = definition;
            SpawnedAt = Time.time;
            ExpiresAt = SpawnedAt + lifetime;
            _phase = Random.value * 10f;

            transform.position = position;
            body.radius = definition.Radius;
            visual.sprite = definition.Sprite;
            visual.color = definition.Tint;

            float native = definition.Sprite != null ? Mathf.Max(definition.Sprite.bounds.size.x, definition.Sprite.bounds.size.y) : 1f;
            _baseScale = definition.Radius * 2f * visualScale / Mathf.Max(0.01f, native);
            if (shadow != null)
            {
                float shadowNative = shadow.sprite != null ? shadow.sprite.bounds.size.x : 1f;
                shadow.transform.localScale = new Vector3(1.4f, 0.5f, 1f) * (definition.Radius * 2f / shadowNative);
            }

            gameObject.SetActive(true);
            Animate();
        }

        private void Update() => Animate();

        private void Animate()
        {
            float now = Time.time;
            float pop = Mathf.Clamp01((now - SpawnedAt) / PopInSeconds);
            float overshoot = 1f + Mathf.Sin(pop * Mathf.PI) * 0.25f;
            float fade = Mathf.Clamp01((ExpiresAt - now) / FadeOutSeconds);

            Transform v = visual.transform;
            v.localPosition = new Vector3(0f, 0.05f + Mathf.Sin(now * 3f + _phase) * 0.05f, 0f);
            v.localScale = Vector3.one * (_baseScale * pop * overshoot);
            v.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(now * 2f + _phase) * 6f);

            Color c = Definition != null ? Definition.Tint : Color.white;
            c.a *= fade;
            visual.color = c;
            if (shadow != null) shadow.color = new Color(0f, 0f, 0f, 0.22f * fade * pop);
        }
    }
}

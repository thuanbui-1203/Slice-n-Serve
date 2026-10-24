using UnityEngine;

namespace SliceNServe.Data
{
    [CreateAssetMenu(menuName = "Slice & Serve/Ingredient", fileName = "Ingredient")]
    public sealed class IngredientDefinition : ScriptableObject
    {
        [Tooltip("Stable key used by recipes and save data. Never the asset name.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;
        [SerializeField] private Sprite sprite;
        [SerializeField] private Color tint = Color.white;

        [Tooltip("Colour of the slice burst. Separate from tint so real art can stay untinted.")]
        [SerializeField] private Color juiceColor = Color.white;

        [Tooltip("Collider radius, and the basis for spawn separation.")]
        [SerializeField, Min(0.05f)] private float radius = 0.35f;

        [Tooltip("Weight in the spawn table. 0 means it never spawns naturally.")]
        [SerializeField, Min(0f)] private float spawnWeight = 1f;

        [Tooltip("Reserved: false knocks the knife aside (obstacle behaviour, §6 roadmap). "
               + "The code path is stubbed and unused in Section 2.")]
        [SerializeField] private bool slicesOnHit = true;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public Color Tint => tint;
        public Color JuiceColor => juiceColor;
        public float Radius => radius;
        public float SpawnWeight => spawnWeight;
        public bool SlicesOnHit => slicesOnHit;
    }
}

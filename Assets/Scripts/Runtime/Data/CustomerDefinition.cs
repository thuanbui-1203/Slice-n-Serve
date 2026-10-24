using UnityEngine;

namespace SliceNServe.Data
{
    /// <summary>A customer archetype, and the extension point for §6's VIP / "boss" customers.</summary>
    [CreateAssetMenu(menuName = "Slice & Serve/Customer", fileName = "Customer")]
    public sealed class CustomerDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Tooltip("One portrait per mood, in PatienceState order: Normal, Hurry, Critical, then Angry.")]
        [SerializeField] private Sprite[] spriteSet;

        [SerializeField, Range(0.1f, 5f)] private float patienceMultiplier = 1f;
        [SerializeField, Min(0f)] private float tipMultiplier = 1f;
        [SerializeField, Min(1)] private int orderCount = 1;
        [SerializeField, Min(0f)] private float spawnWeight = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite[] SpriteSet => spriteSet;
        public float PatienceMultiplier => patienceMultiplier;
        public float TipMultiplier => tipMultiplier;
        public int OrderCount => orderCount;
        public float SpawnWeight => spawnWeight;
    }
}

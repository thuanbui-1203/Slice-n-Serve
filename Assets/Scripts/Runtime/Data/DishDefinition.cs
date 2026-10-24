using System.Collections.Generic;
using UnityEngine;

namespace SliceNServe.Data
{
    [System.Serializable]
    public struct IngredientRequirement
    {
        public IngredientDefinition ingredient;
        [Min(1)] public int count;
    }

    [CreateAssetMenu(menuName = "Slice & Serve/Dish", fileName = "Dish")]
    public sealed class DishDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite sprite;
        [SerializeField] private List<IngredientRequirement> ingredients = new List<IngredientRequirement>();
        [SerializeField, Min(0)] private int baseCash = 10;
        [SerializeField, Min(0f)] private float baseOrderWeight = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public IReadOnlyList<IngredientRequirement> Ingredients => ingredients;
        public int BaseCash => baseCash;
        public float BaseOrderWeight => baseOrderWeight;
    }
}

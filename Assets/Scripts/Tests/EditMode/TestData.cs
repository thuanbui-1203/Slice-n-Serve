using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using UnityEditor;
using UnityEngine;

namespace SliceNServe.Tests
{
    /// <summary>Builds definitions in memory. Their fields are private and serialized, so they are
    /// written the way the Inspector writes them.</summary>
    internal static class TestData
    {
        private static readonly List<Object> Created = new List<Object>();

        public static RuntimeTuning Tuning(System.Action<BoardConfig> edit = null)
        {
            var config = Track(ScriptableObject.CreateInstance<BoardConfig>());
            edit?.Invoke(config);
            return new RuntimeTuning(config);
        }

        public static IngredientDefinition Ingredient(string id, float weight = 1f)
        {
            var def = Track(ScriptableObject.CreateInstance<IngredientDefinition>());
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = id;
            so.FindProperty("spawnWeight").floatValue = weight;
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        public static DishDefinition Dish(string id, int cash, params (IngredientDefinition ingredient, int count)[] recipe)
        {
            var def = Track(ScriptableObject.CreateInstance<DishDefinition>());
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("baseCash").intValue = cash;
            SerializedProperty list = so.FindProperty("ingredients");
            list.arraySize = recipe.Length;
            for (int i = 0; i < recipe.Length; i++)
            {
                list.GetArrayElementAtIndex(i).FindPropertyRelative("ingredient").objectReferenceValue = recipe[i].ingredient;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("count").intValue = recipe[i].count;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        public static CustomerDefinition Customer(string id, float patience = 1f, float tip = 1f)
        {
            var def = Track(ScriptableObject.CreateInstance<CustomerDefinition>());
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("patienceMultiplier").floatValue = patience;
            so.FindProperty("tipMultiplier").floatValue = tip;
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        public static void Cleanup()
        {
            for (int i = 0; i < Created.Count; i++)
                if (Created[i] != null) Object.DestroyImmediate(Created[i]);
            Created.Clear();
        }

        private static T Track<T>(T obj) where T : Object
        {
            Created.Add(obj);
            return obj;
        }
    }
}

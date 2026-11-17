using System.IO;
using System.Linq;
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using SliceNServe.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SliceNServe.EditorTools
{
    /// <summary>
    /// Applies the §1 and §2 runbooks in code: project settings, layers, physics, import
    /// settings, data assets, prefabs and Game.unity. Idempotent — run it again after changing
    /// it and it rebuilds the generated content in place, keeping asset GUIDs.
    ///
    /// Menu: Slice &amp; Serve → Setup. Headless, step 1 must use -importPackage, because
    /// AssetDatabase.ImportPackage is deferred and -quit exits before it runs:
    ///   Unity -batchmode -quit -projectPath . -importPackage "&lt;ugui package&gt;/Package Resources/TMP Essential Resources.unitypackage"
    ///   Unity -batchmode -quit -projectPath . -executeMethod SliceNServe.EditorTools.ProjectSetup.BuildAll
    /// </summary>
    public static class ProjectSetup
    {
        private const string ArtPlaceholders = "Assets/Art/Placeholders";
        private const string ArtMart = "Assets/Art/Pixel_Mart";
        private const string ArtFpp = "Assets/Art/FoodPingPong";   // menu art, font and music from Food-Ping-Pong
        private const string FppFontPath = ArtFpp + "/Fonts/ScoreDozer SDF.asset";
        private const string FppUiSheet = ArtFpp + "/UI/UI.png";
        private const string FppBooksSheet = ArtFpp + "/UI/UI books.png";
        private const string SettingsDir = "Assets/Settings";
        private const string DataDir = "Assets/ScriptableObjects";
        private const string PrefabDir = "Assets/Prefabs";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";
        private const string TmpFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string UnlitSpriteMaterial = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        public const int KnifeLayer = 8;
        public const int IngredientLayer = 9;
        public const int BoardWallLayer = 10;
        public const int ObstacleLayer = 11;

        private static readonly Color Ink = new Color(0.20f, 0.13f, 0.12f);
        private static readonly Color Paper = new Color(1f, 0.97f, 0.89f);
        private static readonly Color Accent = new Color(0.93f, 0.45f, 0.18f);

        // ---------------------------------------------------------------- entry points

        [MenuItem("Slice & Serve/Setup/1. Import TMP Essential Resources")]
        public static void ImportTmpEssentials()
        {
            if (File.Exists(TmpFontPath)) { Debug.Log("[Setup] TMP Essential Resources already imported."); return; }

            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            string package = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh();
            Debug.Log("[Setup] Imported TMP Essential Resources.");
        }

        [MenuItem("Slice & Serve/Setup/2. Build Everything")]
        public static void BuildAll()
        {
            // Open the fresh scene first: opening a scene in Single mode unloads assets nothing
            // references yet, which would silently null every asset reference built before it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigureLayers();
            ConfigurePhysics();
            ConfigurePlayer();
            ConfigureTextures();
            ConfigureFppTextures();
            ConfigureRestaurantTextures();
            ConfigureCustomerTextures();
            ConfigureDiceAndChandelierTextures();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var knifeMat = PhysicsMaterial(SettingsDir + "/KnifeMaterial.physicsMaterial2D");
            var wallMat = PhysicsMaterial(SettingsDir + "/WallMaterial.physicsMaterial2D");

            BoardConfig config = BuildData(out IngredientDefinition[] ingredients, out DishDefinition[] dishes, out CustomerDefinition[] customers);

            KnifeController knifePrefab = BuildKnifePrefab(knifeMat);
            Ingredient ingredientPrefab = BuildIngredientPrefab();
            OrderTicketView ticketPrefab = BuildTicketPrefab();
            UpgradeDefinition[] upgrades = BuildUpgrades();
            ShopCard cardPrefab = BuildShopCardPrefab();

            BuildScene(scene, config, wallMat, ingredients, dishes, customers, knifePrefab, ingredientPrefab, ticketPrefab, upgrades, cardPrefab);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Build Everything finished.");
        }

        // ---------------------------------------------------------------- player builds

        [MenuItem("Slice & Serve/Build/Windows x64")]
        public static void BuildWindows() => BuildPlayer(BuildTarget.StandaloneWindows64, "Builds/Windows/SliceNServe.exe");

        [MenuItem("Slice & Serve/Build/Android (ARM64 APK)")]
        public static void BuildAndroid() => BuildPlayer(BuildTarget.Android, "Builds/Android/SliceNServe.apk");

        private static void BuildPlayer(BuildTarget target, string path)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.None
            });

            var summary = report.summary;
            Debug.Log($"[Setup] {target} build {summary.result}: {summary.totalErrors} errors, {summary.totalSize / (1024 * 1024)} MB → {path}");
            if (Application.isBatchMode && summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        // ---------------------------------------------------------------- project settings

        private static void ConfigureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            layers.GetArrayElementAtIndex(KnifeLayer).stringValue = "Knife";
            layers.GetArrayElementAtIndex(IngredientLayer).stringValue = "Ingredient";
            layers.GetArrayElementAtIndex(BoardWallLayer).stringValue = "BoardWall";
            layers.GetArrayElementAtIndex(ObstacleLayer).stringValue = "Obstacle";
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Plan §4.3 and §4.5: zero gravity, 200 u/s translation cap, and a collision
        /// matrix in which the game layers touch only Knife↔BoardWall and Knife↔Obstacle.</summary>
        private static void ConfigurePhysics()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset")[0]);
            settings.FindProperty("m_Gravity").vector2Value = Vector2.zero;
            settings.FindProperty("m_MaxTranslationSpeed").floatValue = 200f;

            SerializedProperty matrix = settings.FindProperty("m_LayerCollisionMatrix");
            int[] game = { KnifeLayer, IngredientLayer, BoardWallLayer, ObstacleLayer };
            for (int a = 0; a < 32; a++)
            {
                uint row = (uint)matrix.GetArrayElementAtIndex(a).longValue;
                for (int b = 0; b < 32; b++)
                {
                    bool involvesGame = game.Contains(a) || game.Contains(b);
                    if (!involvesGame) continue;

                    bool on = (a == KnifeLayer && (b == BoardWallLayer || b == ObstacleLayer))
                           || (b == KnifeLayer && (a == BoardWallLayer || a == ObstacleLayer));
                    row = on ? row | (1u << b) : row & ~(1u << b);
                }
                matrix.GetArrayElementAtIndex(a).longValue = row;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>§1 runbook Step 1: landscape-only, 1920×1080 windowed-fullscreen PC, IL2CPP ARM64 Android.</summary>
        private static void ConfigurePlayer()
        {
            // Auto-rotation restricted to the two landscape orientations: a player holding the
            // phone either way up gets the right picture, and portrait can never render.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.runInBackground = true;

            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.gcIncremental = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
        }

        private static void ConfigureTextures()
        {
            string[] smooth = { "circle", "square", "panel" };
            foreach (string file in Directory.GetFiles(ArtPlaceholders, "*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var importer = (TextureImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = smooth.Contains(name) ? FilterMode.Bilinear : FilterMode.Point;
                importer.wrapMode = name == "floor_tile" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;

                var ts = new TextureImporterSettings();
                importer.ReadTextureSettings(ts);
                ts.spriteMeshType = name == "floor_tile" ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
                importer.SetTextureSettings(ts);

                importer.spriteBorder = name == "panel" ? new Vector4(16f, 16f, 16f, 16f) : Vector4.zero;
                importer.SaveAndReimport();
            }

            // Only the Pixel Mart sprites this slice uses: pixel art wants point filtering.
            foreach (string name in UsedMartSprites)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath($"{ArtMart}/{name}.png");
                if (importer == null) { Debug.LogWarning($"[Setup] Missing art {name}.png"); continue; }

                if (importer.filterMode == FilterMode.Point && importer.textureCompression == TextureImporterCompression.Uncompressed) continue;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        /// <summary>Food-Ping-Pong's pixel UI keeps its own slicing (copied .meta files); this only
        /// makes sure it renders crisp. The Dark UI icons are smooth art and stay bilinear.</summary>
        private static void ConfigureFppTextures()
        {
            foreach (string file in Directory.GetFiles(ArtFpp, "*.png", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer == null) continue;

                bool pixel = path.Contains("/UI/") || path.EndsWith("KN004.png");
                FilterMode filter = pixel ? FilterMode.Point : FilterMode.Bilinear;
                if (importer.textureType == TextureImporterType.Sprite && importer.filterMode == filter
                    && importer.textureCompression == TextureImporterCompression.Uncompressed && !importer.mipmapEnabled) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.filterMode = filter;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
        }

        private static readonly string[] UsedMartSprites =
        {
            "potato", "bell_pepper", "mushroom_white", "white_cheese_piece", "cabbage", "egg_white",
            "bowl", "teakettle", "frying pan", "chopping board",
            "kitchen_knife_set", "scissors", "basket_yellow", "coffee_bag", "spatula",
            // kitchen decor around the board
            "whisk", "rolling pin", "cooking_oil", "olive_oil", "salt", "sugar", "flour", "ketchup",
            "mustard", "milk_bottle", "egg_box", "butter", "meat1", "fish"
        };

        private static PhysicsMaterial2D PhysicsMaterial(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (mat == null)
            {
                mat = new PhysicsMaterial2D();
                AssetDatabase.CreateAsset(mat, path);
            }

            // Friction 0 and bounciness 1 on both sides: a clean mirror, so all energy loss is
            // explicit in KnifeController rather than emergent from material mixing (plan §4.5).
            mat.friction = 0f;
            mat.bounciness = 1f;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- data

        private static BoardConfig BuildData(out IngredientDefinition[] ingredients, out DishDefinition[] dishes, out CustomerDefinition[] customers)
        {
            EnsureFolder(DataDir + "/Ingredients");
            EnsureFolder(DataDir + "/Dishes");
            EnsureFolder(DataDir + "/Customers");

            var config = LoadOrCreate<BoardConfig>(DataDir + "/BoardConfig.asset");

            // The board layout is part of the scene design, not a balance knob: the kitchen decor
            // and HUD are laid out around it, so it is written here rather than left to the asset.
            var defaults = ScriptableObject.CreateInstance<BoardConfig>();
            config.viewBounds = defaults.viewBounds;
            config.worldBounds = defaults.worldBounds;
            config.launcherPosition = defaults.launcherPosition;
            config.ingredientZone = defaults.ingredientZone;
            config.bounceRetention = defaults.bounceRetention;   // a rule now: the die limits bounces, knives keep their speed
            Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(config);

            IngredientDefinition potato = Ingredient("potato", "Potato", "potato", new Color(0.85f, 0.68f, 0.40f), 1f);
            IngredientDefinition pepper = Ingredient("pepper", "Pepper", "bell_pepper", new Color(0.92f, 0.30f, 0.20f), 1f);
            IngredientDefinition mushroom = Ingredient("mushroom", "Mushroom", "mushroom_white", new Color(0.93f, 0.88f, 0.78f), 1f);
            IngredientDefinition cheese = Ingredient("cheese", "Cheese", "white_cheese_piece", new Color(1f, 0.85f, 0.35f), 0.6f);
            IngredientDefinition cabbage = Ingredient("cabbage", "Cabbage", "cabbage", new Color(0.50f, 0.86f, 0.35f), 1f);
            IngredientDefinition egg = Ingredient("egg", "Egg", "egg_white", new Color(1f, 0.93f, 0.55f), 1f);
            ingredients = new[] { potato, pepper, mushroom, cheese, cabbage, egg };

            dishes = new[]
            {
                Dish("salad", "Garden Salad", "bowl", 14, (cabbage, 2), (pepper, 1)),
                Dish("soup", "Mushroom Soup", "teakettle", 12, (mushroom, 1), (potato, 1)),
                Dish("omelette", "Cheese Omelette", "frying pan", 16, (egg, 1), (cheese, 1)),
                Dish("loaded_potato", "Loaded Potato", "chopping board", 24, (potato, 1), (cheese, 1), (pepper, 1)),
            };

            customers = new[]
            {
                Customer("patient", "Patient", 1.4f),
                Customer("regular", "Regular", 1.0f),
                Customer("hasty", "Hasty", 0.6f),
            };

            return config;
        }

        private static IngredientDefinition Ingredient(string id, string displayName, string art, Color juice, float weight)
        {
            var def = LoadOrCreate<IngredientDefinition>($"{DataDir}/Ingredients/Ingredient_{displayName.Replace(" ", "")}.asset");
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("sprite").objectReferenceValue = MartSprite(art);
            so.FindProperty("tint").colorValue = Color.white;
            so.FindProperty("juiceColor").colorValue = juice;
            so.FindProperty("radius").floatValue = 0.35f;
            so.FindProperty("spawnWeight").floatValue = weight;
            so.FindProperty("slicesOnHit").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        private static DishDefinition Dish(string id, string displayName, string art, int cash, params (IngredientDefinition ingredient, int count)[] recipe)
        {
            var def = LoadOrCreate<DishDefinition>($"{DataDir}/Dishes/Dish_{displayName.Replace(" ", "")}.asset");
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("sprite").objectReferenceValue = MartSprite(art);
            so.FindProperty("baseCash").intValue = cash;
            so.FindProperty("baseOrderWeight").floatValue = 1f;

            SerializedProperty list = so.FindProperty("ingredients");
            list.arraySize = recipe.Length;
            for (int i = 0; i < recipe.Length; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("ingredient").objectReferenceValue = recipe[i].ingredient;
                e.FindPropertyRelative("count").intValue = recipe[i].count;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        private static CustomerDefinition Customer(string id, string displayName, float patience)
        {
            var def = LoadOrCreate<CustomerDefinition>($"{DataDir}/Customers/Customer_{displayName}.asset");
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("patienceMultiplier").floatValue = patience;
            so.FindProperty("tipMultiplier").floatValue = 1f;
            so.FindProperty("orderCount").intValue = 1;
            so.FindProperty("spawnWeight").floatValue = 1f;

            // The generated faces are named customer_<art>_<index>_<mood>.png.
            string art = id == "regular" ? "average" : id == "hasty" ? "impatient" : id;
            string[] faces = Directory.GetFiles(ArtPlaceholders, $"customer_{art}_*.png").OrderBy(f => f).ToArray();
            SerializedProperty set = so.FindProperty("spriteSet");
            set.arraySize = faces.Length;
            for (int i = 0; i < faces.Length; i++)
                set.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(faces[i].Replace('\\', '/'));
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        // ---------------------------------------------------------------- upgrades (§3)

        private static UpgradeDefinition[] BuildUpgrades()
        {
            EnsureFolder(DataDir + "/Upgrades");
            return new[]
            {
                Upgrade("knife_speed", "Sharper Knives", UpgradeKind.KnifeSpeed, "kitchen_knife_set", 5, 30, 1.6f, 0.10f, "+{0}% speed", 100f),
                Upgrade("split_chance", "Twin Blades", UpgradeKind.SplitChance, "scissors", 5, 60, 1.6f, 0.08f, "{0}% split", 100f),
                Upgrade("spawn_rate", "Fresh Deliveries", UpgradeKind.IngredientSpawnRate, "basket_yellow", 5, 40, 1.5f, 0.15f, "+{0}% restock", 100f),
                Upgrade("patience", "Free Coffee", UpgradeKind.CustomerPatience, "coffee_bag", 5, 40, 1.5f, 0.10f, "+{0}% patience", 100f),
                Upgrade("sous_chef", "Sous-Chef", UpgradeKind.SousChef, "spatula", 3, 120, 2.5f, 1f, "{0}× sous-chef", 1f),
            };
        }

        private static UpgradeDefinition Upgrade(string id, string displayName, UpgradeKind kind, string art, int maxLevel,
                                                 int baseCost, float growth, float perLevel, string format, float displayScale)
        {
            var def = LoadOrCreate<UpgradeDefinition>($"{DataDir}/Upgrades/Upgrade_{displayName.Replace(" ", "").Replace("-", "")}.asset");
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("icon").objectReferenceValue = MartSprite(art);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("maxLevel").intValue = maxLevel;
            so.FindProperty("baseCost").intValue = baseCost;
            so.FindProperty("costGrowth").floatValue = growth;
            so.FindProperty("effectPerLevel").floatValue = perLevel;
            so.FindProperty("effectFormat").stringValue = format;
            so.FindProperty("displayScale").floatValue = displayScale;
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        // ---------------------------------------------------------------- prefabs

        private static KnifeController BuildKnifePrefab(PhysicsMaterial2D knifeMat)
        {
            var root = new GameObject("Knife") { layer = KnifeLayer };

            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.linearDamping = 0f;
            body.angularDamping = 0.4f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // A circle, not a capsule, on purpose: a rotation-independent silhouette keeps the
            // rebound learnable however the sprite spins (plan §5.3).
            var circle = root.AddComponent<CircleCollider2D>();
            circle.radius = 0.18f;
            circle.sharedMaterial = knifeMat;

            var slicer = root.AddComponent<IngredientSlicer>();
            var knife = root.AddComponent<KnifeController>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * 3f;   // 32 px at 100 PPU → ~0.95 u long
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = Placeholder("knife");
            sr.sortingOrder = 20;

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(root.transform, false);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = 0.14f;
            trail.minVertexDistance = 0.05f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 0f));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.9f, 1f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) }
            };
            trail.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(UnlitSpriteMaterial);
            trail.sortingOrder = 15;

            var so = new SerializedObject(knife);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("slicer").objectReferenceValue = slicer;
            so.FindProperty("trail").objectReferenceValue = trail;
            so.FindProperty("visual").objectReferenceValue = sr;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, PrefabDir + "/Knife.prefab").GetComponent<KnifeController>();
        }

        private static Ingredient BuildIngredientPrefab()
        {
            var root = new GameObject("Ingredient") { layer = IngredientLayer };

            // Trigger, no Rigidbody2D: the knife's swept cast is the only thing that finds it.
            var circle = root.AddComponent<CircleCollider2D>();
            circle.isTrigger = true;
            circle.radius = 0.35f;
            var ingredient = root.AddComponent<Ingredient>();

            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(root.transform, false);
            shadowGo.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            var shadow = shadowGo.AddComponent<SpriteRenderer>();
            shadow.sprite = Placeholder("circle");
            shadow.color = new Color(0f, 0f, 0f, 0.22f);
            shadow.sortingOrder = 4;

            var visualGo = new GameObject("Visual");
            visualGo.transform.SetParent(root.transform, false);
            var visual = visualGo.AddComponent<SpriteRenderer>();
            visual.sortingOrder = 5;

            var so = new SerializedObject(ingredient);
            so.FindProperty("body").objectReferenceValue = circle;
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("shadow").objectReferenceValue = shadow;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, PrefabDir + "/Ingredient.prefab").GetComponent<Ingredient>();
        }

        private static OrderTicketView BuildTicketPrefab()
        {
            TMP_FontAsset font = Font();
            const float w = 300f, h = 196f;

            var root = new GameObject("OrderTicket", typeof(RectTransform));
            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(w, h);
            var layout = root.AddComponent<LayoutElement>();
            layout.preferredWidth = w;
            layout.preferredHeight = h;
            var group = root.AddComponent<CanvasGroup>();
            var view = root.AddComponent<OrderTicketView>();

            RectTransform body = Rect("Body", root.transform);
            Stretch(body);
            Image bg = body.gameObject.AddComponent<Image>();
            Panel(bg, Paper);

            // Header: portrait, dish name, dish icon.
            RectTransform portraitFrame = Rect("PortraitFrame", body);
            Place(portraitFrame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -10f), new Vector2(62f, 62f));
            Panel(portraitFrame.gameObject.AddComponent<Image>(), new Color(0.85f, 0.8f, 0.7f));
            Image portrait = Img("Portrait", portraitFrame, null, Color.white);
            Place(portrait.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 54f), new Vector2(0.5f, 0.5f));

            TextMeshProUGUI dishName = Text("DishName", body, font, 25f, Ink, TextAlignmentOptions.TopLeft, bold: true);
            Place(dishName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(84f, -12f), new Vector2(-150f, 60f));
            dishName.textWrappingMode = TextWrappingModes.Normal;

            Image dishIcon = Img("DishIcon", body, null, Color.white);
            Place(dishIcon.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(54f, 54f), new Vector2(1f, 1f));

            // Ingredient checklist: up to three lines, enabled per recipe.
            var slots = new OrderTicketView.LineSlot[3];
            for (int i = 0; i < slots.Length; i++)
            {
                RectTransform slot = Rect($"Line{i}", body);
                Place(slot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f + i * 94f, -80f), new Vector2(90f, 72f));

                Image icon = Img("Icon", slot, null, Color.white);
                Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(52f, 52f), new Vector2(0f, 0.5f));

                TextMeshProUGUI count = Text("Count", slot, font, 22f, Ink, TextAlignmentOptions.Left, bold: true);
                Place(count.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(52f, -6f), new Vector2(44f, 30f), new Vector2(0f, 0.5f));

                Image check = Img("Check", slot, Placeholder("check"), Color.white);
                Place(check.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, -16f), new Vector2(30f, 30f), new Vector2(0f, 0.5f));

                slots[i] = new OrderTicketView.LineSlot { root = slot.gameObject, icon = icon, count = count, check = check };
            }

            // Patience bar: a slot that holds position, and a child the bar shakes.
            RectTransform barSlot = Rect("PatienceSlot", body);
            Place(barSlot, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 12f), new Vector2(-24f, 22f), new Vector2(0.5f, 0f));
            RectTransform shake = Rect("Shake", barSlot);
            Stretch(shake);
            Panel(shake.gameObject.AddComponent<Image>(), new Color(0.25f, 0.18f, 0.16f));
            Image fill = Img("Fill", shake, Placeholder("square"), Color.green);
            Stretch(fill.rectTransform, 4f);
            fill.preserveAspect = false;   // a bar, not an icon
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            var bar = barSlot.gameObject.AddComponent<PatienceBar>();
            SetRefs(bar, ("fill", fill), ("shakeTarget", shake));

            // Ready: a green band over the finished checklist. The whole ticket is the button.
            RectTransform ready = Rect("Ready", body);
            Place(ready, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -78f), new Vector2(-16f, 78f), new Vector2(0.5f, 1f));
            Image readyBg = ready.gameObject.AddComponent<Image>();
            Panel(readyBg, new Color(0.27f, 0.62f, 0.27f));
            readyBg.raycastTarget = false;
            TextMeshProUGUI readyText = Text("Label", ready, PixelFont(font), 36f, Color.white, TextAlignmentOptions.Center, bold: false);
            Stretch(readyText.rectTransform);
            readyText.SetText("Tap to serve!");
            ready.gameObject.SetActive(false);

            var button = root.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.96f, 0.88f);
            colors.pressedColor = new Color(0.85f, 0.8f, 0.72f);
            colors.selectedColor = Color.white;
            button.colors = colors;

            Image flash = Img("Flash", body, Placeholder("panel"), Color.clear);
            flash.type = Image.Type.Sliced;
            Stretch(flash.rectTransform);

            var so = new SerializedObject(view);
            so.FindProperty("group").objectReferenceValue = group;
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("flash").objectReferenceValue = flash;
            so.FindProperty("portrait").objectReferenceValue = portrait;
            so.FindProperty("dishName").objectReferenceValue = dishName;
            so.FindProperty("dishIcon").objectReferenceValue = dishIcon;
            so.FindProperty("patience").objectReferenceValue = bar;
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("readyBanner").objectReferenceValue = ready.gameObject;
            SerializedProperty lines = so.FindProperty("lines");
            lines.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                SerializedProperty e = lines.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("root").objectReferenceValue = slots[i].root;
                e.FindPropertyRelative("icon").objectReferenceValue = slots[i].icon;
                e.FindPropertyRelative("count").objectReferenceValue = slots[i].count;
                e.FindPropertyRelative("check").objectReferenceValue = slots[i].check;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, PrefabDir + "/OrderTicket.prefab").GetComponent<OrderTicketView>();
        }

        private static ShopCard BuildShopCardPrefab()
        {
            TMP_FontAsset font = TooltipFont(Font());

            var root = new GameObject("ShopCard", typeof(RectTransform));
            ((RectTransform)root.transform).sizeDelta = new Vector2(440f, 300f);
            Panel(root.AddComponent<Image>(), Color.white);
            var card = root.AddComponent<ShopCard>();

            RectTransform iconFrame = Rect("IconFrame", root.transform);
            Place(iconFrame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -18f), new Vector2(104f, 104f));
            Panel(iconFrame.gameObject.AddComponent<Image>(), new Color(0.94f, 0.88f, 0.76f));
            Image icon = Img("Icon", iconFrame, null, Color.white);
            Stretch(icon.rectTransform, 8f);

            TextMeshProUGUI title = Text("Title", root.transform, font, 34f, Ink, TextAlignmentOptions.TopLeft, bold: true);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(138f, -20f), new Vector2(-156f, 46f));
            TextMeshProUGUI level = Text("Level", root.transform, font, 26f, new Color(0.55f, 0.42f, 0.35f), TextAlignmentOptions.TopLeft, bold: true);
            Place(level.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(138f, -70f), new Vector2(-156f, 36f));

            TextMeshProUGUI effect = Text("Effect", root.transform, font, 27f, Ink, TextAlignmentOptions.Left, bold: false);
            Place(effect.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -132f), new Vector2(-36f, 60f));
            effect.textWrappingMode = TextWrappingModes.Normal;

            RectTransform buyRt = Rect("Buy", root.transform);
            Place(buyRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 18f), new Vector2(-36f, 78f), new Vector2(0.5f, 0f));
            Image buyBg = buyRt.gameObject.AddComponent<Image>();
            Panel(buyBg, Accent);
            var buy = buyRt.gameObject.AddComponent<Button>();
            buy.targetGraphic = buyBg;
            TextMeshProUGUI price = Text("Price", buyRt, font, 38f, Color.white, TextAlignmentOptions.Center, bold: true);
            Stretch(price.rectTransform);

            // The Slime pixel fonts are drawn wide-spaced: pull the letters together, and drop fake
            // bold, which only smears pixel strokes.
            if (font != Font())
                foreach (TextMeshProUGUI t in new[] { title, level, effect, price })
                {
                    t.fontStyle = FontStyles.Normal;
                    t.characterSpacing = -9f;
                }
            if (font != Font()) title.fontSize = 31f;   // "Fresh Deliveries" must fit beside the icon

            SetRefs(card, ("icon", icon), ("title", title), ("level", level), ("effect", effect), ("buy", buy),
                    ("buyBackground", buyBg), ("price", price), ("punchTarget", (RectTransform)root.transform));

            return SavePrefab(root, PrefabDir + "/ShopCard.prefab").GetComponent<ShopCard>();
        }

        // ---------------------------------------------------------------- scene

        private static void BuildScene(UnityEngine.SceneManagement.Scene scene, BoardConfig config, PhysicsMaterial2D wallMat,
                                       IngredientDefinition[] ingredients, DishDefinition[] dishes, CustomerDefinition[] customers,
                                       KnifeController knifePrefab, Ingredient ingredientPrefab, OrderTicketView ticketPrefab,
                                       UpgradeDefinition[] upgrades, ShopCard cardPrefab)
        {
            TMP_FontAsset font = Font();

            // Camera: contain-fit is applied by BoardViewportAdapter; 5.4 is its 16:9 value.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;   // what letterbox bars show
            camGo.AddComponent<AudioListener>();
            SetRefs(camGo.AddComponent<BoardViewportAdapter>(), ("boardConfig", config));

            var lightGo = new GameObject("Global Light 2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;

            var bootstrap = new GameObject("Bootstrap").AddComponent<GameBootstrap>();
            SetRefs(bootstrap, ("boardConfig", config));
            SetArray(bootstrap, "upgrades", upgrades);

            var bounds = new GameObject("Board").AddComponent<BoardBounds>();
            SetRefs(bounds, ("wallSprite", Placeholder("square")), ("floorSprite", Placeholder("floor_tile")), ("wallMaterial", wallMat));

            // Aim: input, preview, and the knife waiting at the launcher.
            var aimGo = new GameObject("Aim");
            var aim = aimGo.AddComponent<AimInput>();
            SetRefs(aim, ("boardCamera", cam), ("actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath)));

            var baseGo = new GameObject("LauncherBase");
            baseGo.transform.SetParent(aimGo.transform, false);
            baseGo.transform.localScale = Vector3.one * (1.1f / 0.64f);
            var launcherBase = baseGo.AddComponent<SpriteRenderer>();
            launcherBase.sprite = Placeholder("circle");
            launcherBase.sortingOrder = 3;

            var waitingGo = new GameObject("LauncherKnife");
            waitingGo.transform.SetParent(aimGo.transform, false);
            waitingGo.transform.localScale = Vector3.one * 3f;
            var waiting = waitingGo.AddComponent<SpriteRenderer>();
            waiting.sprite = Placeholder("knife");
            waiting.sortingOrder = 20;

            var preview = aimGo.AddComponent<TrajectoryPreview>();
            SetRefs(preview, ("dotSprite", Placeholder("circle")), ("launcherKnife", waiting), ("launcherBase", launcherBase));

            var knives = new GameObject("Knives").AddComponent<KnifeManager>();
            SetRefs(knives, ("knifePrefab", knifePrefab));

            var spawnerGo = new GameObject("Ingredients");
            var spawner = spawnerGo.AddComponent<IngredientSpawner>();
            SetRefs(spawner, ("ingredientPrefab", ingredientPrefab), ("poolRoot", spawnerGo.transform));
            SetArray(spawner, "definitions", ingredients);

            var orders = new GameObject("Orders").AddComponent<OrderSpawner>();
            SetArray(orders, "dishes", dishes);
            SetArray(orders, "customers", customers);

            var fx = new GameObject("BoardFx").AddComponent<BoardFx>();
            SetRefs(fx, ("particleSprite", Placeholder("square")), ("ringSprite", Placeholder("circle")), ("popupFont", font));

            KitchenDecor kitchen = BuildKitchen();
            CustomerLine customerLine = BuildCustomers(PixelFont(font));
            DiceView diceView = BuildDice(config, PixelFont(font));

            var audioGo = new GameObject("Audio");
            var audioSource = audioGo.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            var musicSource = audioGo.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            var gameAudio = audioGo.AddComponent<GameAudio>();
            SetRefs(gameAudio, ("source", audioSource), ("musicSource", musicSource),
                    ("music", AssetDatabase.LoadAssetAtPath<AudioClip>(ArtFpp + "/Audio/Song1.mp3")));

            Menus menus = BuildHud(cam, ticketPrefab, cardPrefab, font, gameAudio, customerLine);
            SetArray(aim, "blockers", new Object[] { menus.HudUpgrades.transform, menus.Orders.transform.Find("Slide"), menus.AutoToggle.transform });

            var runner = new GameObject("Runner").AddComponent<GameRunner>();
            SetRefs(runner,
                ("bootstrap", bootstrap), ("bounds", bounds), ("ingredients", spawner), ("knives", knives),
                ("aimInput", aim), ("trajectoryPreview", preview), ("orderSpawner", orders), ("boardFx", fx), ("kitchen", kitchen),
                ("hud", menus.Hud), ("overlay", menus.Overlay), ("gameAudio", gameAudio), ("shop", menus.Shop),
                ("mainMenu", menus.MainMenu), ("settingsPanel", menus.Settings), ("hudUpgrades", menus.HudUpgrades), ("banner", menus.Banner),
                ("customers", customerLine), ("ordersPanel", menus.Orders),
                ("dice", diceView), ("autoToggle", menus.AutoToggle), ("autoLabel", menus.AutoLabel));

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // Riley's "Restaurant Character and Tileset" (rkuhlf-assets.itch.io/restaurant-pixel-art, CC0).
        private const string RestaurantDir = "Assets/Art/Spritesheets";
        private const string ChefSheet = RestaurantDir + "/ChefSheet.png";

        /// <summary>The cooking line and side shelves, dressed with Pixel Mart props and the
        /// restaurant pack's chef, pot and meats. Anything missing is simply left out.</summary>
        private static KitchenDecor BuildKitchen()
        {
            var kitchen = new GameObject("Kitchen").AddComponent<KitchenDecor>();
            Sprite brick = RestaurantSprite("Tiles", 8);
            SetRefs(kitchen, ("square", Placeholder("square")), ("circle", Placeholder("circle")),
                    ("wallTile", brick != null ? brick : Placeholder("floor_tile")));
            if (brick != null) SetColor(kitchen, "wallColor", new Color(0.5f, 0.42f, 0.42f));   // brick, dimmed behind the board

            // The dining room: wooden plank wall, the pack's front door, a chair and table.
            SetRefs(kitchen, ("diningWall", RestaurantSprite("Tiles", 16)), ("door", RestaurantSprite("Door", 1)));
            SetArray(kitchen, "furniture", Restaurant(("Furniture", 0), ("Furniture", 1)));
            Sprite chandelier = ArtFolderSprites(ChandelierDir).FirstOrDefault();
            if (chandelier != null) SetRefs(kitchen, ("chandelier", chandelier));

            SetArray(kitchen, "pans", Marts("frying pan"));
            SetArray(kitchen, "pots", Restaurant(("ChefTools", 5)).Concat(Marts("teakettle")).ToArray());
            SetArray(kitchen, "counterItems", Marts("chopping board").Concat(Restaurant(("Meat", 1), ("Meat", 3), ("Meat", 0))).ToArray());
            SetArray(kitchen, "shelfItems", Marts("salt", "sugar", "flour", "cooking_oil", "ketchup", "mustard", "olive_oil", "milk_bottle", "egg_box", "butter"));
            SetArray(kitchen, "hangingTools", Marts("spatula", "whisk", "rolling pin", "kitchen_knife_set"));

            // The chef's Aseprite tags: Chop is frames 13–14, Jump 7–12.
            Sprite[] chef = ChefFrames();
            if (chef.Length >= 15)
            {
                SetArray(kitchen, "chefChop", new Object[] { chef[13], chef[14] });
                SetArray(kitchen, "chefCheer", chef.Skip(7).Take(6).ToArray<Object>());
            }
            return kitchen;
        }

        private static Object[] Marts(params string[] names) => names.Select(MartSprite).Where(s => s != null).ToArray<Object>();

        private static Object[] Restaurant(params (string sheet, int index)[] picks)
            => picks.Select(p => RestaurantSprite(p.sheet, p.index)).Where(s => s != null).ToArray<Object>();

        private static Sprite RestaurantSprite(string sheet, int index)
            => AssetDatabase.LoadAllAssetsAtPath($"{RestaurantDir}/{sheet}.png").OfType<Sprite>().FirstOrDefault(s => s.name == $"{sheet}_{index}");

        /// <summary>The chef sheet as a fixed 48×56 grid pivoted at the feet, in frame order. The
        /// pack ships it cropped tight with centred pivots, which makes every frame jump about.</summary>
        private static Sprite[] ChefFrames()
        {
            if (!File.Exists(ChefSheet)) return new Sprite[0];
            return LoadAllAssetsSorted(ChefSheet);
        }

        private static Sprite[] LoadAllAssetsSorted(string path)
            => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .OrderBy(s => int.TryParse(s.name.Substring(s.name.LastIndexOf('_') + 1), out int n) ? n : 0).ToArray();

        /// <summary>Point-filtered pixel art, sliced into a fixed grid, keeping each cell's sprite
        /// name (sheet_index) so references survive a re-slice.</summary>
        private static void GridSlice(string path, int cellWidth, int cellHeight, Vector2 pivot, bool fullRect)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            var ts = new TextureImporterSettings();
            importer.ReadTextureSettings(ts);
            ts.spriteMeshType = fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            importer.SetTextureSettings(ts);

            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            string sheet = Path.GetFileNameWithoutExtension(path);

            var factory = new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            // Keep existing sprite IDs by name, so a re-run doesn't break references to them.
            var existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            var rects = new System.Collections.Generic.List<UnityEditor.SpriteRect>();
            int columns = width / cellWidth;
            for (int i = 0; i < columns; i++)
            {
                string name = $"{sheet}_{i}";
                rects.Add(new UnityEditor.SpriteRect
                {
                    name = name,
                    spriteID = existing.TryGetValue(name, out GUID id) ? id : GUID.Generate(),
                    rect = new Rect(i * cellWidth, height - cellHeight, cellWidth, cellHeight),
                    alignment = SpriteAlignment.Custom,
                    pivot = pivot
                });
            }
            provider.SetSpriteRects(rects.ToArray());

            var names = provider.GetDataProvider<UnityEditor.U2D.Sprites.ISpriteNameFileIdDataProvider>();
            names?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));

            provider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>The restaurant pack's sheets: the chef re-gridded (see <see cref="ChefFrames"/>),
        /// the tiles cut into 32 px squares for tiling, everything point-filtered.</summary>
        private static void ConfigureRestaurantTextures()
        {
            if (!AssetDatabase.IsValidFolder(RestaurantDir)) return;

            GridSlice(ChefSheet, 48, 56, new Vector2(0.5f, 0f), fullRect: false);
            GridSlice(RestaurantDir + "/Tiles.png", 32, 32, new Vector2(0.5f, 0.5f), fullRect: true);
            GridSlice(RestaurantDir + "/Door.png", 64, 64, new Vector2(0.5f, 0f), fullRect: false);        // side, front
            GridSlice(RestaurantDir + "/Furniture.png", 32, 32, new Vector2(0.5f, 0f), fullRect: false);   // chair, table, stool, …

            foreach (string sheet in new[] { "ChefTools", "Meat" })
            {
                var importer = AssetImporter.GetAtPath($"{RestaurantDir}/{sheet}.png") as TextureImporter;
                if (importer == null || (importer.filterMode == FilterMode.Point && importer.textureCompression == TextureImporterCompression.Uncompressed)) continue;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        // Customers: side-view walk cycles facing right, one character per PNG, frames in a single
        // row of squares (frame size = sheet height). Drop a pack into Assets/Art/Customers to
        // replace the generated placeholders; an optional "<name>_idle.png" adds idle frames.
        private const string CustomersDir = "Assets/Art/Customers";
        private const string PlaceholderCustomersDir = ArtPlaceholders + "/Customers";

        private static string CustomerArtDir()
            => AssetDatabase.IsValidFolder(CustomersDir) && Directory.GetFiles(CustomersDir, "*.png").Length > 0 ? CustomersDir : PlaceholderCustomersDir;

        private static string[] CustomerSheets()
        {
            string dir = CustomerArtDir();
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.png").Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToArray()
                : new string[0];
        }

        private static void ConfigureCustomerTextures()
        {
            foreach (string sheet in CustomerSheets())
            {
                var importer = AssetImporter.GetAtPath(sheet) as TextureImporter;
                if (importer == null) continue;
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if (height <= 0 || width % height != 0)
                {
                    Debug.LogWarning($"[Setup] {sheet} is {width}×{height}: customer sheets must be one row of square frames. Skipped.");
                    continue;
                }
                GridSlice(sheet, height, height, new Vector2(0.5f, 0f), fullRect: false);
            }
        }

        private static CustomerLine BuildCustomers(TMP_FontAsset font)
        {
            var line = new GameObject("Customers").AddComponent<CustomerLine>();
            SetRefs(line, ("square", Placeholder("square")), ("check", Placeholder("check")), ("font", font));

            string[] sheets = CustomerSheets().Where(s => !s.EndsWith("_idle.png")).ToArray();
            var so = new SerializedObject(line);
            SerializedProperty looks = so.FindProperty("looks");
            looks.arraySize = 0;
            foreach (string sheet in sheets)
            {
                Sprite[] walk = LoadAllAssetsSorted(sheet);
                if (walk.Length == 0) continue;
                Sprite[] idle = LoadAllAssetsSorted(sheet.Substring(0, sheet.Length - 4) + "_idle.png");

                looks.arraySize++;
                SerializedProperty look = looks.GetArrayElementAtIndex(looks.arraySize - 1);
                SetSprites(look.FindPropertyRelative("walk"), walk);
                SetSprites(look.FindPropertyRelative("idle"), idle);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[Setup] Customers: {looks.arraySize} looks from {CustomerArtDir()}.");
            return line;
        }

        // Dice: faces 1–6, either one row of six square frames or six separate images (sorted by
        // name); a file with "roll" in its name adds tumbling frames. Drop a pack into
        // Assets/Art/Dice to replace the generated placeholder faces.
        // Chandelier: the first PNG in Assets/Art/Chandelier replaces the dining room's lamps.
        private const string DiceDir = "Assets/Art/Dice";
        private const string PlaceholderDiceDir = ArtPlaceholders + "/Dice";
        private const string ChandelierDir = "Assets/Art/Chandelier";

        private static string DiceArtDir()
            => AssetDatabase.IsValidFolder(DiceDir) && Directory.GetFiles(DiceDir, "*.png").Length > 0 ? DiceDir : PlaceholderDiceDir;

        private static string[] Pngs(string dir)
            => Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.png").Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToArray()
                : new string[0];

        private static void ConfigureDiceAndChandelierTextures()
        {
            foreach (string png in Pngs(DiceArtDir()).Concat(Pngs(ChandelierDir)))
            {
                var importer = AssetImporter.GetAtPath(png) as TextureImporter;
                if (importer == null) continue;
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);

                bool strip = !png.StartsWith(ChandelierDir) && height > 0 && width % height == 0 && width / height > 1;
                if (strip) { GridSlice(png, height, height, new Vector2(0.5f, 0.5f), fullRect: false); continue; }

                if (importer.textureType == TextureImporterType.Sprite && importer.filterMode == FilterMode.Point
                    && importer.textureCompression == TextureImporterCompression.Uncompressed) continue;
                importer.textureType = TextureImporterType.Sprite;
                if (importer.spriteImportMode == SpriteImportMode.None) importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        private static Sprite[] ArtFolderSprites(string dir, System.Func<string, bool> filter = null)
            => Pngs(dir).Where(f => filter == null || filter(f)).SelectMany(LoadAllAssetsSorted).ToArray();

        private static DiceView BuildDice(BoardConfig config, TMP_FontAsset font)
        {
            string dir = DiceArtDir();
            Sprite[] faces = ArtFolderSprites(dir, f => !Path.GetFileName(f).ToLowerInvariant().Contains("roll")).Take(6).ToArray();
            Sprite[] roll = ArtFolderSprites(dir, f => Path.GetFileName(f).ToLowerInvariant().Contains("roll"));
            if (faces.Length < 6) Debug.LogWarning($"[Setup] Dice: found {faces.Length} faces in {dir}, need 6 (1 to 6).");
            else Debug.Log($"[Setup] Dice: faces from {dir}{(roll.Length > 0 ? $", {roll.Length} roll frames" : "")}.");

            // Beside the launcher, in the launch corridor.
            var root = new GameObject("Dice");
            root.transform.position = config.launcherPosition + new Vector2(1.6f, -0.1f);

            var shadow = new GameObject("Shadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(root.transform, false);
            shadow.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            shadow.sprite = Placeholder("circle");
            shadow.color = new Color(0f, 0f, 0f, 0.25f);
            shadow.sortingOrder = 19;

            var face = new GameObject("Face").AddComponent<SpriteRenderer>();
            face.transform.SetParent(root.transform, false);
            face.sortingOrder = 21;
            if (faces.Length > 0) face.sprite = faces[0];

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.68f, 0f);   // above: the board frame is just below
            var label = labelGo.AddComponent<TextMeshPro>();
            if (font != null) label.font = font;
            label.fontSize = 2.4f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = new Color(0.36f, 0.22f, 0.18f);
            label.sortingOrder = 22;
            label.rectTransform.sizeDelta = new Vector2(2f, 0.4f);

            var view = root.AddComponent<DiceView>();
            SetRefs(view, ("face", face), ("shadow", shadow), ("label", label));
            SetArray(view, "faces", faces.ToArray<Object>());
            SetArray(view, "rollFrames", roll.ToArray<Object>());
            return view;
        }

        private static void SetSprites(SerializedProperty array, Sprite[] sprites)
        {
            array.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }

        private static void SetColor(Object target, string field, Color value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).colorValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private struct Menus
        {
            public OrderTicketHud Hud;
            public RunOverlay Overlay;
            public ShopPanel Shop;
            public MainMenu MainMenu;
            public SettingsPanel Settings;
            public Button HudUpgrades;
            public HudBanner Banner;
            public OrdersPanel Orders;
            public Button AutoToggle;
            public TextMeshProUGUI AutoLabel;
        }

        private static Menus BuildHud(Camera cam, OrderTicketView ticketPrefab, ShopCard cardPrefab,
                                      TMP_FontAsset font, GameAudio gameAudio, CustomerLine customerLine)
        {
            TMP_FontAsset pixelFont = PixelFont(font);

            // §1 plan §6.4: Screen Space Overlay, scale with screen size at 1920×1080, match 0.5.
            var canvasGo = new GameObject("HUD", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Every HUD element lives under the safe-area root; nothing parents to the canvas.
            RectTransform safe = Rect("SafeAreaRoot", canvasGo.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // The orders panel: slides in from the left over the dining room when the customers
            // are tapped. Tickets sit two to a row in spawn order; a rush shrinks the grid to fit.
            RectTransform ordersRoot = Rect("Orders", safe);
            Stretch(ordersRoot);
            RectTransform slide = Rect("Slide", ordersRoot);
            slide.anchorMin = new Vector2(0f, 0f);
            slide.anchorMax = new Vector2(0f, 1f);
            slide.pivot = new Vector2(0f, 0.5f);
            slide.offsetMin = new Vector2(0f, 10f);
            slide.offsetMax = new Vector2(620f, -10f);
            Panel(slide.gameObject.AddComponent<Image>(), new Color(0.16f, 0.1f, 0.09f, 0.94f));

            TextMeshProUGUI ordersTitle = Text("Title", slide, pixelFont, 46f, new Color(1f, 0.93f, 0.76f), TextAlignmentOptions.Left, bold: false);
            Place(ordersTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -12f), new Vector2(-120f, 58f));
            ordersTitle.SetText("Orders");
            TextMeshProUGUI ordersHint = Text("Hint", slide, font, 24f, new Color(1f, 0.93f, 0.76f, 0.75f), TextAlignmentOptions.Left, bold: false);
            Place(ordersHint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -70f), new Vector2(-120f, 34f));
            ordersHint.SetText("Tap a green ticket to serve it");

            Button closeOrders = PixelButton("Close", slide, FppSprite(FppUiSheet, "UI_91"), pixelFont, 30f, Color.white,
                                             new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(84f, 84f), gameAudio, out TextMeshProUGUI closeLabel);
            ((RectTransform)closeOrders.transform).pivot = new Vector2(1f, 1f);
            closeLabel.SetText("");
            Image closeIcon = Img("Icon", closeOrders.transform, FppIcon("X"), Color.white);
            Place(closeIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(46f, 46f), new Vector2(0.5f, 0.5f));

            RectTransform strip = Rect("Tickets", slide);
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 1f);
            strip.pivot = new Vector2(0f, 1f);
            strip.offsetMin = new Vector2(16f, 16f);
            strip.offsetMax = new Vector2(-16f, -116f);
            var grid = strip.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(300f, 196f);
            grid.spacing = new Vector2(14f, 14f);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            var ordersPanel = ordersRoot.gameObject.AddComponent<OrdersPanel>();
            SetRefs(ordersPanel, ("slide", slide), ("list", strip), ("closeButton", closeOrders));

            // Status: cash and reputation, top-right.
            RectTransform status = Rect("Status", safe);
            Place(status, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -10f), new Vector2(270f, 140f), new Vector2(1f, 1f));
            Panel(status.gameObject.AddComponent<Image>(), new Color(0.12f, 0.08f, 0.08f, 0.72f));

            RectTransform cashRow = Rect("CashRow", status);
            Place(cashRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -12f), new Vector2(-24f, 60f), new Vector2(0.5f, 1f));
            Image coin = Img("Coin", cashRow, Placeholder("coin"), Color.white);
            Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(48f, 48f), new Vector2(0f, 0.5f));
            TextMeshProUGUI cashText = Text("Cash", cashRow, font, 48f, new Color(1f, 0.88f, 0.4f), TextAlignmentOptions.Left, bold: true);
            Place(cashText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(64f, 0f), new Vector2(-64f, 0f), new Vector2(0f, 0.5f));
            cashText.rectTransform.offsetMin = new Vector2(64f, 0f);
            cashText.rectTransform.offsetMax = Vector2.zero;
            var cash = status.gameObject.AddComponent<CashReadout>();
            SetRefs(cash, ("label", cashText), ("punchTarget", cashRow));

            RectTransform heartRow = Rect("Hearts", status);
            Place(heartRow, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 14f), new Vector2(-24f, 48f), new Vector2(0.5f, 0f));
            var heartLayout = heartRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            heartLayout.spacing = 8f;
            heartLayout.childAlignment = TextAnchor.MiddleLeft;
            heartLayout.childControlWidth = false;
            heartLayout.childControlHeight = false;
            heartLayout.childForceExpandWidth = false;
            heartLayout.childForceExpandHeight = false;
            var hearts = new Image[5];
            for (int i = 0; i < hearts.Length; i++)
            {
                hearts[i] = Img($"Heart{i}", heartRow, Placeholder("heart"), Color.white);
                hearts[i].rectTransform.sizeDelta = new Vector2(46f, 46f);
            }
            var reputation = status.gameObject.AddComponent<ReputationReadout>();
            SetArray(reputation, "hearts", hearts);

            // Mid-run shop, under the status panel: the run freezes while it is open.
            Button hudUpgrades = PixelButton("Upgrades", safe, FppSprite(FppUiSheet, "UI_91"), pixelFont, 30f, Color.white,
                                             new Vector2(1f, 1f), new Vector2(-18f, -162f), new Vector2(270f, 84f), gameAudio, out TextMeshProUGUI upgradesLabel);
            upgradesLabel.SetText("Upgrades");
            upgradesLabel.rectTransform.offsetMin = new Vector2(64f, 0f);
            upgradesLabel.rectTransform.offsetMax = new Vector2(-14f, 4f);
            Image shopIcon = Img("Icon", hudUpgrades.transform, FppIcon("Shop"), Color.white);
            Place(shopIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 2f), new Vector2(46f, 46f), new Vector2(0f, 0.5f));

            // Auto-throw switch, under Upgrades; GameRunner shows it once auto-throw is unlocked.
            Button autoToggle = PixelButton("AutoThrow", safe, FppSprite(FppUiSheet, "UI_91"), pixelFont, 28f, Color.white,
                                            new Vector2(1f, 1f), new Vector2(-18f, -254f), new Vector2(270f, 76f), gameAudio, out TextMeshProUGUI autoLabel);
            autoLabel.SetText("Auto: ON");
            autoToggle.gameObject.SetActive(false);

            // Run events (rush hour, bigger orders, "order ready"), across the top of the board.
            RectTransform bannerRt = Rect("Banner", safe);
            Place(bannerRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(120f, -14f), new Vector2(1120f, 72f), new Vector2(0.5f, 1f));
            var bannerGroup = bannerRt.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.blocksRaycasts = false;
            bannerGroup.alpha = 0f;
            Image bannerBg = bannerRt.gameObject.AddComponent<Image>();
            Panel(bannerBg, new Color(0.78f, 0.16f, 0.12f, 0.92f));
            bannerBg.raycastTarget = false;
            TextMeshProUGUI bannerText = Text("Label", bannerRt, pixelFont, 32f, Color.white, TextAlignmentOptions.Center, bold: false);
            Stretch(bannerText.rectTransform);
            var banner = bannerRt.gameObject.AddComponent<HudBanner>();
            SetRefs(banner, ("group", bannerGroup), ("background", bannerBg), ("label", bannerText));

            // First-run hint in the launch corridor, under the board.
            RectTransform hintRt = Rect("Hint", safe);
            Place(hintRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(250f, 18f), new Vector2(760f, 70f), new Vector2(0.5f, 0f));
            var hint = hintRt.gameObject.AddComponent<CanvasGroup>();
            hint.blocksRaycasts = false;
            TextMeshProUGUI hintText = Text("Text", hintRt, font, 32f, Color.white, TextAlignmentOptions.Bottom, bold: true);
            Stretch(hintText.rectTransform);
            hintText.SetText("Drag back and release to throw the knife");
            hintText.outlineWidth = 0.2f;
            hintText.outlineColor = new Color32(40, 20, 20, 255);

            RectTransform effects = Rect("Effects", safe);
            Stretch(effects);

            var hud = canvasGo.AddComponent<OrderTicketHud>();
            SetRefs(hud,
                ("container", strip), ("ticketPrefab", ticketPrefab), ("cash", cash), ("reputation", reputation),
                ("hint", hint), ("boardCamera", cam), ("effectsRoot", effects), ("font", font),
                ("panel", ordersPanel), ("customers", customerLine));

            // Sibling order is draw order: the shop and settings open on top of the menu and cards.
            RunOverlay overlay = BuildOverlay(canvasGo.transform, font, pixelFont, gameAudio);
            MainMenu mainMenu = BuildMainMenu(canvasGo.transform, font, pixelFont, gameAudio);
            ShopPanel shop = BuildShop(canvasGo.transform, cardPrefab, font, pixelFont, gameAudio);
            SettingsPanel settings = BuildSettings(canvasGo.transform, font, pixelFont, gameAudio);

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            return new Menus { Hud = hud, Overlay = overlay, Shop = shop, MainMenu = mainMenu, Settings = settings, HudUpgrades = hudUpgrades, Banner = banner, Orders = ordersPanel,
                             AutoToggle = autoToggle, AutoLabel = autoLabel };
        }

        // ---------------------------------------------------------------- main menu & settings

        private static readonly Color Cream = new Color(1f, 0.93f, 0.76f);
        private static readonly Color Cocoa = new Color(0.36f, 0.22f, 0.18f);

        private static MainMenu BuildMainMenu(Transform canvas, TMP_FontAsset font, TMP_FontAsset pixelFont, GameAudio gameAudio)
        {
            // The live board shows through the dim: the menu sits in the kitchen it opens.
            RectTransform root = Rect("MainMenu", canvas);
            Stretch(root);
            Image dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.06f, 0.04f, 0.04f, 0.62f);
            var group = root.gameObject.AddComponent<CanvasGroup>();

            // Logo: the name between a pan and a spatula, bobbing above the menu card.
            RectTransform logo = Rect("Logo", root);
            Place(logo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 345f), new Vector2(1300f, 220f), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI title = Text("Title", logo, pixelFont, 150f, Cream, TextAlignmentOptions.Center, bold: false);
            Stretch(title.rectTransform);
            title.SetText("Slice & Serve");
            title.outlineWidth = 0.22f;
            title.outlineColor = new Color32(70, 30, 24, 255);
            Image pan = Img("Pan", logo, FppIcon("KN004"), Color.white);
            Place(pan.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 10f), new Vector2(150f, 150f), new Vector2(0.5f, 0.5f));
            pan.rectTransform.localEulerAngles = new Vector3(0f, 0f, 20f);
            Image spatula = Img("Spatula", logo, FppIcon("KN005"), Color.white);
            Place(spatula.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 10f), new Vector2(150f, 150f), new Vector2(0.5f, 0.5f));
            spatula.rectTransform.localEulerAngles = new Vector3(0f, 0f, -20f);
            spatula.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // mirrored, so the pair frames the name

            TextMeshProUGUI tagline = Text("Tagline", root, pixelFont, 40f, new Color(1f, 0.82f, 0.55f), TextAlignmentOptions.Center, bold: false);
            Place(tagline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 232f), new Vector2(1200f, 60f), new Vector2(0.5f, 0.5f));
            tagline.SetText("Bounce the knife. Feed the rush.");

            // The menu card: Food-Ping-Pong's framed paper, with the three choices stacked.
            RectTransform card = Rect("Card", root);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(620f, 680f), new Vector2(0.5f, 0.5f));
            PixelPanel(card.gameObject.AddComponent<Image>(), FppSprite(FppBooksSheet, "UI books & more_18"));

            Button play = PixelButton("Play", card, FppSprite(FppUiSheet, "UI_91"), pixelFont, 72f, Color.white,
                                      new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(460f, 130f), gameAudio, out TextMeshProUGUI playLabel);
            playLabel.SetText("Play");
            Button upgrade = MenuButton("Upgrade", card, pixelFont, "Shop", new Vector2(0f, -210f), gameAudio);
            Button settings = MenuButton("Settings", card, pixelFont, "Settings", new Vector2(0f, -322f), gameAudio);

            // Exit, last and quieter: smaller, with a red label, so it is never mistaken for Play.
            Button exit = MenuButton("Exit", card, pixelFont, "X", new Vector2(0f, -434f), gameAudio);
            var exitRt = (RectTransform)exit.transform;
            exitRt.sizeDelta = new Vector2(360f, 86f);
            exit.GetComponentInChildren<TextMeshProUGUI>().color = new Color(0.72f, 0.2f, 0.15f);
            exit.transform.Find("Icon").GetComponent<Image>().color = new Color(0.72f, 0.2f, 0.15f);

            // A badge on Upgrade when something in the shop is affordable: the nudge to spend.
            Image badge = Img("Badge", upgrade.transform, Placeholder("circle"), new Color(0.86f, 0.2f, 0.16f));
            Place(badge.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(10f, 10f), new Vector2(46f, 46f), new Vector2(0.5f, 0.5f));
            badge.preserveAspect = false;
            TextMeshProUGUI bang = Text("Mark", badge.transform, pixelFont, 36f, Color.white, TextAlignmentOptions.Center, bold: false);
            Stretch(bang.rectTransform);
            bang.SetText("!");

            // The till and the best run, along the bottom of the card.
            RectTransform till = Rect("Till", card);
            Place(till, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-110f, 36f), new Vector2(240f, 60f), new Vector2(0.5f, 0f));
            Image coin = Img("Coin", till, Placeholder("coin"), Color.white);
            Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(46f, 46f), new Vector2(0f, 0.5f));
            TextMeshProUGUI cash = Text("Cash", till, pixelFont, 44f, new Color(0.62f, 0.38f, 0.08f), TextAlignmentOptions.Left, bold: false);
            Stretch(cash.rectTransform);
            cash.rectTransform.offsetMin = new Vector2(58f, 0f);
            TextMeshProUGUI best = Text("Best", card, pixelFont, 30f, Cocoa, TextAlignmentOptions.Right, bold: false);
            Place(best.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(110f, 36f), new Vector2(260f, 60f), new Vector2(0.5f, 0f));

            var menu = root.gameObject.AddComponent<MainMenu>();
            SetRefs(menu, ("group", group), ("logo", logo), ("play", play), ("upgrade", upgrade), ("settings", settings), ("exit", exit),
                    ("till", cash), ("best", best), ("upgradeBadge", badge.gameObject));
            return menu;
        }

        /// <summary>A secondary menu choice: cream pill, dark label, icon on the left.</summary>
        private static Button MenuButton(string name, RectTransform card, TMP_FontAsset pixelFont, string icon, Vector2 position, GameAudio gameAudio)
        {
            Button button = PixelButton(name, card, FppSprite(FppBooksSheet, "UI books & more_240"), pixelFont, 52f, Cocoa,
                                        new Vector2(0.5f, 1f), position, new Vector2(460f, 100f), gameAudio, out TextMeshProUGUI label);
            label.SetText(name);
            label.rectTransform.offsetMin = new Vector2(60f, 0f);
            Image img = Img("Icon", button.transform, FppIcon(icon), Cocoa);
            Place(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(56f, 56f), new Vector2(0f, 0.5f));
            return button;
        }

        private static SettingsPanel BuildSettings(Transform canvas, TMP_FontAsset font, TMP_FontAsset pixelFont, GameAudio gameAudio)
        {
            RectTransform root = Rect("Settings", canvas);
            Stretch(root);
            Image dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.03f, 0.03f, 0.8f);
            var group = root.gameObject.AddComponent<CanvasGroup>();

            RectTransform card = Rect("Card", root);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 640f), new Vector2(0.5f, 0.5f));
            PixelPanel(card.gameObject.AddComponent<Image>(), FppSprite(FppBooksSheet, "UI books & more_18"));

            TextMeshProUGUI title = Text("Title", card, pixelFont, 80f, new Color(0.75f, 0.22f, 0.15f), TextAlignmentOptions.Center, bold: false);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -40f), new Vector2(-80f, 100f), new Vector2(0.5f, 1f));
            title.SetText("Settings");

            Slider music = SettingsSlider("Music", card, pixelFont, "Music", -180f);
            Slider sfx = SettingsSlider("Sound", card, pixelFont, "Sound", -290f);

            // Fullscreen: PC only; the panel hides the row on phones.
            RectTransform row = SettingsRow("Fullscreen", card, pixelFont, "Settings", -400f);
            RectTransform box = Rect("Toggle", row);
            Place(box, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(76f, 76f), new Vector2(1f, 0.5f));
            Image boxBg = box.gameObject.AddComponent<Image>();
            PixelPanel(boxBg, FppSprite(FppBooksSheet, "UI books & more_240"));
            Image check = Img("Check", box, Placeholder("check"), Color.white);
            Stretch(check.rectTransform, 10f);
            var toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxBg;
            toggle.graphic = check;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(toggle.onValueChanged, gameAudio.PlayToggle);

            Button back = PixelButton("Back", card, FppSprite(FppUiSheet, "UI_91"), pixelFont, 52f, Color.white,
                                      new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(360f, 100f), gameAudio, out TextMeshProUGUI backLabel);
            backLabel.SetText("Back");

            var panel = root.gameObject.AddComponent<SettingsPanel>();
            SetRefs(panel, ("group", group), ("music", music), ("sfx", sfx), ("fullscreen", toggle),
                    ("fullscreenRow", row.gameObject), ("back", back));
            root.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>One settings line: an icon and a label on the left, the control on the right.</summary>
        private static RectTransform SettingsRow(string name, RectTransform card, TMP_FontAsset pixelFont, string icon, float y)
        {
            RectTransform row = Rect(name + "Row", card);
            Place(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, y), new Vector2(-120f, 90f), new Vector2(0.5f, 0.5f));
            Image img = Img("Icon", row, FppIcon(icon), Cocoa);
            Place(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(60f, 60f), new Vector2(0f, 0.5f));
            TextMeshProUGUI label = Text("Label", row, pixelFont, 46f, Cocoa, TextAlignmentOptions.Left, bold: false);
            Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(80f, 0f), new Vector2(280f, 70f), new Vector2(0f, 0.5f));
            label.SetText(name);
            return row;
        }

        private static Slider SettingsSlider(string name, RectTransform card, TMP_FontAsset pixelFont, string icon, float y)
        {
            RectTransform row = SettingsRow(name, card, pixelFont, icon, y);

            RectTransform rt = Rect("Slider", row);
            Place(rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(360f, 44f), new Vector2(1f, 0.5f));
            Image track = rt.gameObject.AddComponent<Image>();
            PixelPanel(track, FppSprite(FppBooksSheet, "UI books & more_240"));
            track.color = new Color(0.78f, 0.66f, 0.55f);

            RectTransform fillArea = Rect("Fill Area", rt);
            Stretch(fillArea, 8f);
            Image fill = Img("Fill", fillArea, Placeholder("panel"), Accent);
            fill.type = Image.Type.Sliced;
            fill.preserveAspect = false;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

            RectTransform handleArea = Rect("Handle Slide Area", rt);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(20f, 0f);
            handleArea.offsetMax = new Vector2(-20f, 0f);
            // The handle: a cocoa ring with a cream centre (a child draws over its parent).
            Image handle = Img("Handle", handleArea, Placeholder("circle"), Cocoa);
            handle.raycastTarget = true;
            handle.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            handle.rectTransform.offsetMin = new Vector2(-30f, -10f);
            handle.rectTransform.offsetMax = new Vector2(30f, 10f);
            Image centre = Img("Centre", handle.transform, Placeholder("circle"), Cream);
            Stretch(centre.rectTransform, 7f);

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.5f;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }

        private static RunOverlay BuildOverlay(Transform canvas, TMP_FontAsset font, TMP_FontAsset pixelFont, GameAudio gameAudio)
        {
            // The dim covers the whole screen; the card sits centred, well inside any safe area.
            RectTransform root = Rect("Overlay", canvas);
            Stretch(root);
            Image dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.03f, 0.03f, 0.72f);
            var group = root.gameObject.AddComponent<CanvasGroup>();

            RectTransform card = Rect("Card", root);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 640f), new Vector2(0.5f, 0.5f));
            Panel(card.gameObject.AddComponent<Image>(), Paper);

            TextMeshProUGUI title = Text("Title", card, pixelFont, 84f, new Color(0.75f, 0.22f, 0.15f), TextAlignmentOptions.Center, bold: false);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -36f), new Vector2(-60f, 110f), new Vector2(0.5f, 1f));

            TextMeshProUGUI body = Text("Body", card, font, 32f, Ink, TextAlignmentOptions.Center, bold: false);
            Place(body.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            body.rectTransform.offsetMin = new Vector2(60f, 170f);
            body.rectTransform.offsetMax = new Vector2(-60f, -160f);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 8f;

            // Primary action centred; RunOverlay slides it left when the secondary one is shown.
            Button button = MakeButton("Button", card, font, 42f, Accent, new Vector2(0f, 48f), new Vector2(460f, 104f), gameAudio, out TextMeshProUGUI label);
            Button secondary = MakeButton("Secondary", card, font, 38f, new Color(0.45f, 0.32f, 0.26f), new Vector2(250f, 48f), new Vector2(300f, 104f), gameAudio, out TextMeshProUGUI secondaryLabel);

            var overlay = root.gameObject.AddComponent<RunOverlay>();
            SetRefs(overlay, ("group", group), ("card", card), ("title", title), ("body", body), ("button", button), ("buttonLabel", label),
                    ("secondaryButton", secondary), ("secondaryLabel", secondaryLabel));
            return overlay;
        }

        private static ShopPanel BuildShop(Transform canvas, ShopCard cardPrefab, TMP_FontAsset font, TMP_FontAsset pixelFont, GameAudio gameAudio)
        {
            RectTransform root = Rect("Shop", canvas);
            Stretch(root);
            Image dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.03f, 0.03f, 0.82f);
            var group = root.gameObject.AddComponent<CanvasGroup>();

            RectTransform card = Rect("Card", root);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1480f, 900f), new Vector2(0.5f, 0.5f));
            Panel(card.gameObject.AddComponent<Image>(), Paper);

            TextMeshProUGUI title = Text("Title", card, pixelFont, 68f, new Color(0.75f, 0.22f, 0.15f), TextAlignmentOptions.Left, bold: false);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -28f), new Vector2(-96f, 90f));
            title.SetText("Kitchen Upgrades");

            RectTransform till = Rect("Till", card);
            Place(till, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48f, -36f), new Vector2(330f, 76f), new Vector2(1f, 1f));
            Panel(till.gameObject.AddComponent<Image>(), new Color(0.2f, 0.13f, 0.12f));
            Image coin = Img("Coin", till, Placeholder("coin"), Color.white);
            Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
            TextMeshProUGUI cash = Text("Cash", till, font, 46f, new Color(1f, 0.88f, 0.4f), TextAlignmentOptions.Right, bold: true);
            Stretch(cash.rectTransform);
            cash.rectTransform.offsetMin = new Vector2(72f, 0f);
            cash.rectTransform.offsetMax = new Vector2(-18f, 0f);

            RectTransform grid = Rect("Grid", card);
            Place(grid, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(1380f, 624f), new Vector2(0.5f, 1f));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(440f, 300f);
            layout.spacing = new Vector2(22f, 24f);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;

            Button back = MakeButton("Back", card, font, 40f, new Color(0.45f, 0.32f, 0.26f), new Vector2(0f, 30f), new Vector2(360f, 96f), gameAudio, out TextMeshProUGUI backLabel);
            backLabel.SetText("Back");

            var shop = root.gameObject.AddComponent<ShopPanel>();
            SetRefs(shop, ("group", group), ("grid", grid), ("cardPrefab", cardPrefab), ("cash", cash), ("back", back));
            root.gameObject.SetActive(false);
            return shop;
        }

        /// <summary>A Food-Ping-Pong pixel button: a 9-sliced sheet sprite drawn at 4× pixel scale.</summary>
        private static Button PixelButton(string name, Transform parent, Sprite sprite, TMP_FontAsset font, float fontSize, Color textColor,
                                          Vector2 anchor, Vector2 position, Vector2 size, GameAudio gameAudio, out TextMeshProUGUI label)
        {
            RectTransform rt = Rect(name, parent);
            Place(rt, anchor, anchor, position, size);
            Image bg = rt.gameObject.AddComponent<Image>();
            PixelPanel(bg, sprite);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.9f, 0.8f);
            colors.pressedColor = new Color(0.75f, 0.65f, 0.6f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, gameAudio.PlayClick);

            label = Text("Label", rt, font, fontSize, textColor, TextAlignmentOptions.Center, bold: false);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMax = new Vector2(0f, 4f);   // the pill's shadow is at the bottom
            return button;
        }

        /// <summary>Sheet sprites are 64 PPU; 0.39 draws each source pixel as ~4 canvas units at 1080p.</summary>
        private static void PixelPanel(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 100f / (64f * 4f);
            image.color = Color.white;
        }

        private static Button MakeButton(string name, RectTransform parent, TMP_FontAsset font, float fontSize, Color color,
                                         Vector2 position, Vector2 size, GameAudio gameAudio, out TextMeshProUGUI label)
        {
            RectTransform rt = Rect(name, parent);
            Place(rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), position, size, new Vector2(0.5f, 0f));
            Image bg = rt.gameObject.AddComponent<Image>();
            Panel(bg, color);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.92f, 0.85f);
            colors.pressedColor = new Color(0.8f, 0.7f, 0.65f);
            button.colors = colors;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, gameAudio.PlayClick);

            label = Text("Label", rt, font, fontSize, Color.white, TextAlignmentOptions.Center, bold: true);
            Stretch(label.rectTransform);
            return button;
        }

        // ---------------------------------------------------------------- helpers

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static Sprite Placeholder(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtPlaceholders}/{name}.png");

        private static Sprite MartSprite(string name)
            => AssetDatabase.LoadAllAssetsAtPath($"{ArtMart}/{name}.png").OfType<Sprite>().FirstOrDefault();

        private static Sprite FppSprite(string sheet, string name)
        {
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(sheet).OfType<Sprite>().FirstOrDefault(s => s.name == name);
            if (sprite == null) Debug.LogError($"[Setup] Missing sprite {name} in {sheet}.");
            return sprite;
        }

        private static Sprite FppIcon(string name)
            => AssetDatabase.LoadAllAssetsAtPath($"{ArtFpp}/Icons/{name}.png").OfType<Sprite>().FirstOrDefault();

        /// <summary>Food-Ping-Pong's ScoreDozer pixel font, for menus and headings; falls back to the body font.</summary>
        private static TMP_FontAsset PixelFont(TMP_FontAsset fallback)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FppFontPath);
            if (font == null) { Debug.LogWarning("[Setup] ScoreDozer font missing; menus use the body font."); return fallback; }

            // ScoreDozer is ASCII only: anything else (×, —) falls through to the body font.
            if (fallback != null && !font.fallbackFontAssetTable.Contains(fallback))
            {
                font.fallbackFontAssetTable.Add(fallback);
                EditorUtility.SetDirty(font);
            }
            return font;
        }

        // Slime's Pixel Font Pack (pleeze.itch.io/slimesfonts, CC0), wherever it was unzipped under
        // Assets/Art. The first of these found wins: Sol Schori reads best at card sizes.
        private static readonly string[] TooltipFontPreference = { "Sol Schori", "Counting Apples", "Varnished", "Breadcrumbs & Flakery", "Ant Party" };

        private static string TooltipFontDir()
            => AssetDatabase.GetSubFolders("Assets/Art")
                .FirstOrDefault(f => Path.GetFileName(f).IndexOf("slime", System.StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>
        /// The upgrade cards' text font, from the Slime pack's folder: a TextMeshPro font asset if
        /// one is there, otherwise one generated from its .ttf (point-sampled raster, so it stays
        /// crisp). Falls back to the body font.
        /// </summary>
        private static TMP_FontAsset TooltipFont(TMP_FontAsset fallback)
        {
            string dir = TooltipFontDir();
            if (dir == null) return fallback;

            string[] tmp = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            string[] ttf = AssetDatabase.FindAssets("t:Font", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".ttf", System.StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", System.StringComparison.OrdinalIgnoreCase)).ToArray();

            // An exact name first ("Sol Schori", not "Sol Schori Bold"), then any file containing it.
            string Preferred(string[] paths)
            {
                foreach (string name in TooltipFontPreference)
                {
                    string Stem(string p) => Path.GetFileNameWithoutExtension(p).Replace(" SDF", "");
                    string hit = paths.FirstOrDefault(p => Stem(p).Equals(name, System.StringComparison.OrdinalIgnoreCase))
                              ?? paths.FirstOrDefault(p => Stem(p).IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hit != null) return hit;
                }
                return paths.OrderBy(p => p, System.StringComparer.Ordinal).FirstOrDefault();
            }

            TMP_FontAsset font = null;
            string chosen = Preferred(tmp);
            if (chosen != null) font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(chosen);
            else if ((chosen = Preferred(ttf)) != null) font = GenerateFontAsset(chosen);

            if (font == null) { Debug.LogWarning($"[Setup] No usable font in {dir}; upgrade cards use the body font."); return fallback; }

            if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
            if (fallback != null && !font.fallbackFontAssetTable.Contains(fallback))
            {
                font.fallbackFontAssetTable.Add(fallback);   // for anything the pixel font lacks (×, —)
                EditorUtility.SetDirty(font);
            }
            Debug.Log($"[Setup] Upgrade card font: {chosen}");
            return font;
        }

        private static TMP_FontAsset GenerateFontAsset(string ttfPath)
        {
            string assetPath = Path.ChangeExtension(ttfPath, null) + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(ttfPath);
            if (source == null) return null;

            // Hinted raster, not SDF: a pixel font wants hard edges. Dynamic, so any glyph used is added.
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 32, 2, UnityEngine.TextCore.LowLevel.GlyphRenderMode.RASTER_HINTED,
                                                               512, 512, AtlasPopulationMode.Dynamic);
            if (font == null) return null;

            font.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(font, assetPath);
            font.atlasTextures[0].name = font.name + " Atlas";
            font.atlasTextures[0].filterMode = FilterMode.Point;
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
            font.material.name = font.name + " Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            font.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~");
            AssetDatabase.SaveAssets();
            return font;
        }

        private static TMP_FontAsset Font()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontPath);
            if (font == null) Debug.LogError("[Setup] TMP Essential Resources are missing — run step 1 first.");
            return font;
        }

        private static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach ((string field, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(field);
                if (p == null) { Debug.LogError($"[Setup] {target.GetType().Name} has no field '{field}'."); continue; }
                p.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;   // UI
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot ?? anchorMin;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void Panel(Image image, Color color)
        {
            image.sprite = Placeholder("panel");
            image.type = Image.Type.Sliced;
            image.color = color;
        }

        private static Image Img(string name, Transform parent, Sprite sprite, Color color)
        {
            Image img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color,
                                            TextAlignmentOptions align, bool bold)
        {
            var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
    }
}

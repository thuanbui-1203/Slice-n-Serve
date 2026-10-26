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

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var knifeMat = PhysicsMaterial(SettingsDir + "/KnifeMaterial.physicsMaterial2D");
            var wallMat = PhysicsMaterial(SettingsDir + "/WallMaterial.physicsMaterial2D");

            BoardConfig config = BuildData(out IngredientDefinition[] ingredients, out DishDefinition[] dishes, out CustomerDefinition[] customers);

            KnifeController knifePrefab = BuildKnifePrefab(knifeMat);
            Ingredient ingredientPrefab = BuildIngredientPrefab();
            OrderTicketView ticketPrefab = BuildTicketPrefab();

            BuildScene(scene, config, wallMat, ingredients, dishes, customers, knifePrefab, ingredientPrefab, ticketPrefab);

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

        private static readonly string[] UsedMartSprites =
        {
            "potato", "bell_pepper", "mushroom_white", "white_cheese_piece", "cabbage", "egg_white",
            "bowl", "teakettle", "frying pan", "chopping board"
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
            group.blocksRaycasts = false;
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

        // ---------------------------------------------------------------- scene

        private static void BuildScene(UnityEngine.SceneManagement.Scene scene, BoardConfig config, PhysicsMaterial2D wallMat,
                                       IngredientDefinition[] ingredients, DishDefinition[] dishes, CustomerDefinition[] customers,
                                       KnifeController knifePrefab, Ingredient ingredientPrefab, OrderTicketView ticketPrefab)
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

            var audioGo = new GameObject("Audio");
            var audioSource = audioGo.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            var gameAudio = audioGo.AddComponent<GameAudio>();
            SetRefs(gameAudio, ("source", audioSource));

            (OrderTicketHud hud, RunOverlay overlay) = BuildHud(cam, ticketPrefab, font, gameAudio);

            var runner = new GameObject("Runner").AddComponent<GameRunner>();
            SetRefs(runner,
                ("bootstrap", bootstrap), ("bounds", bounds), ("ingredients", spawner), ("knives", knives),
                ("aimInput", aim), ("trajectoryPreview", preview), ("orderSpawner", orders), ("boardFx", fx),
                ("hud", hud), ("overlay", overlay), ("gameAudio", gameAudio));

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static (OrderTicketHud, RunOverlay) BuildHud(Camera cam, OrderTicketView ticketPrefab, TMP_FontAsset font, GameAudio gameAudio)
        {
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

            // Ticket strip: top-left, left→right in spawn order.
            RectTransform strip = Rect("TicketStrip", safe);
            Place(strip, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(1300f, 196f));
            var row = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14f;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

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

            // First-run hint in the launch corridor, bottom-left.
            RectTransform hintRt = Rect("Hint", safe);
            Place(hintRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 18f), new Vector2(760f, 70f), new Vector2(0f, 0f));
            var hint = hintRt.gameObject.AddComponent<CanvasGroup>();
            hint.blocksRaycasts = false;
            TextMeshProUGUI hintText = Text("Text", hintRt, font, 32f, Color.white, TextAlignmentOptions.BottomLeft, bold: true);
            Stretch(hintText.rectTransform);
            hintText.SetText("Drag back and release to throw the knife");
            hintText.outlineWidth = 0.2f;
            hintText.outlineColor = new Color32(40, 20, 20, 255);

            RectTransform effects = Rect("Effects", safe);
            Stretch(effects);

            var hud = canvasGo.AddComponent<OrderTicketHud>();
            SetRefs(hud,
                ("container", strip), ("ticketPrefab", ticketPrefab), ("cash", cash), ("reputation", reputation),
                ("hint", hint), ("boardCamera", cam), ("effectsRoot", effects), ("font", font));

            RunOverlay overlay = BuildOverlay(canvasGo.transform, font, gameAudio);

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            return (hud, overlay);
        }

        private static RunOverlay BuildOverlay(Transform canvas, TMP_FontAsset font, GameAudio gameAudio)
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

            TextMeshProUGUI title = Text("Title", card, font, 84f, new Color(0.75f, 0.22f, 0.15f), TextAlignmentOptions.Center, bold: true);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -36f), new Vector2(-60f, 110f), new Vector2(0.5f, 1f));

            TextMeshProUGUI body = Text("Body", card, font, 32f, Ink, TextAlignmentOptions.Center, bold: false);
            Place(body.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            body.rectTransform.offsetMin = new Vector2(60f, 170f);
            body.rectTransform.offsetMax = new Vector2(-60f, -160f);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 8f;

            RectTransform buttonRt = Rect("Button", card);
            Place(buttonRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(480f, 104f), new Vector2(0.5f, 0f));
            Image buttonBg = buttonRt.gameObject.AddComponent<Image>();
            Panel(buttonBg, Accent);
            var button = buttonRt.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonBg;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.92f, 0.85f);
            colors.pressedColor = new Color(0.8f, 0.7f, 0.65f);
            button.colors = colors;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, gameAudio.PlayClick);

            TextMeshProUGUI label = Text("Label", buttonRt, font, 42f, Color.white, TextAlignmentOptions.Center, bold: true);
            Stretch(label.rectTransform);

            var overlay = root.gameObject.AddComponent<RunOverlay>();
            SetRefs(overlay, ("group", group), ("card", card), ("title", title), ("body", body), ("button", button), ("buttonLabel", label));
            return overlay;
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

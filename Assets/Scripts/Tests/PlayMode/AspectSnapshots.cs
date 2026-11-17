using System.Collections;
using System.IO;
using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SliceNServe.Tests
{
    /// <summary>
    /// The §1 aspect matrix as pictures: renders the live scene at each target resolution, mid-aim
    /// and mid-flight, into PNGs for a human to look at. Explicit, so it never runs in the normal
    /// suite. Needs a graphics device (no -nographics):
    ///
    ///   Unity -batchmode -projectPath . -runTests -testPlatform PlayMode
    ///         -testFilter SliceNServe.Tests.AspectSnapshots -testResults snapshots.xml
    ///
    /// Output goes to $SNAPSHOT_DIR, or Logs/Snapshots.
    /// </summary>
    [Explicit("Writes screenshots for manual review; run with -testFilter.")]
    public sealed class AspectSnapshots
    {
        [UnityTest]
        [TestCase(1920, 1080, "pc_16x9_fhd", ExpectedResult = null)]
        [TestCase(2400, 1080, "phone_20x9", ExpectedResult = null)]
        [TestCase(2048, 1536, "tablet_4x3", ExpectedResult = null)]
        [TestCase(1920, 1200, "pc_16x10", ExpectedResult = null)]
        [TestCase(3840, 1080, "ultrawide_32x9", ExpectedResult = null)]
        [TestCase(1280, 720, "editor_720p", ExpectedResult = null)]     // a typical Editor Game view
        [TestCase(960, 540, "editor_small", ExpectedResult = null)]    // a docked, smaller Game view
        public IEnumerator Capture(int width, int height, string label)
        {
            string dir = System.Environment.GetEnvironmentVariable("SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.dataPath, "..", "Logs", "Snapshots");
            Directory.CreateDirectory(dir);

            SaveService.StoreOverride = new MemorySaveStore();   // never the player's real save
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            Camera cam = Camera.main;
            ConfigureCamera(cam, rt, width, height);

            var pointer = new VirtualPointer();
            try
            {
                var runner = Object.FindAnyObjectByType<GameRunner>();
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_1_title.png"));

                var settings = Object.FindAnyObjectByType<SettingsPanel>(FindObjectsInactive.Include);
                settings.Open(null);
                for (int i = 0; i < 20; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_0_settings.png"));
                settings.Close();
                for (int i = 0; i < 20; i++) yield return null;

                var chooser = Object.FindAnyObjectByType<VersusChooser>(FindObjectsInactive.Include);
                chooser.Open(null, null, null);
                for (int i = 0; i < 5; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_0_chooser.png"));
                chooser.gameObject.SetActive(false);
                var lobby = Object.FindAnyObjectByType<PvpLobby>(FindObjectsInactive.Include);
                lobby.Open(null);
                for (int i = 0; i < 5; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_0_lobby.png"));
                lobby.gameObject.SetActive(false);

                // The shop mid-progression: some cash, a few levels bought, one maxed-out row.
                ServiceRegistry services = ServiceRegistry.Current;
                services.Wallet.Add(2000);
                foreach (var upgrade in services.Upgrades.Catalog)
                    for (int n = 0; n < (upgrade.Kind == Data.UpgradeKind.KnifeSpeed ? 5 : 2); n++) services.Upgrades.TryBuy(upgrade);
                var shop = Object.FindAnyObjectByType<ShopPanel>(FindObjectsInactive.Include);
                shop.Open(null);
                for (int i = 0; i < 20; i++) yield return null;   // fade in
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_0_shop.png"));
                shop.Close();
                for (int i = 0; i < 20; i++) yield return null;

                runner.StartRun();
                Time.timeScale = 3f;
                for (int i = 0; i < 90; i++) yield return null;   // let tickets arrive and patience drain
                Time.timeScale = 1f;

                Vector2 start = cam.WorldToScreenPoint(new Vector2(3f, 1f));
                Vector2 end = cam.WorldToScreenPoint(new Vector2(1.6f, -1.6f));
                pointer.Mouse(start, false);
                pointer.Mouse(start, true);
                yield return null;
                pointer.Mouse(end, true);
                for (int i = 0; i < 5; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_2_aiming.png"));

                pointer.Mouse(end, false);
                for (int i = 0; i < 25; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_3_flying.png"));

                Time.timeScale = 2f;
                for (int i = 0; i < 120; i++) yield return null;
                Time.timeScale = 1f;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_4_later.png"));

                // A finished order waiting to be served, and the orders panel open over the line.
                if (runner.Queue.ActiveCount > 0)
                {
                    Orders.OrderInstance order = runner.Queue[0];
                    for (int l = 0; l < order.Lines.Length; l++)
                        for (int n = order.Lines[l].Fulfilled; n < order.Lines[l].Required; n++)
                            ServiceRegistry.Current.Events.Publish(new IngredientSliced(order.Lines[l].Ingredient, Vector2.zero, 1, 0, 1));
                }
                for (int i = 0; i < 30; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_5_ready.png"));
                runner.ToggleOrders();
                for (int i = 0; i < 30; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_6_orders.png"));
                runner.ToggleOrders();

                // The end-of-run card, as the results show it (fonts match the shop's).
                var overlay = Object.FindAnyObjectByType<RunOverlay>(FindObjectsInactive.Include);
                overlay.Show("Kitchen closed!",
                    "Your reputation ran out.\n\nOrders served: <color=#B5381F>12</color>  (best 30)\n"
                  + "Cash earned: <color=#B5381F>$480</color>  |  In the till: <color=#B5381F>$1229</color>\n"
                  + "Best combo: <color=#B5381F>x7</color>  |  Order level reached: <color=#B5381F>3</color>",
                    "Play again", () => { }, "Main menu", () => { });
                for (int i = 0; i < 30; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_7_results.png"));
                overlay.Show("Paused", "The customers will wait. For now.", "Resume", () => { }, "Back to Main Menu", () => { });
                for (int i = 0; i < 10; i++) yield return null;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_7_paused.png"));

                // §5 Play with Friends: two boards on one screen, mid-match.
                Pvp.MatchSetup.BeginLocal(42, 60f);
                SceneManager.LoadScene("Game");
                yield return null;
                yield return null;
                cam = Camera.main;
                ConfigureCamera(cam, rt, width, height);
                var pvp = Object.FindAnyObjectByType<Pvp.PvpRunner>();
                pvp.BeginPlay();
                Time.timeScale = 3f;
                for (int i = 0; i < 60; i++) yield return null;
                pvp.KnivesOf(0).Launch(new Vector2(0.4f, 1f).normalized * 18f, LaunchSource.Manual);
                pvp.KnivesOf(1).Launch(new Vector2(-0.5f, 1f).normalized * 18f, LaunchSource.Manual);
                for (int i = 0; i < 30; i++) yield return null;
                Time.timeScale = 1f;
                yield return Render(cam, rt, Path.Combine(dir, $"{label}_8_versus.png"));
                Pvp.MatchSetup.Clear();
            }
            finally
            {
                pointer.Dispose();
                Time.timeScale = 1f;
                Pvp.MatchSetup.Clear();
            }
        }

        /// <summary>
        /// Batch mode has no real screen, so the camera renders to a texture of the target size.
        /// The adapter would read Screen, so its policy is applied for the texture instead, and
        /// the HUD is moved onto the camera so it lands in the same image.
        /// </summary>
        private static void ConfigureCamera(Camera cam, RenderTexture rt, int width, int height)
        {
            cam.GetComponent<BoardViewportAdapter>().enabled = false;
            cam.targetTexture = rt;

            float aspect = (float)width / height;
            float effective = ViewportMath.EffectiveAspect(aspect);
            cam.orthographicSize = ViewportMath.OrthographicSize(9.6f, 5.4f, effective);
            cam.rect = ViewportMath.LetterboxRect(aspect, effective);

            var hud = Object.FindAnyObjectByType<OrderTicketHud>(FindObjectsInactive.Include).GetComponentInParent<Canvas>(true).rootCanvas;
            hud.renderMode = RenderMode.ScreenSpaceCamera;
            hud.worldCamera = cam;
            hud.planeDistance = 1f;
            hud.sortingOrder = 100;   // above every world sprite, as Screen Space Overlay is

            var safe = Object.FindAnyObjectByType<SafeAreaFitter>();
            safe.enabled = false;
            var rect = (RectTransform)safe.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static IEnumerator Render(Camera cam, RenderTexture rt, string path)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();

            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            Debug.Log($"[Snapshot] {path}");
        }
    }
}

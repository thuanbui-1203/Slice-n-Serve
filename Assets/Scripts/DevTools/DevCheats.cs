using SliceNServe.Board;
using SliceNServe.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SliceNServe.DevTools
{
    /// <summary>
    /// Developer buttons: add money, reset progress. Lives in its own assembly that no game code
    /// references, and builds its own little canvas at runtime, so it is fully detachable:
    /// delete the GameObject, or the whole <c>Assets/Scripts/DevTools</c> folder, and the game is
    /// unchanged (Build Everything adds it only if the type exists).
    ///
    /// Only in the Editor and development builds: in a release build it removes itself on start.
    /// </summary>
    public sealed class DevCheats : MonoBehaviour
    {
        [SerializeField, Min(1)] private int moneyAmount = 1000;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const float ConfirmSeconds = 3f;

        private TextMeshProUGUI _resetLabel;
        private float _confirmUntil = -1f;

        private void Start()
        {
            var canvasGo = new GameObject("DevCheatsCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;   // above the HUD and its panels
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(canvasGo.transform, false);
            bar.anchorMin = bar.anchorMax = bar.pivot = Vector2.zero;
            bar.anchoredPosition = new Vector2(12f, 12f);
            bar.sizeDelta = new Vector2(400f, 52f);

            Button(bar, "AddMoney", new Vector2(0f, 0f), new Color(0.2f, 0.5f, 0.25f, 0.9f), $"DEV +${moneyAmount}", AddMoney);
            _resetLabel = Button(bar, "ResetProgress", new Vector2(204f, 0f), new Color(0.6f, 0.18f, 0.15f, 0.9f), "DEV Reset", ResetProgress);

            // A press on these must never start a throw or open the orders panel underneath.
            AimInput aim = FindAnyObjectByType<AimInput>();
            if (aim != null) aim.AddBlocker(bar);
        }

        private void Update()
        {
            if (_confirmUntil >= 0f && Time.unscaledTime > _confirmUntil)
            {
                _confirmUntil = -1f;
                _resetLabel.SetText("DEV Reset");
            }
        }

        private void AddMoney()
        {
            ServiceRegistry services = ServiceRegistry.Current;
            if (services == null) return;

            services.Wallet.Add(moneyAmount);
            services.Save.Save();
            Debug.Log($"[DevCheats] +${moneyAmount} (now ${services.Wallet.Amount}).");

            // Mid-run the HUD follows the wallet; the menu reads it once, so reopen it fresh.
            GameRunner runner = FindAnyObjectByType<GameRunner>();
            if (runner == null || runner.State == GameRunner.RunState.Title) Reload();
        }

        /// <summary>Two taps: the first asks, the second wipes the save and restarts at the menu.</summary>
        private void ResetProgress()
        {
            if (_confirmUntil < 0f)
            {
                _confirmUntil = Time.unscaledTime + ConfirmSeconds;
                _resetLabel.SetText("Tap again!");
                return;
            }

            ServiceRegistry services = ServiceRegistry.Current;
            if (services == null) return;
            services.Save.Reset();
            Debug.Log("[DevCheats] Progress reset.");
            Reload();
        }

        private void Reload()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        private static TextMeshProUGUI Button(RectTransform parent, string name, Vector2 at, Color color, string text,
                                              UnityEngine.Events.UnityAction onClick)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = at;
            rt.sizeDelta = new Vector2(196f, 52f);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(rt, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.fontSize = 24f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.SetText(text);
            return label;
        }
#else
        // Release build: no cheats, and nothing left running.
        private void Awake() => Destroy(gameObject);
#endif
    }
}

using UnityEngine;

namespace SliceNServe.UI
{
    /// <summary>
    /// Insets this RectTransform to Screen.safeArea so children clear notches and rounded
    /// corners. In landscape the notch sits on the *left or right* edge, so the horizontal
    /// insets are the ones that actually bite.
    ///
    /// One component on the HUD root. Individual layouts must never each solve this themselves.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreen;

        private void Awake() => _rect = (RectTransform)transform;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (Screen.safeArea != _lastSafeArea ||
                Screen.width != _lastScreen.x ||
                Screen.height != _lastScreen.y)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            if (Screen.width <= 0 || Screen.height <= 0) return;

            _lastSafeArea = Screen.safeArea;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);

            // Screen Space - Overlay + Scale With Screen Size means canvas-normalised
            // coordinates are identical to screen-normalised ones.
            Vector2 min = _lastSafeArea.position;
            Vector2 max = _lastSafeArea.position + _lastSafeArea.size;
            min.x /= Screen.width;  min.y /= Screen.height;
            max.x /= Screen.width;  max.y /= Screen.height;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}

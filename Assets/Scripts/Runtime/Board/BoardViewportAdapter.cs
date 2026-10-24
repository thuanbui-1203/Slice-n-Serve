using SliceNServe.Core;
using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Applies the Section 1 aspect policy to the board camera: contain-fit the board box,
    /// clamp to [4:3 .. 21:9], letterbox outside it.
    ///
    /// This is the ONLY component that writes Camera.orthographicSize or Camera.rect. Section 2,
    /// §4.4 specifies orthographicSize = 5.4 for Game.unity's camera; that value is exactly what
    /// this component computes at 16:9, so it is the reference, not a competing source of truth.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public sealed class BoardViewportAdapter : MonoBehaviour
    {
        /// <summary>Used only when no BoardConfig is assigned, so the Scene view still frames
        /// the board correctly before the asset is wired up.</summary>
        private static readonly Rect FallbackBounds = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);

        [SerializeField] private BoardConfig boardConfig;

        private Camera _camera;
        private int _lastWidth = -1;
        private int _lastHeight = -1;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            Apply();
        }

        private void LateUpdate()
        {
            if (Screen.width == _lastWidth && Screen.height == _lastHeight) return;
            Apply();
        }

        public void Apply()
        {
            if (_camera == null) _camera = GetComponent<Camera>();

            // Guard against a zero height during a window resize, and in headless builds.
            if (Screen.height <= 0 || Screen.width <= 0) return;

            _lastWidth = Screen.width;
            _lastHeight = Screen.height;

            Rect bounds = boardConfig != null ? boardConfig.worldBounds : FallbackBounds;

            // Keep the board centred without touching the camera's z (which sets its depth).
            Vector3 position = transform.position;
            if (position.x != bounds.center.x || position.y != bounds.center.y)
                transform.position = new Vector3(bounds.center.x, bounds.center.y, position.z);

            float screenAspect = (float)Screen.width / Screen.height;
            float effectiveAspect = ViewportMath.EffectiveAspect(screenAspect);

            _camera.orthographic = true;
            _camera.orthographicSize = ViewportMath.OrthographicSize(
                bounds.width * 0.5f, bounds.height * 0.5f, effectiveAspect);
            _camera.rect = ViewportMath.LetterboxRect(screenAspect, effectiveAspect);
        }
    }
}

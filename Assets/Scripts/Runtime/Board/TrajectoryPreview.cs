using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Draws the first N wall bounces as a dotted path while the player is dragging, and points
    /// the waiting knife along it.
    ///
    /// This is a *preview*, not a simulation: it must never call Physics2D.Simulate, and it
    /// deliberately ignores ingredients so it cannot lie about what the knife will hit. If you
    /// find yourself "fixing" it into a real prediction, that is a design change, not a bug fix.
    /// </summary>
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        private const int MaxDots = 72;

        [Tooltip("Geometric preview only: wall reflections, ignores ingredients by design.")]
        [SerializeField, Range(0, 6)] private int previewBounces = 2;
        [SerializeField] private Sprite dotSprite;
        [SerializeField] private float dotSpacing = 0.32f;
        [SerializeField] private float dotSize = 0.13f;
        [SerializeField] private float maxPreviewLength = 22f;
        [SerializeField] private float knifeRadius = 0.18f;
        [SerializeField] private Color weakColor = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color strongColor = new Color(1f, 0.55f, 0.2f, 0.95f);

        [Tooltip("The knife shown waiting at the launcher, aimed along the preview.")]
        [SerializeField] private SpriteRenderer launcherKnife;
        [SerializeField] private SpriteRenderer launcherBase;

        private readonly SpriteRenderer[] _dots = new SpriteRenderer[MaxDots];
        private readonly Vector2[] _corners = new Vector2[8];

        private AimInput _input;
        private RuntimeTuning _tuning;
        private KnifeManager _knives;
        private int _wallMask;
        private float _idleAngle = 90f;

        public void Initialize(AimInput input, RuntimeTuning tuning, KnifeManager knives)
        {
            _input = input;
            _tuning = tuning;
            _knives = knives;
            _wallMask = 1 << LayerMask.NameToLayer("BoardWall");

            for (int i = 0; i < MaxDots; i++)
            {
                var go = new GameObject("Dot");
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * (dotSize / Mathf.Max(0.01f, dotSprite.bounds.size.x));
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = dotSprite;
                sr.sortingOrder = 30;
                sr.enabled = false;
                _dots[i] = sr;
            }

            Vector2 launcher = tuning.LauncherPosition;
            if (launcherKnife != null) launcherKnife.transform.position = launcher;
            if (launcherBase != null) launcherBase.transform.position = launcher;
        }

        private void LateUpdate()
        {
            if (_input == null) return;

            bool ready = _knives.CanLaunch;
            Vector2 drag = _input.CurrentDragWorld;
            bool aiming = ready && _input.IsDragging && KnifeLauncher.IsValidDrag(drag, _tuning);

            int used = 0;
            float angle = _idleAngle + Mathf.Sin(Time.time * 2.5f) * 6f;

            if (aiming)
            {
                Vector2 dir = KnifeLauncher.ResolveDirection(drag);
                float power = KnifeLauncher.ResolvePower(drag, _tuning);
                used = DrawPath(dir, power);
                angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            }

            for (int i = used; i < MaxDots; i++) _dots[i].enabled = false;

            if (launcherKnife != null)
            {
                launcherKnife.enabled = ready;
                launcherKnife.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
            if (launcherBase != null)
                launcherBase.color = ready ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.12f);
        }

        private int DrawPath(Vector2 dir, float power)
        {
            // Corners of the reflected path: a circle cast of the knife's own radius, so the
            // preview bends where the knife's centre will, not where a ray would.
            Vector2 p = _tuning.LauncherPosition;
            int cornerCount = 0;
            _corners[cornerCount++] = p;

            float remaining = maxPreviewLength;
            for (int i = 0; i <= previewBounces && remaining > 0f; i++)
            {
                RaycastHit2D hit = Physics2D.CircleCast(p, knifeRadius, dir, remaining, _wallMask);
                if (!hit)
                {
                    p += dir * remaining;
                    _corners[cornerCount++] = p;
                    break;
                }

                remaining -= hit.distance;
                p = hit.centroid + hit.normal * 0.01f;   // step off the surface to avoid re-hitting
                _corners[cornerCount++] = p;
                dir = Vector2.Reflect(dir, hit.normal);
            }

            Color color = Color.Lerp(weakColor, strongColor, power);
            float total = maxPreviewLength - Mathf.Max(0f, remaining);
            float travelled = 0f;
            float offset = (Time.time * 1.6f) % dotSpacing;   // dots crawl along the path
            int used = 0;

            for (int c = 1; c < cornerCount && used < MaxDots; c++)
            {
                Vector2 a = _corners[c - 1], b = _corners[c];
                float len = Vector2.Distance(a, b);
                for (float s = offset; s < len && used < MaxDots; s += dotSpacing)
                {
                    float along = (travelled + s) / Mathf.Max(0.01f, total);
                    SpriteRenderer dot = _dots[used++];
                    dot.enabled = true;
                    dot.transform.position = Vector2.Lerp(a, b, s / len);
                    color.a = Mathf.Lerp(0.95f, 0.05f, along);   // fade with distance
                    dot.color = color;
                }
                travelled += len;
                offset = dotSpacing - ((len - offset) % dotSpacing);
            }

            return used;
        }
    }
}

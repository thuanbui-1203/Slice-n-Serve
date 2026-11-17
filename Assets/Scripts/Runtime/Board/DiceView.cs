using TMPro;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// The die beside the launcher. It tumbles when rolled and lands on how many times the
    /// player's next knife may bounce off the border; while that knife flies it counts the
    /// bounces down. Purely a view: the roll itself comes from <c>GameRandom</c> and is already
    /// decided when the tumble starts. Procedural animation, as elsewhere — optional roll frames
    /// from an art pack play during the tumble, otherwise random faces flicker past.
    /// </summary>
    public sealed class DiceView : MonoBehaviour
    {
        public const float RollSeconds = 0.6f;

        [Tooltip("Faces 1 to 6, in order.")]
        [SerializeField] private Sprite[] faces;
        [Tooltip("Optional tumbling frames, played while rolling.")]
        [SerializeField] private Sprite[] rollFrames;
        [SerializeField] private SpriteRenderer face;
        [SerializeField] private SpriteRenderer shadow;
        [SerializeField] private TextMeshPro label;
        [SerializeField, Min(0.1f)] private float size = 0.85f;

        private float _rollAge = -1f, _punch, _frameTimer;
        private int _value = 1, _shown = 1;
        private bool _dim;

        public bool IsRolling => _rollAge >= 0f && _rollAge < RollSeconds;
        public int Value => _value;

        private void Awake() => SetFace(_value);

        /// <summary>Tumbles, then lands on <paramref name="value"/> (1–6).</summary>
        public void Roll(int value)
        {
            _value = Mathf.Clamp(value, 1, 6);
            _rollAge = 0f;
            _dim = false;
            if (label != null) label.SetText("");
        }

        /// <summary>While the knife flies: the bounces it has left. Zero greys the die out.</summary>
        public void ShowRemaining(int remaining)
        {
            if (IsRolling) return;
            _dim = remaining <= 0;
            if (remaining > 0 && remaining != _shown) { SetFace(remaining); _punch = 0.6f; }
            if (label != null)
            {
                if (remaining > 1) label.SetText("{0} left", remaining);
                else if (remaining == 1) label.SetText("last bounce!");
                else label.SetText("");
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 offset = Vector3.zero;
            float angle = 0f;

            if (_rollAge >= 0f)
            {
                _rollAge += dt;
                float t = Mathf.Clamp01(_rollAge / RollSeconds);
                if (t < 1f)
                {
                    // Two hops, spinning, the face flickering until it settles.
                    offset.y = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * 0.45f * (1f - t);
                    angle = (1f - t) * (1f - t) * 720f;
                    _frameTimer -= dt;
                    if (_frameTimer <= 0f)
                    {
                        _frameTimer = 0.06f;
                        if (rollFrames != null && rollFrames.Length > 0)
                            face.sprite = rollFrames[Mathf.FloorToInt(_rollAge / 0.06f) % rollFrames.Length];
                        else SetFace(Random.Range(1, 7));
                    }
                }
                else
                {
                    _rollAge = -1f;
                    SetFace(_value);
                    _punch = 1f;
                    if (label != null) label.SetText(_value == 1 ? "1 bounce" : "{0} bounces", _value);
                }
            }

            _punch = Mathf.MoveTowards(_punch, 0f, dt * 3f);
            float squash = 1f + 0.25f * Mathf.Sin(_punch * Mathf.PI);
            if (face != null)
            {
                face.transform.localPosition = offset;
                face.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                float s = size / SpriteSize(face.sprite);
                face.transform.localScale = new Vector3(s * squash, s / squash, 1f);
                face.color = _dim ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            }
            if (shadow != null)
            {
                float w = size * (1f - offset.y * 0.8f) / SpriteSize(shadow.sprite);
                shadow.transform.localScale = new Vector3(w, w * 0.3f, 1f);
            }
        }

        private void SetFace(int value)
        {
            _shown = value;
            if (face != null && faces != null && faces.Length >= value) face.sprite = faces[value - 1];
        }

        private static float SpriteSize(Sprite s)
            => s != null ? Mathf.Max(0.01f, Mathf.Max(s.bounds.size.x, s.bounds.size.y)) : 1f;
    }
}

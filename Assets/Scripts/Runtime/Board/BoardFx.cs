using SliceNServe.Core;
using TMPro;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Slice, bounce and waste feedback on the board, driven entirely by events. Everything is
    /// pooled at start, so a run never instantiates and the effects cost no garbage.
    /// Purely presentational: removing this component changes nothing about the game.
    /// </summary>
    public sealed class BoardFx : MonoBehaviour
    {
        private const int ParticleCount = 160;
        private const int RingCount = 12;
        private const int PopupCount = 8;

        [SerializeField] private Sprite particleSprite;
        [SerializeField] private Sprite ringSprite;
        [SerializeField] private TMP_FontAsset popupFont;

        private struct Particle
        {
            public Vector2 Velocity;
            public float Age, Life, Size, Spin;
            public Color Color;
        }

        private struct Pop
        {
            public float Age, Life, Size;
            public Color Color;
        }

        private readonly SpriteRenderer[] _particleViews = new SpriteRenderer[ParticleCount];
        private readonly Particle[] _particles = new Particle[ParticleCount];
        private readonly SpriteRenderer[] _ringViews = new SpriteRenderer[RingCount];
        private readonly Pop[] _rings = new Pop[RingCount];
        private readonly TextMeshPro[] _popupViews = new TextMeshPro[PopupCount];
        private readonly Pop[] _popups = new Pop[PopupCount];

        private EventBus _events;
        private int _nextParticle, _nextRing, _nextPopup;
        private float _nextWasteText;

        public void Initialize(EventBus events)
        {
            _events = events;

            for (int i = 0; i < ParticleCount; i++)
            {
                _particleViews[i] = CreateSprite("Particle", particleSprite, 40);
                _particles[i].Life = -1f;
            }
            for (int i = 0; i < RingCount; i++)
            {
                _ringViews[i] = CreateSprite("Ring", ringSprite, 39);
                _rings[i].Life = -1f;
            }
            for (int i = 0; i < PopupCount; i++)
            {
                var go = new GameObject("Popup");
                go.transform.SetParent(transform, false);
                TextMeshPro text = go.AddComponent<TextMeshPro>();
                if (popupFont != null) text.font = popupFont;
                text.alignment = TextAlignmentOptions.Center;
                text.fontStyle = FontStyles.Bold;
                text.outlineWidth = 0.25f;
                text.outlineColor = new Color32(30, 20, 20, 255);
                text.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
                text.sortingOrder = 50;
                go.SetActive(false);
                _popupViews[i] = text;
                _popups[i].Life = -1f;
            }

            _events.Subscribe<IngredientSliced>(OnSliced);
            _events.Subscribe<IngredientWasted>(OnWasted);
            _events.Subscribe<KnifeBounced>(OnBounced);
        }

        private void OnDestroy()
        {
            if (_events == null) return;

            _events.Unsubscribe<IngredientSliced>(OnSliced);
            _events.Unsubscribe<IngredientWasted>(OnWasted);
            _events.Unsubscribe<KnifeBounced>(OnBounced);
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.enabled = false;
            return sr;
        }

        private void OnSliced(IngredientSliced e)
        {
            Color juice = e.Ingredient != null ? e.Ingredient.JuiceColor : Color.white;
            Burst(e.Position, juice, 12, 3.5f, 0.16f);
            Burst(e.Position, Color.white, 4, 2f, 0.1f);
            Ring(e.Position, juice, 1.6f);

            if (e.Combo >= 3) Popup(e.Position + Vector2.up * 1.1f, $"Combo x{e.Combo}!", new Color(1f, 0.85f, 0.3f), 5f);
        }

        private void OnWasted(IngredientWasted e)
        {
            Burst(e.Position, new Color(0.55f, 0.55f, 0.55f, 0.9f), 8, 1.5f, 0.18f);
            // Once helpers and split knives are flying, waste happens constantly: the puff always
            // plays, but the words are rate-limited so they stay a hint rather than noise.
            if (Time.time < _nextWasteText) return;
            _nextWasteText = Time.time + 1.5f;
            Popup(e.Position + Vector2.up * 0.45f, "not needed", new Color(0.8f, 0.8f, 0.8f), 3.2f);
        }

        private void OnBounced(KnifeBounced e)
        {
            Burst(e.Position, new Color(1f, 0.95f, 0.7f), 5, 2.5f, 0.08f);
        }

        private void Burst(Vector2 at, Color color, int count, float speed, float size)
        {
            for (int n = 0; n < count; n++)
            {
                int i = _nextParticle;
                _nextParticle = (_nextParticle + 1) % ParticleCount;

                float a = Random.value * Mathf.PI * 2f;
                _particles[i] = new Particle
                {
                    Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (speed * Random.Range(0.4f, 1f)),
                    Age = 0f,
                    Life = Random.Range(0.25f, 0.5f),
                    Size = size * Random.Range(0.7f, 1.3f),
                    Spin = Random.Range(-720f, 720f),
                    Color = color
                };
                SpriteRenderer view = _particleViews[i];
                view.enabled = true;
                view.transform.position = at;
            }
        }

        private void Ring(Vector2 at, Color color, float size)
        {
            int i = _nextRing;
            _nextRing = (_nextRing + 1) % RingCount;
            _rings[i] = new Pop { Age = 0f, Life = 0.3f, Size = size, Color = color };
            _ringViews[i].enabled = true;
            _ringViews[i].transform.position = at;
        }

        private void Popup(Vector2 at, string message, Color color, float size)
        {
            int i = _nextPopup;
            _nextPopup = (_nextPopup + 1) % PopupCount;
            _popups[i] = new Pop { Age = 0f, Life = 0.9f, Size = size, Color = color };

            TextMeshPro view = _popupViews[i];
            view.gameObject.SetActive(true);
            view.transform.position = at;
            view.text = message;
            view.fontSize = size;
        }

        private void Update()
        {
            if (_events == null) return;   // not initialised: the pools are empty

            float dt = Time.deltaTime;

            for (int i = 0; i < ParticleCount; i++)
            {
                if (_particles[i].Life < 0f) continue;

                ref Particle p = ref _particles[i];
                p.Age += dt;
                SpriteRenderer view = _particleViews[i];
                if (p.Age >= p.Life) { p.Life = -1f; view.enabled = false; continue; }

                float t = p.Age / p.Life;
                p.Velocity *= 1f - 4f * dt;
                Transform tr = view.transform;
                tr.position += (Vector3)(p.Velocity * dt);
                tr.Rotate(0f, 0f, p.Spin * dt);
                tr.localScale = Vector3.one * (p.Size * (1f - t) / SpriteSize(view));
                Color c = p.Color;
                c.a *= 1f - t * t;
                view.color = c;
            }

            for (int i = 0; i < RingCount; i++)
            {
                if (_rings[i].Life < 0f) continue;

                ref Pop r = ref _rings[i];
                r.Age += dt;
                SpriteRenderer view = _ringViews[i];
                if (r.Age >= r.Life) { r.Life = -1f; view.enabled = false; continue; }

                float t = r.Age / r.Life;
                view.transform.localScale = Vector3.one * (r.Size * Mathf.Lerp(0.3f, 1f, t) / SpriteSize(view));
                Color c = r.Color;
                c.a = 0.5f * (1f - t);
                view.color = c;
            }

            for (int i = 0; i < PopupCount; i++)
            {
                if (_popups[i].Life < 0f) continue;

                ref Pop p = ref _popups[i];
                p.Age += dt;
                TextMeshPro view = _popupViews[i];
                if (p.Age >= p.Life) { p.Life = -1f; view.gameObject.SetActive(false); continue; }

                float t = p.Age / p.Life;
                view.transform.position += Vector3.up * (1.2f * dt);
                Color c = p.Color;
                c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                view.color = c;
            }
        }

        private static float SpriteSize(SpriteRenderer view)
            => view.sprite != null ? Mathf.Max(0.01f, view.sprite.bounds.size.x) : 1f;
    }
}

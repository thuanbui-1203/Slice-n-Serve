using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.UI
{
    /// <summary>
    /// Placeholder sound, synthesised once at start so the slice has no audio-asset dependency
    /// (plan §4.7's "must not block on art", applied to sound). Replace any clip with a real
    /// AudioClip later without touching the event wiring.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private const int Rate = 44100;

        [SerializeField] private AudioSource source;
        [SerializeField, Range(0f, 1f)] private float volume = 0.6f;

        private AudioClip _launch, _bounce, _slice, _waste, _serve, _expire, _gameOver, _tick;
        private EventBus _events;
        private float _lastBounce;

        public void Initialize(EventBus events)
        {
            _events = events;
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;

            _launch = Sweep("launch", 0.18f, 300f, 900f, 0.5f, noise: 0.6f);
            _bounce = Tone("bounce", 0.05f, 1400f, 0.35f, decay: 60f);
            _slice = Sweep("slice", 0.12f, 1800f, 500f, 0.5f, noise: 0.85f);
            _waste = Tone("waste", 0.16f, 140f, 0.6f, decay: 18f, noise: 0.3f);
            _serve = Arpeggio("serve", new[] { 660f, 880f, 1320f }, 0.09f, 0.45f);
            _expire = Arpeggio("expire", new[] { 330f, 247f }, 0.16f, 0.5f, square: true);
            _gameOver = Arpeggio("gameover", new[] { 392f, 330f, 262f, 196f }, 0.22f, 0.5f, square: true);
            _tick = Tone("tick", 0.04f, 2200f, 0.25f, decay: 80f);

            _events.Subscribe<KnifeLaunched>(OnLaunched);
            _events.Subscribe<KnifeBounced>(OnBounced);
            _events.Subscribe<IngredientSliced>(OnSliced);
            _events.Subscribe<IngredientWasted>(OnWasted);
            _events.Subscribe<OrderServed>(OnServed);
            _events.Subscribe<OrderExpired>(OnExpired);
            _events.Subscribe<GameOver>(OnGameOver);
        }

        private void OnDestroy()
        {
            if (_events == null) return;

            _events.Unsubscribe<KnifeLaunched>(OnLaunched);
            _events.Unsubscribe<KnifeBounced>(OnBounced);
            _events.Unsubscribe<IngredientSliced>(OnSliced);
            _events.Unsubscribe<IngredientWasted>(OnWasted);
            _events.Unsubscribe<OrderServed>(OnServed);
            _events.Unsubscribe<OrderExpired>(OnExpired);
            _events.Unsubscribe<GameOver>(OnGameOver);
        }

        /// <summary>For UI buttons.</summary>
        public void PlayClick() => Play(_tick, 1f);

        private void OnLaunched(KnifeLaunched e) => Play(_launch, 1f);

        private void OnBounced(KnifeBounced e)
        {
            // Corner hits can fire two bounces in one step; one click is enough.
            if (Time.time - _lastBounce < 0.04f) return;
            _lastBounce = Time.time;
            Play(_bounce, 1f + Random.Range(-0.08f, 0.08f));
        }

        private void OnSliced(IngredientSliced e) => Play(_slice, 1f + Mathf.Min(e.Combo, 8) * 0.06f);
        private void OnWasted(IngredientWasted e) => Play(_waste, 1f);
        private void OnServed(OrderServed e) => Play(_serve, 1f);
        private void OnExpired(OrderExpired e) => Play(_expire, 1f);
        private void OnGameOver(GameOver e) => Play(_gameOver, 1f);

        private void Play(AudioClip clip, float pitch)
        {
            if (clip == null) return;

            // PlayOneShot ignores pitch per call, so a pitched cue gets the shared source's pitch.
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
        }

        private static AudioClip Tone(string clipName, float seconds, float freq, float amp, float decay, float noise = 0f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(clipName.GetHashCode());
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(i / 40f);
                float s = Mathf.Sin(2f * Mathf.PI * freq * t);
                float r = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = amp * env * Mathf.Lerp(s, r, noise);
            }
            return Build(clipName, data);
        }

        private static AudioClip Sweep(string clipName, float seconds, float from, float to, float amp, float noise)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(clipName.GetHashCode());
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                float freq = Mathf.Lerp(from, to, u);
                phase += 2f * Mathf.PI * freq / Rate;
                float env = Mathf.Sin(u * Mathf.PI) * (1f - u);
                float r = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (r - lp) * Mathf.Clamp01(freq / 4000f);   // band-limited noise: a "swish"
                data[i] = amp * env * Mathf.Lerp(Mathf.Sin(phase), lp * 2f, noise);
            }
            return Build(clipName, data);
        }

        private static AudioClip Arpeggio(string clipName, float[] notes, float noteSeconds, float amp, bool square = false)
        {
            int per = Mathf.CeilToInt(noteSeconds * Rate);
            var data = new float[per * notes.Length + Rate / 10];
            for (int k = 0; k < notes.Length; k++)
            {
                for (int i = 0; i < per * 2 && k * per + i < data.Length; i++)
                {
                    float t = (float)i / Rate;
                    float s = Mathf.Sin(2f * Mathf.PI * notes[k] * t);
                    if (square) s = Mathf.Sign(s) * 0.5f;
                    float env = Mathf.Exp(-t * 9f) * Mathf.Clamp01(i / 60f);
                    data[k * per + i] += amp * env * s;
                }
            }
            return Build(clipName, data);
        }

        private static AudioClip Build(string clipName, float[] data)
        {
            AudioClip clip = AudioClip.Create(clipName, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

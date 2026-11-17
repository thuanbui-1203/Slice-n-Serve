using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.UI
{
    /// <summary>
    /// Placeholder sound, synthesised once at start so the slice has no audio-asset dependency
    /// (plan §4.7's "must not block on art", applied to sound). Replace any clip with a real
    /// AudioClip later without touching the event wiring.
    ///
    /// No two plays sound quite the same: the frequent gameplay sounds come in a few slightly
    /// different takes, picked at random, and every play gets its own random pitch, loudness and
    /// left/right position (<see cref="Variation"/>). A small pool of sources lets overlapping
    /// sounds keep their own pitch.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private const int Rate = 44100;

        [SerializeField] private AudioSource source;
        [SerializeField, Range(0f, 1f)] private float volume = 0.6f;

        [System.Serializable]
        public struct Variation
        {
            [Tooltip("Random pitch, ± this fraction (0.06 = ±6 %).")]
            [Range(0f, 0.3f)] public float pitch;
            [Tooltip("Random quieter by up to this fraction.")]
            [Range(0f, 0.6f)] public float volume;
            [Tooltip("Random left/right position, ± this much (0 centre, 1 fully to one side).")]
            [Range(0f, 1f)] public float pan;
        }

        [Header("Sound files (Assets/Audio) — each replaces its synthesised sound when set")]
        [SerializeField] private AudioClip runEndClip;
        [SerializeField] private AudioClip orderServedClip;
        [SerializeField] private AudioClip knifeBounceClip;
        [SerializeField] private AudioClip knifeLaunchClip;
        [Tooltip("One is picked at random for each roll.")]
        [SerializeField] private AudioClip[] diceRollClips;
        [SerializeField] private AudioClip rushStartClip;
        [SerializeField] private AudioClip rushEndClip;
        [Tooltip("Sound files are turned down (never up) to about this loudness (RMS; 0.1 ≈ -20 dB), "
               + "so a loud recording never drowns the rest.")]
        [SerializeField, Range(0.01f, 1f)] private float fileLoudness = 0.1f;

        [Header("Randomness on every play")]
        [Tooltip("Knife, slices, bounces, waste, serve: the sounds heard over and over.")]
        [SerializeField] private Variation gameplay = new Variation { pitch = 0.07f, volume = 0.25f, pan = 0.3f };
        [Tooltip("Stingers and UI (rush, game over, purchase, clicks): kept closer to their tune.")]
        [SerializeField] private Variation stinger = new Variation { pitch = 0.025f, volume = 0.1f, pan = 0.1f };
        [SerializeField, Range(1, 5)] private int takesPerSound = 3;
        [SerializeField, Range(2, 16)] private int voices = 8;

        [Header("Music (optional)")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioClip music;
        [SerializeField, Range(0f, 1f)] private float musicLevel = 0.6f;

        private AudioClip[] _launch, _bounce, _slice, _waste, _serve;
        private AudioClip _expire, _gameOver, _tick, _purchase, _rush, _rushEnd;
        private AudioClip[] _dice;
        private readonly System.Collections.Generic.Dictionary<AudioClip, float> _gain =
            new System.Collections.Generic.Dictionary<AudioClip, float>();
        private AudioSource[] _voices;
        private int _nextVoice;
        private EventBus _events;
        private GameSettings _settings;
        private float _lastBounce;

        public void Initialize(EventBus events)
        {
            _events = events;
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;

            // A few voices, so overlapping sounds each keep their own random pitch and pan.
            _voices = new AudioSource[Mathf.Max(1, voices)];
            _voices[0] = source;
            for (int i = 1; i < _voices.Length; i++)
            {
                AudioSource v = gameObject.AddComponent<AudioSource>();
                v.playOnAwake = false;
                v.outputAudioMixerGroup = source.outputAudioMixerGroup;
                _voices[i] = v;
            }

            // Several takes of each frequent sound: slightly different pitch, length and noise.
            _launch = Takes(k => Sweep("launch", 0.18f * k.len, 300f * k.f, 900f * k.f2, 0.5f, noise: 0.6f * k.n));
            _bounce = Takes(k => Tone("bounce", 0.05f * k.len, 1400f * k.f, 0.35f, decay: 60f / k.len));
            _slice = Takes(k => Sweep("slice", 0.12f * k.len, 1800f * k.f, 500f * k.f2, 0.5f, noise: 0.85f * k.n));
            _waste = Takes(k => Tone("waste", 0.16f * k.len, 140f * k.f, 0.6f, decay: 18f / k.len, noise: 0.3f * k.n));
            _serve = Takes(k => Arpeggio("serve", new[] { 660f * k.f, 880f * k.f, 1320f * k.f2 }, 0.09f * k.len, 0.45f));
            _expire = Arpeggio("expire", new[] { 330f, 247f }, 0.16f, 0.5f, square: true);
            _gameOver = Arpeggio("gameover", new[] { 392f, 330f, 262f, 196f }, 0.22f, 0.5f, square: true);
            _tick = Tone("tick", 0.04f, 2200f, 0.25f, decay: 80f);
            _purchase = Arpeggio("purchase", new[] { 523f, 659f, 784f, 1047f }, 0.06f, 0.4f);
            _rush = Arpeggio("rush", new[] { 880f, 659f, 880f, 659f, 880f }, 0.1f, 0.45f, square: true);

            // Recorded sounds, where provided, replace the synthesised ones (still varied per play).
            if (knifeLaunchClip != null) _launch = new[] { Level(knifeLaunchClip) };
            if (knifeBounceClip != null) _bounce = new[] { Level(knifeBounceClip) };
            if (orderServedClip != null) _serve = new[] { Level(orderServedClip) };
            if (runEndClip != null) _gameOver = Level(runEndClip);
            if (rushStartClip != null) _rush = Level(rushStartClip);
            if (rushEndClip != null) _rushEnd = Level(rushEndClip);
            _dice = System.Array.FindAll(diceRollClips ?? new AudioClip[0], c => c != null);
            for (int i = 0; i < _dice.Length; i++) Level(_dice[i]);

            _events.Subscribe<KnifeLaunched>(OnLaunched);
            _events.Subscribe<KnifeBounced>(OnBounced);
            _events.Subscribe<IngredientSliced>(OnSliced);
            _events.Subscribe<IngredientWasted>(OnWasted);
            _events.Subscribe<OrderServed>(OnServed);
            _events.Subscribe<OrderExpired>(OnExpired);
            _events.Subscribe<GameOver>(OnGameOver);
            _events.Subscribe<RushHourChanged>(OnRush);
            _events.Subscribe<DiceRolled>(OnDiceRolled);
        }

        /// <summary>Follows the Settings panel's sliders, and starts the background music.</summary>
        public void BindSettings(GameSettings settings)
        {
            _settings = settings;
            _settings.Changed += ApplyVolumes;
            ApplyVolumes();

            if (musicSource == null || music == null) return;
            musicSource.clip = music;
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.Play();
        }

        private void ApplyVolumes()
        {
            if (musicSource != null) musicSource.volume = musicLevel * MusicVolume;
        }

        private float MusicVolume => _settings != null ? _settings.MusicVolume : 1f;
        private float SfxVolume => _settings != null ? _settings.SfxVolume : 1f;

        private void OnDestroy()
        {
            if (_settings != null) _settings.Changed -= ApplyVolumes;
            if (_events == null) return;

            _events.Unsubscribe<KnifeLaunched>(OnLaunched);
            _events.Unsubscribe<KnifeBounced>(OnBounced);
            _events.Unsubscribe<IngredientSliced>(OnSliced);
            _events.Unsubscribe<IngredientWasted>(OnWasted);
            _events.Unsubscribe<OrderServed>(OnServed);
            _events.Unsubscribe<OrderExpired>(OnExpired);
            _events.Unsubscribe<GameOver>(OnGameOver);
            _events.Unsubscribe<RushHourChanged>(OnRush);
            _events.Unsubscribe<DiceRolled>(OnDiceRolled);
        }

        /// <summary>For UI buttons.</summary>
        public void PlayClick() => Play(_tick, 1f, stinger);

        /// <summary>For toggles, whose event carries the new value.</summary>
        public void PlayToggle(bool _) => Play(_tick, 1.2f, stinger);

        /// <summary>For the shop: a bright chime on a purchase, a dull thud when it can't be afforded.</summary>
        public void PlayPurchase(bool bought)
        {
            if (bought) Play(_purchase, 1f, stinger);
            else Play(_waste, 1f);
        }

        private void OnLaunched(KnifeLaunched e) => Play(_launch, 1f);

        private void OnBounced(KnifeBounced e)
        {
            // Corner hits can fire two bounces in one step; one click is enough.
            if (Time.time - _lastBounce < 0.04f) return;
            _lastBounce = Time.time;
            Play(_bounce, 1f);
        }

        private void OnSliced(IngredientSliced e) => Play(_slice, 1f + Mathf.Min(e.Combo, 8) * 0.06f);
        private void OnWasted(IngredientWasted e) => Play(_waste, 1f);
        private void OnServed(OrderServed e) => Play(_serve, 1f);
        private void OnExpired(OrderExpired e) => Play(_expire, 1f, stinger);
        private void OnGameOver(GameOver e) => Play(_gameOver, 1f, stinger);
        private void OnRush(RushHourChanged e) => Play(e.Active ? _rush : _rushEnd, 1f, stinger);
        private void OnDiceRolled(DiceRolled e) => Play(_dice, 1f);

        /// <summary>A random take of a frequent sound, with the gameplay variation.</summary>
        private void Play(AudioClip[] takes, float pitch)
        {
            if (takes == null || takes.Length == 0) return;
            Play(takes[Random.Range(0, takes.Length)], pitch, gameplay);
        }

        private void Play(AudioClip clip, float pitch, Variation vary)
        {
            if (clip == null || _voices == null) return;

            // Round-robin over the voices: PlayOneShot takes the source's pitch and pan, so each
            // play gets a source of its own to set them on.
            AudioSource voice = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            voice.pitch = pitch * (1f + Random.Range(-vary.pitch, vary.pitch));
            voice.panStereo = Random.Range(-vary.pan, vary.pan);
            float level = _gain.TryGetValue(clip, out float g) ? g : 1f;
            voice.PlayOneShot(clip, volume * SfxVolume * level * (1f - Random.Range(0f, vary.volume)));
        }

        /// <summary>
        /// Measures a sound file once and remembers how far to turn it down so it sits at
        /// <see cref="fileLoudness"/>. Only ever quieter: a soft file is left as recorded.
        /// </summary>
        private AudioClip Level(AudioClip clip)
        {
            if (clip == null || _gain.ContainsKey(clip)) return clip;
            float gain = 1f;
            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                var data = new float[clip.samples * clip.channels];
                if (data.Length > 0 && clip.GetData(data, 0))
                {
                    double sum = 0;
                    for (int i = 0; i < data.Length; i++) sum += data[i] * data[i];
                    float rms = (float)System.Math.Sqrt(sum / data.Length);
                    if (rms > 1e-5f) gain = Mathf.Min(1f, fileLoudness / rms);
                }
            }
            catch (System.Exception) { /* streamed or unreadable: play as recorded */ }
            _gain[clip] = gain;
            return clip;
        }

        /// <summary>For tests: how much a sound file is turned down (1 = as recorded).</summary>
        public float GainOf(AudioClip clip) => clip != null && _gain.TryGetValue(clip, out float g) ? g : 1f;

        /// <summary>Per-take scales for the synthesised sounds: frequency, a second frequency,
        /// length and noise, each a few per cent either side of 1.</summary>
        private struct Take { public float f, f2, len, n; }

        private AudioClip[] Takes(System.Func<Take, AudioClip> make)
        {
            var clips = new AudioClip[Mathf.Max(1, takesPerSound)];
            var rng = new System.Random(clips.Length * 7919);
            float Near(float spread) => 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
            for (int i = 0; i < clips.Length; i++)
            {
                var k = i == 0 ? new Take { f = 1f, f2 = 1f, len = 1f, n = 1f }   // the original first
                               : new Take { f = Near(0.06f), f2 = Near(0.08f), len = Near(0.12f), n = Near(0.25f) };
                clips[i] = make(k);
            }
            return clips;
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

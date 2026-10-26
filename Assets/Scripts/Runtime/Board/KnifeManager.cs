using System;
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Owns the knife pool and the only public way to launch. Pooled from day one even though
    /// Section 2 launches one at a time, because Section 3's "Knife Split Chance" multiplies
    /// within the cap rather than introducing the concept of many knives.
    /// </summary>
    public sealed class KnifeManager : MonoBehaviour
    {
        /// <summary>The §1 budget's hard ceiling on simultaneous knives.</summary>
        public const int PoolCapacity = 16;

        [SerializeField] private KnifeController knifePrefab;

        private readonly KnifeController[] _pool = new KnifeController[PoolCapacity];
        private readonly KnifeController[] _pendingSplits = new KnifeController[PoolCapacity];
        private int _pendingSplitCount;
        private RuntimeTuning _tuning;
        private KnifeController _manualKnife;
        private int _nextId = 1;
        private int _liveCount;
        private bool _initialized;

        /// <summary>True when a knife is idle at the launcher: Section 2 allows one live manual
        /// knife. Helper knives (§3) never block the player.</summary>
        public bool CanLaunch => _initialized && _manualKnife == null;
        public int LiveCount => _liveCount;

        /// <summary>Fired whenever a knife returns to the launcher.</summary>
        public event Action<KnifeController> KnifeReturned;

        public void Initialize(RuntimeTuning tuning, EventBus events, BoardBounds bounds, IngredientSpawner spawner)
        {
            _tuning = tuning;

            // Pooled up front and never instantiated during a run.
            for (int i = 0; i < PoolCapacity; i++)
            {
                KnifeController k = Instantiate(knifePrefab, transform);
                k.name = $"Knife_{i}";
                k.gameObject.SetActive(false);
                k.Initialize(tuning, events, spawner, bounds.PlayArea);
                k.Expired += OnKnifeExpired;
                k.Bounced += OnKnifeBounced;
                _pool[i] = k;
            }

            _initialized = true;
        }

        /// <summary>Takes a velocity, not a drag, so §3's Sous-Chef helpers can call it with a
        /// computed aim vector and <see cref="LaunchSource.Helper"/> and no input at all.</summary>
        public KnifeController Launch(Vector2 velocity, LaunchSource source)
            => Launch(_tuning.LauncherPosition, velocity, source);

        /// <summary>Launches from an arbitrary point — a split knife starts where its parent is.</summary>
        public KnifeController Launch(Vector2 position, Vector2 velocity, LaunchSource source)
        {
            // The single-live-knife rule guards against a double-tap; helpers bypass it (§3).
            if (source == LaunchSource.Manual && !CanLaunch) return null;

            for (int i = 0; i < PoolCapacity; i++)
            {
                KnifeController k = _pool[i];
                if (k.IsLive) continue;

                k.Launch(_nextId++, position, velocity, source);
                _liveCount++;
                if (source == LaunchSource.Manual) _manualKnife = k;
                return k;
            }
            return null;
        }

        /// <summary>For §3's wave clear and §5's round reset.</summary>
        public void ReturnAllKnives()
        {
            _pendingSplitCount = 0;
            for (int i = 0; i < PoolCapacity; i++)
                if (_pool[i] != null && _pool[i].IsLive) OnKnifeExpired(_pool[i]);
        }

        /// <summary>
        /// Knife Split Chance (concept §3): each knife may split once, on a wall hit. The roll
        /// happens in the collision callback, but the launch waits for the next physics step —
        /// enabling a body from inside a contact callback is not something to rely on.
        /// </summary>
        private void OnKnifeBounced(KnifeController k)
        {
            if (k.HasSplit || _tuning.SplitChance <= 0f) return;
            if (UnityEngine.Random.value >= _tuning.SplitChance) return;

            k.HasSplit = true;
            if (_pendingSplitCount < _pendingSplits.Length) _pendingSplits[_pendingSplitCount++] = k;
        }

        private void FixedUpdate()
        {
            for (int i = 0; i < _pendingSplitCount; i++)
            {
                KnifeController parent = _pendingSplits[i];
                _pendingSplits[i] = null;
                if (!parent.IsLive) continue;

                Vector2 v = parent.Body.linearVelocity;
                Launch(parent.Body.position, Rotate(v, _tuning.Config.splitAngleDegrees), LaunchSource.Split);
                parent.Body.linearVelocity = Rotate(v, -_tuning.Config.splitAngleDegrees);   // fan out both ways
            }
            _pendingSplitCount = 0;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private void OnKnifeExpired(KnifeController k)
        {
            k.ReturnToPool();
            _liveCount--;
            if (k == _manualKnife) _manualKnife = null;
            KnifeReturned?.Invoke(k);
        }
    }
}

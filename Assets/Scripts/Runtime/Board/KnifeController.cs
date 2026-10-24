using System;
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    public sealed class KnifeController : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private IngredientSlicer slicer;
        [SerializeField] private TrailRenderer trail;

        private RuntimeTuning _tuning;
        private EventBus _events;
        private Rect _playArea;
        private int _wallLayer;

        private int _knifeId;
        private int _bounceCount;
        private int _sliceCount;
        private float _age;
        private bool _live;

        public bool IsLive => _live;
        public int KnifeId => _knifeId;
        public int BounceCount => _bounceCount;

        /// <summary>Ingredients sliced since launch — the combo counter, reset per launch.</summary>
        public int SliceCount => _sliceCount;
        public Rigidbody2D Body => body;

        public event Action<KnifeController> Expired;

        public void Initialize(RuntimeTuning tuning, EventBus events, IngredientSpawner spawner, Rect playArea)
        {
            _tuning = tuning;
            _events = events;
            _playArea = playArea;
            _wallLayer = LayerMask.NameToLayer("BoardWall");

            if (body == null) body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;                                       // decision D10
            body.linearDamping = 0f;
            body.angularDamping = 0.4f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.simulated = false;

            if (slicer == null) slicer = GetComponent<IngredientSlicer>();
            slicer.Initialize(body, spawner);
        }

        public void Launch(int knifeId, Vector2 position, Vector2 velocity, LaunchSource source)
        {
            _knifeId = knifeId;
            _bounceCount = 0;
            _sliceCount = 0;
            _age = 0f;
            _live = true;

            // Move the body, not just the transform: an unsimulated body keeps its old pose
            // otherwise, and interpolation would smear the knife from where it last died.
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg));
            body.position = position;
            body.rotation = transform.eulerAngles.z;
            gameObject.SetActive(true);
            body.simulated = true;
            body.linearVelocity = velocity;
            body.angularVelocity = -Mathf.Sign(velocity.x == 0f ? 1f : velocity.x) * 900f;
            if (trail != null) trail.Clear();

            _events.Publish(new KnifeLaunched(knifeId, source));
        }

        public void ReturnToPool()
        {
            if (!_live) return;

            _live = false;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
            gameObject.SetActive(false);
            _events.Publish(new KnifeReturned(_knifeId, _sliceCount));
        }

        private void FixedUpdate()
        {
            if (!_live) return;

            _age += Time.fixedDeltaTime;
            // Plan §5.5 — collection never touches velocity.
            _sliceCount += slicer.Sweep(_knifeId, _bounceCount, _sliceCount, Time.fixedDeltaTime);
            CheckEnd();
        }

        private void CheckEnd()
        {
            float minSq = _tuning.MinLiveSpeed * _tuning.MinLiveSpeed;
            bool tooSlow = body.linearVelocity.sqrMagnitude < minSq;
            bool tooOld = _age > _tuning.MaxKnifeLifetime;
            bool tooBouncy = _bounceCount > _tuning.MaxKnifeBounces;
            bool escaped = !_playArea.Contains(body.position);   // safety net, not a mechanism

            if (tooSlow || tooOld || tooBouncy || escaped) Expired?.Invoke(this);
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (!_live) return;

            // Layer, not tag — this project defines no tags (plan §4.3).
            if (c.collider.gameObject.layer != _wallLayer) return;

            ContactPoint2D contact = c.GetContact(0);   // contactCount is always >= 1 here
            _bounceCount++;
            _events.Publish(new KnifeBounced(_knifeId, _bounceCount, contact.point));

            // OnCollisionEnter2D runs *after* the physics step, so the bounce has already
            // reflected the velocity; this only scales it.
            Vector2 v = body.linearVelocity * _tuning.BounceRetention;
            float cap = _tuning.SpeedHardCap;
            if (v.sqrMagnitude > cap * cap) v = v.normalized * cap;

            // Anti-"wall-hugging": if the outgoing direction is nearly parallel to the wall, nudge
            // it a few degrees *away from* the wall so the knife cannot ride the boundary. A
            // random rotation could just as easily steer it into the wall.
            Vector2 n = contact.normal;
            if (Vector2.Dot(n, body.position - contact.point) < 0f) n = -n;
            if (Mathf.Abs(Vector2.Dot(v.normalized, n)) < 0.08f)
                v = (v.normalized + n * 0.12f).normalized * v.magnitude;

            body.linearVelocity = v;
        }
    }
}

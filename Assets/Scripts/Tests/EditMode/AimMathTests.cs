using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Tests
{
    public sealed class AimMathTests
    {
        private RuntimeTuning _tuning;

        [SetUp] public void SetUp() => _tuning = TestData.Tuning();
        [TearDown] public void TearDown() => TestData.Cleanup();

        [Test]
        public void Power_IsDragLengthOverMaxDrag_Clamped()
        {
            Assert.That(KnifeLauncher.ResolvePower(new Vector2(0f, -1.75f), _tuning), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(KnifeLauncher.ResolvePower(new Vector2(0f, -35f), _tuning), Is.EqualTo(1f));
        }

        [Test]
        public void Speed_SpansTheLaunchEnvelope()
        {
            Assert.That(KnifeLauncher.ResolveVelocity(new Vector2(0f, -0.0001f), _tuning).magnitude, Is.EqualTo(8f).Within(1e-3f));
            Assert.That(KnifeLauncher.ResolveVelocity(new Vector2(0f, -3.5f), _tuning).magnitude, Is.EqualTo(22f).Within(1e-3f));
            Assert.That(KnifeLauncher.ResolveVelocity(new Vector2(0f, -100f), _tuning).magnitude, Is.EqualTo(22f).Within(1e-3f),
                "over-dragging must not exceed launchMaxSpeed");
        }

        [Test]
        public void SpeedUpgrade_ScalesTheEnvelope()
        {
            _tuning.LaunchSpeedMultiplier = 1.5f;
            Assert.That(KnifeLauncher.ResolveVelocity(new Vector2(0f, -3.5f), _tuning).magnitude, Is.EqualTo(33f).Within(1e-3f));
        }

        [Test]
        public void DeadZone_RejectsTaps()
        {
            Assert.That(KnifeLauncher.IsValidDrag(new Vector2(0.2f, 0.2f), _tuning), Is.False);
            Assert.That(KnifeLauncher.IsValidDrag(new Vector2(0f, 0.35f), _tuning), Is.True);
        }

        [Test]
        public void Direction_IsASlingshot()
        {
            Vector2 drag = new Vector2(-1f, -2f);
            Vector2 dir = KnifeLauncher.ResolveDirection(drag);
            Assert.That(Vector2.Dot(dir, -drag.normalized), Is.EqualTo(1f).Within(1e-5f), "launch is opposite the drag");
        }

        [TestCase(1f, 0f)]
        [TestCase(-1f, 0.01f)]
        [TestCase(0f, 1f)]
        [TestCase(0.02f, -1f)]
        public void NearParallelDirections_AreLiftedToTheMinimumAngle(float x, float y)
        {
            Vector2 d = KnifeLauncher.ClampAwayFromParallelWalls(new Vector2(x, y));
            float minSin = Mathf.Sin(KnifeLauncher.MinWallAngleDegrees * Mathf.Deg2Rad);

            Assert.That(d.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Mathf.Abs(d.x), Is.GreaterThanOrEqualTo(minSin - 1e-5f));
            Assert.That(Mathf.Abs(d.y), Is.GreaterThanOrEqualTo(minSin - 1e-5f));
            Assert.That(Mathf.Sign(d.x), Is.EqualTo(x < 0f ? -1f : 1f), "stays in the quadrant");
        }

        [Test]
        public void DiagonalDirections_AreUntouched()
        {
            Vector2 diag = new Vector2(1f, 1f).normalized;
            Assert.That(Vector2.Distance(KnifeLauncher.ClampAwayFromParallelWalls(diag), diag), Is.LessThan(1e-6f));
        }
    }
}

using NUnit.Framework;
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Tests
{
    public sealed class ViewportMathTests
    {
        private const float HalfWidth = 9.6f;   // 19.2-unit board box (BoardConfig.worldBounds)
        private const float HalfHeight = 5.4f;  // 10.8-unit board box
        private const float Tolerance = 1e-4f;

        /// <summary>The table in the §1 plan, Section 6.3, as executable assertions.</summary>
        [TestCase(4f / 3f, 7.2f)]       // 4:3     -> height-bound
        [TestCase(3f / 2f, 6.4f)]       // 3:2
        [TestCase(16f / 10f, 6f)]       // 16:10
        [TestCase(16f / 9f, 5.4f)]      // 16:9    -> exactly Section 2's reference camera
        [TestCase(19.5f / 9f, 5.4f)]    // tall phone
        [TestCase(20f / 9f, 5.4f)]      // taller phone
        [TestCase(21f / 9f, 5.4f)]      // ultrawide edge
        public void OrthographicSize_MatchesTheDocumentedAspectTable(float aspect, float expected)
        {
            float effective = ViewportMath.EffectiveAspect(aspect);
            float size = ViewportMath.OrthographicSize(HalfWidth, HalfHeight, effective);
            Assert.That(size, Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void OrthographicSize_NeverCropsTheBoard_AtAnyRealScreen()
        {
            float[] aspects = { 0.5f, 1f, 4f / 3f, 1.5f, 16f / 10f, 16f / 9f, 19.5f / 9f, 21f / 9f, 32f / 9f };
            foreach (float aspect in aspects)
            {
                float effective = ViewportMath.EffectiveAspect(aspect);
                float size = ViewportMath.OrthographicSize(HalfWidth, HalfHeight, effective);
                Assert.That(size * effective, Is.GreaterThanOrEqualTo(HalfWidth - Tolerance),
                    $"board width cropped at aspect {aspect}");
                Assert.That(size, Is.GreaterThanOrEqualTo(HalfHeight - Tolerance),
                    $"board height cropped at aspect {aspect}");
            }
        }

        [Test]
        public void EffectiveAspect_ClampsToTheSupportedEnvelope()
        {
            Assert.That(ViewportMath.EffectiveAspect(32f / 9f), Is.EqualTo(ViewportMath.MaxAspect));
            Assert.That(ViewportMath.EffectiveAspect(0.5f), Is.EqualTo(ViewportMath.MinAspect));
            Assert.That(ViewportMath.EffectiveAspect(16f / 9f), Is.EqualTo(16f / 9f).Within(Tolerance));
        }

        [Test]
        public void LetterboxRect_IsTheFullViewportInsideTheEnvelope()
        {
            Rect rect = ViewportMath.LetterboxRect(16f / 9f, ViewportMath.EffectiveAspect(16f / 9f));
            Assert.That(rect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
        }

        [Test]
        public void LetterboxRect_PillarboxesBeyondTheWidestSupportedAspect()
        {
            const float screen = 32f / 9f;
            float effective = ViewportMath.EffectiveAspect(screen);
            Rect rect = ViewportMath.LetterboxRect(screen, effective);

            Assert.That(rect.height, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(rect.width, Is.EqualTo(effective / screen).Within(Tolerance));
            Assert.That(rect.x, Is.EqualTo((1f - rect.width) * 0.5f).Within(Tolerance));
            Assert.That(rect.width, Is.LessThan(1f), "ultrawide must not see more board");
        }

        [Test]
        public void LetterboxRect_LetterboxesBelowTheNarrowestSupportedAspect()
        {
            const float screen = 1f;
            float effective = ViewportMath.EffectiveAspect(screen);
            Rect rect = ViewportMath.LetterboxRect(screen, effective);

            Assert.That(rect.width, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(rect.height, Is.EqualTo(screen / effective).Within(Tolerance));
            Assert.That(rect.y, Is.EqualTo((1f - rect.height) * 0.5f).Within(Tolerance));
        }
    }
}

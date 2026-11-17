using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Tests
{
    /// <summary>The die, the seeded randomness behind it (§5 prep), and auto-throw's aim.</summary>
    public sealed class DiceTests
    {
        [Test]
        public void Die_RollsEveryFaceFromOneToSix_AndNothingElse()
        {
            var random = new GameRandom(7);
            var seen = new bool[7];
            for (int i = 0; i < 600; i++)
            {
                int roll = random.RollDie();
                Assert.That(roll, Is.InRange(1, 6));
                seen[roll] = true;
            }
            for (int face = 1; face <= 6; face++) Assert.That(seen[face], Is.True, $"face {face}");
        }

        [Test]
        public void SameSeed_SameRolls_SoBothPvpPlayersGetTheSameRun()
        {
            var a = new GameRandom(1234);
            var b = new GameRandom(1234);
            for (int i = 0; i < 50; i++) Assert.That(a.RollDie(), Is.EqualTo(b.RollDie()));

            a.Reseed(99);
            b.Reseed(99);
            Assert.That(a.Value, Is.EqualTo(b.Value));
        }

        [Test]
        public void AutoThrow_AlwaysAimsIntoTheBoard_NeverAlongAWall()
        {
            var random = new GameRandom(3);
            float sinWall = Mathf.Sin(KnifeLauncher.MinWallAngleDegrees * Mathf.Deg2Rad) - 1e-4f;
            for (int i = 0; i < 500; i++)
            {
                Vector2 d = AutoThrower.RandomDirection(random);
                Assert.That(d.magnitude, Is.EqualTo(1f).Within(1e-4f));
                Assert.That(d.y, Is.GreaterThan(0f), "upward, away from the launcher's wall");
                Assert.That(Mathf.Abs(d.x), Is.GreaterThanOrEqualTo(sinWall));
                Assert.That(Mathf.Abs(d.y), Is.GreaterThanOrEqualTo(sinWall));
            }
        }
    }
}

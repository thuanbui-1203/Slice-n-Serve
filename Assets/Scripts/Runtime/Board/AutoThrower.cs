using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Auto-throw, unlocked once Knife Speed and Split Chance are both maxed. Whenever the
    /// player's knife is idle and the die has landed, it throws the knife for them — at full
    /// speed, in a random upward direction — and the die decides how many bounces it gets, as for
    /// any throw. The player's hands are then free to tap the board for more ingredients.
    ///
    /// Plain C#, ticked by the run owner with the run clock, like <see cref="SousChefCrew"/>.
    /// </summary>
    public sealed class AutoThrower
    {
        /// <summary>Throws go between this angle and 180° minus it, so none starts into the bottom wall.</summary>
        public const float MinAngleDegrees = 12f;

        private readonly RuntimeTuning _tuning;
        private readonly KnifeManager _knives;
        private readonly GameRandom _random;
        private float _wait;

        public AutoThrower(RuntimeTuning tuning, KnifeManager knives, GameRandom random)
        {
            _tuning = tuning;
            _knives = knives;
            _random = random;
        }

        public bool Enabled { get; set; }

        /// <param name="diceSettled">False while the die is still tumbling: the throw waits for it.</param>
        public void Tick(float delta, bool diceSettled)
        {
            if (!Enabled || !_knives.CanLaunch || !diceSettled)
            {
                _wait = _tuning.Config.autoThrowPause;
                return;
            }

            _wait -= delta;
            if (_wait > 0f) return;

            _wait = _tuning.Config.autoThrowPause;
            _knives.Launch(RandomDirection(_random) * _tuning.LaunchMaxSpeed, LaunchSource.Auto);
        }

        /// <summary>A random direction into the board (upward half), never parallel to a wall.</summary>
        public static Vector2 RandomDirection(GameRandom random)
        {
            float a = random.Range(MinAngleDegrees, 180f - MinAngleDegrees) * Mathf.Deg2Rad;
            return KnifeLauncher.ClampAwayFromParallelWalls(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
        }
    }
}

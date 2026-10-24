using SliceNServe.Core;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Gesture → velocity. Pure and static so it is unit-testable and so the aim preview and the
    /// actual launch can never disagree: both call <see cref="ResolveVelocity"/>.
    /// </summary>
    public static class KnifeLauncher
    {
        /// <summary>Gestures closer to parallel than this to any wall are pushed away, so the first
        /// contact is never a graze (plan §5.6).</summary>
        public const float MinWallAngleDegrees = 4f;

        /// <summary>
        /// The gesture is a screen-space drag. Power is the drag length in <em>world units</em>
        /// divided by the reference length, so a desktop drag and a phone thumb that travel the
        /// same board distance produce the same launch.
        /// </summary>
        public static float ResolvePower(Vector2 dragWorld, RuntimeTuning tuning)
            => Mathf.Clamp01(dragWorld.magnitude / tuning.MaxDragLength);

        /// <summary>Slingshot: the launch direction is opposite the drag (decision D12).</summary>
        public static Vector2 ResolveDirection(Vector2 dragWorld)
            => ClampAwayFromParallelWalls(-dragWorld.normalized);

        /// <summary>Linear for now; the designer knob once feel is testable (plan §5.2.2).</summary>
        public static float PowerEase(float power) => power;

        public static float ResolveSpeed(Vector2 dragWorld, RuntimeTuning tuning)
            => Mathf.Lerp(tuning.LaunchMinSpeed, tuning.LaunchMaxSpeed, PowerEase(ResolvePower(dragWorld, tuning)));

        public static Vector2 ResolveVelocity(Vector2 dragWorld, RuntimeTuning tuning)
            => ResolveDirection(dragWorld) * ResolveSpeed(dragWorld, tuning);

        public static bool IsValidDrag(Vector2 dragWorld, RuntimeTuning tuning)
            => dragWorld.magnitude >= tuning.DeadZone;

        public static Vector2 ClampAwayFromParallelWalls(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return Vector2.up;

            Vector2 d = dir.normalized;
            float sin = Mathf.Sin(MinWallAngleDegrees * Mathf.Deg2Rad);
            float cos = Mathf.Cos(MinWallAngleDegrees * Mathf.Deg2Rad);
            float sx = d.x < 0f ? -1f : 1f, sy = d.y < 0f ? -1f : 1f;

            // Near-horizontal travel would graze a horizontal wall; near-vertical travel would
            // graze a vertical one. Rotate to exactly the minimum angle, keeping the quadrant.
            if (Mathf.Abs(d.y) < sin) return new Vector2(sx * cos, sy * sin);
            if (Mathf.Abs(d.x) < sin) return new Vector2(sx * sin, sy * cos);
            return d;
        }
    }
}

using UnityEngine;

namespace SliceNServe.Core
{
    /// <summary>
    /// The Section 1 aspect policy, as pure math: contain-fit the board box, clamp to the
    /// supported envelope, letterbox outside it. No scene and no camera, which is exactly why
    /// it can be covered by millisecond EditMode tests.
    ///
    /// The camera is the only caller — see BoardViewportAdapter.
    /// </summary>
    public static class ViewportMath
    {
        /// <summary>Narrowest supported aspect: 4:3 tablets.</summary>
        public const float MinAspect = 4f / 3f;

        /// <summary>Widest supported aspect: 21:9 ultrawide. Beyond this we pillarbox, so
        /// nobody can see more board than a 16:9 player.</summary>
        public const float MaxAspect = 21f / 9f;

        public static float EffectiveAspect(float screenAspect,
                                            float minAspect = MinAspect,
                                            float maxAspect = MaxAspect)
            => Mathf.Clamp(screenAspect, minAspect, maxAspect);

        /// <summary>
        /// Orthographic size that *contains* the whole board box at this aspect. The board is
        /// never cropped; leftover space becomes backdrop and HUD rails.
        /// Evaluates to exactly 5.4 for the 19.2 x 10.8 board at 16:9 — Section 2's reference
        /// camera value — so adopting this policy costs Section 2 nothing.
        /// </summary>
        public static float OrthographicSize(float boardHalfWidth, float boardHalfHeight,
                                             float effectiveAspect)
            => Mathf.Max(boardHalfHeight, boardHalfWidth / effectiveAspect);

        /// <summary>
        /// The <see cref="Camera.rect"/> that letterboxes the clamped aspect inside the real
        /// viewport. Full screen inside the supported envelope.
        /// </summary>
        public static Rect LetterboxRect(float screenAspect, float effectiveAspect)
        {
            if (screenAspect > effectiveAspect)
            {
                // Too wide: pillarbox.
                float width = effectiveAspect / screenAspect;
                return new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }

            // Too tall: letterbox.
            float height = screenAspect / effectiveAspect;
            return new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }
    }
}

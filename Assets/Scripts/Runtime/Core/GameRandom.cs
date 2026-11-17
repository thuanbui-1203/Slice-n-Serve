namespace SliceNServe.Core
{
    /// <summary>
    /// The one source of gameplay randomness: dice rolls, auto-throw directions, split rolls,
    /// ingredient and order picks. Seeded, so a PvP match (§5) can give both players the same
    /// board from the same seed, and a test can replay a run. Cosmetic randomness (particles,
    /// sound pitch, decor) stays on <c>UnityEngine.Random</c> and never touches this.
    /// </summary>
    public sealed class GameRandom
    {
        private System.Random _random;

        public GameRandom(int? seed = null) => Reseed(seed ?? System.Environment.TickCount);

        public int Seed { get; private set; }

        /// <summary>Restart the sequence: a PvP round starts every player from the match seed.</summary>
        public void Reseed(int seed)
        {
            Seed = seed;
            _random = new System.Random(seed);
        }

        /// <summary>[0, 1).</summary>
        public float Value => (float)_random.NextDouble();

        public float Range(float min, float max) => min + (max - min) * Value;

        /// <summary>[min, max) — like <c>UnityEngine.Random.Range(int, int)</c>.</summary>
        public int Range(int min, int max) => _random.Next(min, max);

        /// <summary>A six-sided die: 1 to 6.</summary>
        public int RollDie() => _random.Next(1, 7);
    }
}

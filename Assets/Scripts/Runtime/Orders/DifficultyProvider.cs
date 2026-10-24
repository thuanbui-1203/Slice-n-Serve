namespace SliceNServe.Orders
{
    /// <summary>Section 4 replaces this with a real time-based ramp. Section 2 ships the seam.</summary>
    public interface IDifficultyProvider
    {
        float PatienceScale { get; }
        float OrderSpawnIntervalScale { get; }
    }

    public sealed class ConstantDifficultyProvider : IDifficultyProvider
    {
        public float PatienceScale => 1f;
        public float OrderSpawnIntervalScale => 1f;
    }
}

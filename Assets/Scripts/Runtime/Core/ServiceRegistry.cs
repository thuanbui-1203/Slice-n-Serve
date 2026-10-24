namespace SliceNServe.Core
{
    /// <summary>
    /// The one container in the project (plan §7). Created by <see cref="GameBootstrap"/>; there
    /// are no other singletons. Sections 3+ add to this rather than inventing their own locator.
    /// </summary>
    public sealed class ServiceRegistry
    {
        public ServiceRegistry(EventBus events, RunClock clock, RuntimeTuning tuning, Wallet wallet)
        {
            Events = events;
            Clock = clock;
            Tuning = tuning;
            Wallet = wallet;
        }

        public EventBus Events { get; }
        public RunClock Clock { get; }
        public RuntimeTuning Tuning { get; }
        public Wallet Wallet { get; }

        public static ServiceRegistry Current { get; private set; }

        public static ServiceRegistry Install(ServiceRegistry registry)
        {
            Current = registry;
            return registry;
        }

        public static void Clear() => Current = null;
    }
}

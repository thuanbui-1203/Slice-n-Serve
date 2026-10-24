using SliceNServe.Core;

namespace SliceNServe.Orders
{
    /// <summary>
    /// Run-level lives. Decision D13: the failure consequence is reputation rather than lost
    /// revenue, because it is legible and it is what Section 3's upgrades protect.
    /// </summary>
    public sealed class ReputationService
    {
        private readonly EventBus _events;

        public int Reputation { get; private set; }
        public int StartReputation { get; }
        public bool IsGameOver => Reputation <= 0;

        public ReputationService(EventBus events, RuntimeTuning tuning)
        {
            _events = events;
            StartReputation = tuning.StartReputation;
            Reputation = StartReputation;
        }

        public void Penalise()
        {
            if (IsGameOver) return;

            Reputation--;
            _events.Publish(new ReputationChanged(Reputation, -1));

            if (IsGameOver) _events.Publish(new GameOver(GameOverReason.ReputationDepleted));
        }
    }
}

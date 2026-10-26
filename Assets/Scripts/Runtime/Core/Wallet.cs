using System;

namespace SliceNServe.Core
{
    /// <summary>
    /// The player's cash. Starts from the saved balance; runs add to it and the shop spends it
    /// (concept §3). Persisting it is <see cref="SaveService"/>'s job, not this class's.
    /// </summary>
    public sealed class Wallet
    {
        public Wallet(int startingAmount = 0) => Amount = Math.Max(0, startingAmount);

        public int Amount { get; private set; }

        public event Action<int> Changed;

        public void Add(int cash)
        {
            if (cash <= 0) return;

            Amount += cash;
            Changed?.Invoke(Amount);
        }

        public bool TrySpend(int cost)
        {
            if (cost < 0 || cost > Amount) return false;
            if (cost == 0) return true;

            Amount -= cost;
            Changed?.Invoke(Amount);
            return true;
        }
    }
}

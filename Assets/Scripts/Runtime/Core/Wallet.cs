using System;

namespace SliceNServe.Core
{
    /// <summary>
    /// Minimal cash holder. Section 3 grows this into the real economy — Section 2 only needs
    /// the value and its change event to prove the loop closes (plan §6.6). Keep the surface
    /// this small so replacing it stays cheap.
    /// </summary>
    public sealed class Wallet
    {
        public int Amount { get; private set; }

        public event Action<int> Changed;

        public void Add(int cash)
        {
            if (cash == 0) return;

            Amount += cash;
            Changed?.Invoke(Amount);
        }
    }
}

using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using UnityEngine;

namespace SliceNServe.Pvp
{
    /// <summary>
    /// "Shared tickets, separate boards" (concept §5): one line of customers for every player.
    /// Each player keeps their own <see cref="LocalOrderQueue"/> — their own progress on each
    /// order — and every customer is added to all of them with the same id, recipe and patience.
    /// The first player to serve an order is paid; it is withdrawn from everyone else's line
    /// (<see cref="OrderTaken"/>). Patience runs identically everywhere, so an order nobody
    /// serves expires for all players at once.
    ///
    /// It is an <see cref="IOrderQueue"/> so the <see cref="OrderSpawner"/> can feed it unchanged;
    /// reads (count, front, rush state) come from the first player's line, which mirrors the rest.
    /// </summary>
    public sealed class SharedOrderQueue : IOrderQueue
    {
        private readonly LocalOrderQueue[] _players;

        /// <param name="buses">Each player's order event bus, in player order: a serve on one
        /// withdraws the order from the others.</param>
        public SharedOrderQueue(LocalOrderQueue[] players, EventBus[] buses)
        {
            _players = players;
            for (int i = 0; i < buses.Length; i++)
            {
                int player = i;
                buses[i].Subscribe<OrderServed>(e => OnServed(player, e));
            }
        }

        public int PlayerCount => _players.Length;
        public LocalOrderQueue Player(int index) => _players[index];

        public int ActiveCount => _players[0].ActiveCount;
        public OrderInstance this[int index] => _players[0][index];
        public OrderInstance Find(int orderId) => _players[0].Find(orderId);

        /// <summary>The same customer joins every player's line.</summary>
        public void Add(OrderInstance order)
        {
            _players[0].Add(order);
            for (int i = 1; i < _players.Length; i++) _players[i].Add(order.CloneFresh());
        }

        /// <summary>Not used in PvP: a slice is credited on the board it happened on.</summary>
        public void SubmitIngredient(IngredientDefinition ingredient, Vector2 position) => _players[0].SubmitIngredient(ingredient, position);

        public bool TryServe(int orderId) => _players[0].TryServe(orderId);

        public OrderInstance Front() => _players[0].Front();

        public void Tick(float delta)
        {
            for (int i = 0; i < _players.Length; i++) _players[i].Tick(delta);
        }

        public bool FrontOnly
        {
            get => _players[0].FrontOnly;
            set { for (int i = 0; i < _players.Length; i++) _players[i].FrontOnly = value; }
        }

        private void OnServed(int player, OrderServed e)
        {
            for (int i = 0; i < _players.Length; i++)
                if (i != player) _players[i].Withdraw(e.OrderId, player);
        }
    }
}

using System.Collections.Generic;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using UnityEngine;

namespace SliceNServe.Pvp
{
    /// <summary>
    /// The online half of "first to serve gets the cash". The host runs the customer line and is
    /// the referee: its own serves count at once; the client's are requests the host grants only
    /// if the order is still open. Each side plays its own board; only these events cross the
    /// wire (<see cref="PvpMessage"/>). Plain C#, pumped by <see cref="PvpRunner"/> every frame.
    /// </summary>
    public sealed class OnlineSync
    {
        private readonly IMatchTransport _transport;
        private readonly bool _isHost;
        private readonly LocalOrderQueue _queue;
        private readonly EventBus _events;
        private readonly IReadOnlyList<DishDefinition> _dishes;
        private readonly IReadOnlyList<CustomerDefinition> _customers;
        private readonly HashSet<int> _pending = new HashSet<int>();

        private bool _sentReady, _peerReady, _peerEnded;
        private int _peerScore;

        public OnlineSync(IMatchTransport transport, bool isHost, LocalOrderQueue queue, EventBus events,
                          IReadOnlyList<DishDefinition> dishes, IReadOnlyList<CustomerDefinition> customers)
        {
            _transport = transport;
            _isHost = isHost;
            _queue = queue;
            _events = events;
            _dishes = dishes;
            _customers = customers;

            if (_isHost)
            {
                _events.Subscribe<OrderCreated>(OnCreated);
                _events.Subscribe<OrderServed>(OnHostServed);
            }
        }

        /// <summary>Both scenes are loaded and the host said go: the clock may run.</summary>
        public bool Started { get; private set; }
        public bool PeerEnded => _peerEnded;
        public int PeerScore => _peerScore;
        public bool Disconnected => _transport == null || _transport.IsClosed;

        public void Dispose()
        {
            if (!_isHost) return;
            _events.Unsubscribe<OrderCreated>(OnCreated);
            _events.Unsubscribe<OrderServed>(OnHostServed);
        }

        /// <summary>The client's ticket tap: ask the host. Paid only when granted.</summary>
        public bool RequestServe(int orderId)
        {
            if (_isHost) return _queue.TryServe(orderId);

            OrderInstance order = _queue.Find(orderId);
            if (order == null || !order.IsReady || !_pending.Add(orderId)) return false;
            _transport.Send(PvpMessage.Of(PvpMessage.ServeRequest, orderId));
            return false;
        }

        public void SendScore(int total) => _transport.Send(new PvpMessage { type = PvpMessage.Score, a = total });
        public void SendEnd(int total) => _transport.Send(new PvpMessage { type = PvpMessage.End, a = total });

        public void Pump()
        {
            if (_transport == null) return;
            if (!_sentReady && _transport.IsConnected)
            {
                _sentReady = true;
                _transport.Send(PvpMessage.Of(PvpMessage.Ready));
            }

            while (_transport.TryReceive(out PvpMessage m))
            {
                switch (m.type)
                {
                    case PvpMessage.Ready:
                        _peerReady = true;
                        break;
                    case PvpMessage.Go:
                        Started = true;
                        break;
                    case PvpMessage.Order:
                        if (!_isHost) AddRemoteOrder(m);
                        break;
                    case PvpMessage.ServeRequest:
                        if (_isHost) Referee(m.id);
                        break;
                    case PvpMessage.Granted:
                        _pending.Remove(m.id);
                        _queue.TryServe(m.id);
                        break;
                    case PvpMessage.Taken:
                        _pending.Remove(m.id);
                        _queue.Withdraw(m.id, byPlayer: 1);
                        break;
                    case PvpMessage.Score:
                        _peerScore = m.a;
                        break;
                    case PvpMessage.End:
                        _peerScore = m.a;
                        _peerEnded = true;
                        break;
                }
            }

            // The host starts the match once both sides have loaded.
            if (_isHost && !Started && _sentReady && _peerReady)
            {
                Started = true;
                _transport.Send(PvpMessage.Of(PvpMessage.Go));
            }
        }

        // ------------------------------------------------------------------ host

        private void OnCreated(OrderCreated e)
        {
            OrderInstance o = _queue.Find(e.OrderId);
            if (o == null) return;

            var counts = new int[o.Lines.Length];
            for (int i = 0; i < counts.Length; i++) counts[i] = o.Lines[i].Required;
            _transport.Send(new PvpMessage
            {
                type = PvpMessage.Order, id = o.OrderId, a = IndexOf(_dishes, o.Dish), b = IndexOf(_customers, o.Customer),
                f = o.PatienceDuration, g = o.RewardScale, counts = counts
            });
        }

        /// <summary>The host served first: the client's copy is taken.</summary>
        private void OnHostServed(OrderServed e) => _transport.Send(PvpMessage.Of(PvpMessage.Taken, e.OrderId));

        private void Referee(int orderId)
        {
            OrderInstance o = _queue.Find(orderId);
            if (o != null && !o.IsClosed)
            {
                _queue.Withdraw(orderId, byPlayer: 1);   // the client got there first
                _transport.Send(PvpMessage.Of(PvpMessage.Granted, orderId));
            }
            else _transport.Send(PvpMessage.Of(PvpMessage.Taken, orderId));
        }

        // ------------------------------------------------------------------ client

        private void AddRemoteOrder(PvpMessage m)
        {
            if (m.a < 0 || m.a >= _dishes.Count || m.b < 0 || m.b >= _customers.Count) return;

            DishDefinition dish = _dishes[m.a];
            int[] counts = m.counts ?? new int[0];
            OrderInstance order = OrderInstance.Create(m.id, dish, _customers[m.b], Time.time, m.f,
                count => count);
            for (int i = 0; i < order.Lines.Length && i < counts.Length; i++) order.Lines[i].Required = counts[i];
            order.RewardScale = m.g;
            _queue.Add(order);
        }

        private static int IndexOf<T>(IReadOnlyList<T> list, T item) where T : class
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == item) return i;
            return -1;
        }
    }

    /// <summary>
    /// The online client's view of its own line: everything is local except serving, which asks
    /// the host first (<see cref="OnlineSync.RequestServe"/>). The HUD's ticket taps go through here.
    /// </summary>
    public sealed class RefereedOrderQueue : IOrderQueue
    {
        private readonly LocalOrderQueue _inner;

        public RefereedOrderQueue(LocalOrderQueue inner) => _inner = inner;

        public OnlineSync Sync { get; set; }

        public int ActiveCount => _inner.ActiveCount;
        public OrderInstance this[int index] => _inner[index];
        public OrderInstance Find(int orderId) => _inner.Find(orderId);
        public void Add(OrderInstance order) => _inner.Add(order);
        public void SubmitIngredient(IngredientDefinition ingredient, Vector2 position) => _inner.SubmitIngredient(ingredient, position);
        public bool TryServe(int orderId) => Sync != null ? Sync.RequestServe(orderId) : _inner.TryServe(orderId);
        public OrderInstance Front() => _inner.Front();
        public void Tick(float delta) => _inner.Tick(delta);
        public bool FrontOnly { get => _inner.FrontOnly; set => _inner.FrontOnly = value; }
    }
}

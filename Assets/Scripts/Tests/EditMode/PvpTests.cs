using System.Collections.Generic;
using NUnit.Framework;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using SliceNServe.Pvp;
using UnityEngine;

namespace SliceNServe.Tests
{
    /// <summary>§5 PvP rules without a scene: the shared line, the match, and online refereeing.</summary>
    public sealed class PvpTests
    {
        private IngredientDefinition _tomato, _cheese;
        private DishDefinition _dish;
        private CustomerDefinition _customer;
        private RuntimeTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _tuning = TestData.Tuning();
            _tomato = TestData.Ingredient("tomato");
            _cheese = TestData.Ingredient("cheese");
            _dish = TestData.Dish("d", 10, (_tomato, 1), (_cheese, 1));
            _customer = TestData.Customer("c");
        }

        [TearDown] public void TearDown() => TestData.Cleanup();

        private OrderInstance NewOrder(int id) => OrderInstance.Create(id, _dish, _customer, 0f, 20f);

        private static void Fill(LocalOrderQueue queue, OrderInstance order)
        {
            foreach (OrderLine line in order.Lines)
                for (int n = line.Fulfilled; n < line.Required; n++) queue.SubmitIngredient(line.Ingredient, Vector2.zero);
        }

        // ------------------------------------------------------------ shared tickets

        private (SharedOrderQueue shared, LocalOrderQueue p1, LocalOrderQueue p2, EventBus b1, EventBus b2) Shared()
        {
            var b1 = new EventBus();
            var b2 = new EventBus();
            var p1 = new LocalOrderQueue(b1, _tuning);
            var p2 = new LocalOrderQueue(b2, _tuning);
            return (new SharedOrderQueue(new[] { p1, p2 }, new[] { b1, b2 }), p1, p2, b1, b2);
        }

        [Test]
        public void EveryCustomer_JoinsBothLines_WithTheirOwnProgress()
        {
            var (shared, p1, p2, _, _) = Shared();
            shared.Add(NewOrder(7));

            Assert.That(p1.Find(7), Is.Not.Null);
            Assert.That(p2.Find(7), Is.Not.Null);
            Assert.That(p2.Find(7), Is.Not.SameAs(p1.Find(7)), "each player's progress is their own");

            p1.SubmitIngredient(_tomato, Vector2.zero);
            Assert.That(p1.Find(7).Lines[0].Fulfilled, Is.EqualTo(1));
            Assert.That(p2.Find(7).Lines[0].Fulfilled, Is.EqualTo(0));
        }

        [Test]
        public void FirstToServe_GetsTheCash_AndTheOrderLeavesTheOtherLine()
        {
            var (shared, p1, p2, b1, b2) = Shared();
            var paid1 = new List<OrderServed>();
            var paid2 = new List<OrderServed>();
            var taken1 = new List<OrderTaken>();
            b1.Subscribe<OrderServed>(paid1.Add);
            b2.Subscribe<OrderServed>(paid2.Add);
            b1.Subscribe<OrderTaken>(taken1.Add);
            shared.Add(NewOrder(3));

            Fill(p1, p1.Find(3));
            Fill(p2, p2.Find(3));
            Assert.That(p1.Find(3).IsReady && p2.Find(3).IsReady, Is.True, "both raced it to ready");

            Assert.That(p2.TryServe(3), Is.True, "player 2 hands it over first");
            Assert.That(paid2.Count, Is.EqualTo(1));
            Assert.That(p1.Find(3), Is.Null, "gone from player 1's line");
            Assert.That(taken1.Count, Is.EqualTo(1));
            Assert.That(taken1[0].ByPlayer, Is.EqualTo(1));
            Assert.That(p1.TryServe(3), Is.False, "too late");
            Assert.That(paid1, Is.Empty);
        }

        [Test]
        public void AnOrderNobodyServes_ExpiresForBoth()
        {
            var (shared, p1, p2, _, _) = Shared();
            shared.Add(NewOrder(1));
            shared.Tick(25f);
            Assert.That(p1.ActiveCount + p2.ActiveCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------ the match

        [Test]
        public void Match_EndsOnTheClock_AndTheHigherScoreWins()
        {
            var match = new PvpMatch(2, 10f);
            int ended = 0;
            match.Ended += () => ended++;

            match.Tick(5f);
            Assert.That(match.Remaining, Is.EqualTo(10f), "the clock waits for Start");
            match.Start();
            match.AddScore(0, 30);
            match.AddScore(1, 45);
            match.Tick(9.9f);
            Assert.That(match.Finished, Is.False);
            match.Tick(0.2f);

            Assert.That(match.Finished, Is.True);
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(match.Winner, Is.EqualTo(1));
            match.AddScore(0, 100);
            Assert.That(match.Score(0), Is.EqualTo(30), "no scoring after the whistle");
        }

        [Test]
        public void Match_Draw_HasNoWinner()
        {
            var match = new PvpMatch(2, 1f);
            match.AddScore(0, 20);
            match.AddScore(1, 20);
            Assert.That(match.Winner, Is.EqualTo(-1));
        }

        // ------------------------------------------------------------ online

        private sealed class Side
        {
            public EventBus Bus;
            public LocalOrderQueue Queue;
            public OnlineSync Sync;
            public readonly List<OrderServed> Paid = new List<OrderServed>();
        }

        private (Side host, Side client) Online()
        {
            var (ht, ct) = LoopbackTransport.Pair();
            var dishes = new[] { _dish };
            var customers = new[] { _customer };
            Side Make(IMatchTransport t, bool isHost)
            {
                var s = new Side { Bus = new EventBus() };
                s.Queue = new LocalOrderQueue(s.Bus, _tuning);
                s.Sync = new OnlineSync(t, isHost, s.Queue, s.Bus, dishes, customers);
                s.Bus.Subscribe<OrderServed>(s.Paid.Add);
                return s;
            }
            Side host = Make(ht, true), client = Make(ct, false);

            for (int i = 0; i < 3; i++) { host.Sync.Pump(); client.Sync.Pump(); }
            Assert.That(host.Sync.Started && client.Sync.Started, Is.True, "ready → go");
            return (host, client);
        }

        private static void PumpBoth(Side a, Side b)
        {
            for (int i = 0; i < 3; i++) { a.Sync.Pump(); b.Sync.Pump(); }
        }

        [Test]
        public void Online_TheHostsCustomers_ReachTheClient()
        {
            var (host, client) = Online();
            host.Queue.Add(OrderInstance.Create(5, _dish, _customer, 0f, 20f, c => c * 2));
            PumpBoth(host, client);

            OrderInstance copy = client.Queue.Find(5);
            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.Lines[0].Required, Is.EqualTo(2), "the demand level travels with it");
            Assert.That(copy.PatienceDuration, Is.EqualTo(20f));
        }

        [Test]
        public void Online_TheClientServesFirst_IsGrantedAndPaid()
        {
            var (host, client) = Online();
            host.Queue.Add(NewOrder(9));
            PumpBoth(host, client);

            Fill(client.Queue, client.Queue.Find(9));
            Assert.That(client.Sync.RequestServe(9), Is.False, "a request, not a serve");
            Assert.That(client.Paid, Is.Empty);
            PumpBoth(host, client);

            Assert.That(client.Paid.Count, Is.EqualTo(1), "granted");
            Assert.That(host.Queue.Find(9), Is.Null, "taken from the host's line");
            Assert.That(host.Paid, Is.Empty);
        }

        [Test]
        public void Online_BothServeAtOnce_OnlyTheFirstIsPaid()
        {
            var (host, client) = Online();
            host.Queue.Add(NewOrder(4));
            PumpBoth(host, client);
            Fill(host.Queue, host.Queue.Find(4));
            Fill(client.Queue, client.Queue.Find(4));

            client.Sync.RequestServe(4);            // in flight...
            Assert.That(host.Queue.TryServe(4), Is.True);   // ...but the host is first
            PumpBoth(host, client);

            Assert.That(host.Paid.Count, Is.EqualTo(1));
            Assert.That(client.Paid, Is.Empty);
            Assert.That(client.Queue.Find(4), Is.Null, "the client's copy is taken");
        }

        [Test]
        public void Online_ScoresAndTheEndTravel()
        {
            var (host, client) = Online();
            client.Sync.SendScore(40);
            host.Sync.SendEnd(55);
            PumpBoth(host, client);

            Assert.That(host.Sync.PeerScore, Is.EqualTo(40));
            Assert.That(client.Sync.PeerScore, Is.EqualTo(55));
            Assert.That(client.Sync.PeerEnded, Is.True);
        }

        [Test]
        public void Tcp_HostAndJoin_OnThisMachine_ExchangeMessages()
        {
            const int port = 47781;
            TcpMatchTransport host = TcpMatchTransport.Host(port);
            TcpMatchTransport client = TcpMatchTransport.Join("127.0.0.1", port);
            try
            {
                float until = Time.realtimeSinceStartup + 5f;
                while ((!host.IsConnected || !client.IsConnected) && Time.realtimeSinceStartup < until) System.Threading.Thread.Sleep(10);
                Assert.That(host.IsConnected && client.IsConnected, Is.True, host.Status + " / " + client.Status);

                host.Send(new PvpMessage { type = PvpMessage.Start, a = 1234, f = 90f });
                PvpMessage got = null;
                until = Time.realtimeSinceStartup + 5f;
                while (!client.TryReceive(out got) && Time.realtimeSinceStartup < until) System.Threading.Thread.Sleep(10);

                Assert.That(got, Is.Not.Null);
                Assert.That(got.type, Is.EqualTo(PvpMessage.Start));
                Assert.That(got.a, Is.EqualTo(1234));

                client.Close();
                until = Time.realtimeSinceStartup + 5f;
                while (!host.IsClosed && Time.realtimeSinceStartup < until) System.Threading.Thread.Sleep(10);
                Assert.That(host.IsClosed, Is.True, "the host notices the other player leaving");
            }
            finally
            {
                host.Close();
                client.Close();
            }
        }

        [Test]
        public void Matchmaking_TwoSearchers_OnThisMachine_FindEachOther()
        {
            // Different ticket orders and TCP ports from the game's, so a running copy can't interfere.
            var a = new LanMatchmaker(tcpPortBase: 47790);
            var b = new LanMatchmaker(tcpPortBase: 47800);
            a.Start();
            b.Start();
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                float last = 0f;
                while (clock.Elapsed.TotalSeconds < 12
                       && (a.Phase != LanMatchmaker.State.Matched || b.Phase != LanMatchmaker.State.Matched)
                       && a.Phase != LanMatchmaker.State.Failed && b.Phase != LanMatchmaker.State.Failed)
                {
                    float now = (float)clock.Elapsed.TotalSeconds;
                    a.Tick(now - last);
                    b.Tick(now - last);
                    last = now;
                    System.Threading.Thread.Sleep(15);
                }

                Assert.That(a.Phase, Is.EqualTo(LanMatchmaker.State.Matched), a.Status);
                Assert.That(b.Phase, Is.EqualTo(LanMatchmaker.State.Matched), b.Status);
                Assert.That(a.IsHost ^ b.IsHost, Is.True, "exactly one hosts");
                Assert.That(a.Transport.IsConnected && b.Transport.IsConnected, Is.True);

                // And they can talk.
                LanMatchmaker host = a.IsHost ? a : b, guest = a.IsHost ? b : a;
                host.Transport.Send(new PvpMessage { type = PvpMessage.Start, a = 99 });
                PvpMessage got = null;
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (!guest.Transport.TryReceive(out got) && wait.Elapsed.TotalSeconds < 5) System.Threading.Thread.Sleep(10);
                Assert.That(got?.a, Is.EqualTo(99));
            }
            finally
            {
                a.Transport?.Close();
                b.Transport?.Close();
                a.Cancel();
                b.Cancel();
            }
        }
    }
}

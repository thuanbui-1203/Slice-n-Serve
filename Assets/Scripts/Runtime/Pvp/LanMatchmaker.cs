using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace SliceNServe.Pvp
{
    /// <summary>
    /// "Find match" on the local network — no address to type. Every searching game listens for
    /// a beacon on a discovery port for a moment; if it hears one it joins that game, otherwise it
    /// becomes the host and broadcasts its own beacon until somebody joins. If two games become
    /// hosts at the same moment, the one with the lower ticket gives way and joins the other.
    ///
    /// UDP broadcast on the LAN plus the existing <see cref="TcpMatchTransport"/>. Across the
    /// internet this would be a relay service's job (see the PvP plan). Main-thread, non-blocking:
    /// call <see cref="Tick"/> every frame.
    /// </summary>
    public sealed class LanMatchmaker
    {
        public const int DiscoveryPort = 47778;
        private const string Magic = "SNS-MATCH-1";
        private const float BeaconInterval = 0.4f;

        public enum State { Listening, Hosting, Joining, Matched, Failed }

        private readonly int _ticket;
        private readonly int _tcpPortBase;
        private readonly bool _loopbackBeacons;
        private UdpClient _listen, _send;
        private float _listenUntil, _nextBeacon, _time;
        private int _hostPort;

        public State Phase { get; private set; }
        public IMatchTransport Transport { get; private set; }
        public bool IsHost { get; private set; }
        public string Status { get; private set; } = "";

        /// <param name="loopbackBeacons">Also send beacons to this machine directly: two copies of the
        /// game on one PC (and the tests) find each other even where broadcast does not loop back.</param>
        public LanMatchmaker(int tcpPortBase = TcpMatchTransport.DefaultPort, bool loopbackBeacons = true, int? ticket = null)
        {
            _tcpPortBase = tcpPortBase;
            _loopbackBeacons = loopbackBeacons;
            _ticket = ticket ?? new System.Random(Environment.TickCount ^ Guid.NewGuid().GetHashCode()).Next(1, int.MaxValue);
        }

        public void Start()
        {
            try
            {
                _listen = new UdpClient { ExclusiveAddressUse = false, EnableBroadcast = true };
                _listen.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listen.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _send = new UdpClient { EnableBroadcast = true };
            }
            catch (Exception e)
            {
                Fail($"Can't search the network: {e.Message}");
                return;
            }

            // A random wait, so two players who press at the same moment rarely both host.
            _listenUntil = UnityEngine.Random.Range(1.0f, 2.5f);
            Phase = State.Listening;
            Status = "Looking for a player on your network...";
        }

        public void Tick(float delta)
        {
            if (Phase == State.Matched || Phase == State.Failed) return;
            _time += delta;

            // Anyone else out there?
            if (TryHearBeacon(out IPAddress from, out int port, out int ticket))
            {
                // Listening: join the first host heard. Hosting: give way to a lower ticket.
                if (Phase == State.Listening || (Phase == State.Hosting && ticket < _ticket && !Transport.IsConnected))
                {
                    if (Phase == State.Hosting) Transport.Close();
                    Join(from, port);
                    return;
                }
            }

            switch (Phase)
            {
                case State.Listening:
                    if (_time >= _listenUntil) Host();
                    break;

                case State.Hosting:
                    if (Transport.IsConnected) { Matched("A player joined!"); break; }
                    if (Transport.IsClosed) { Fail(Transport.Status); break; }
                    if (_time >= _nextBeacon)
                    {
                        _nextBeacon = _time + BeaconInterval;
                        SendBeacon();
                    }
                    break;

                case State.Joining:
                    if (Transport.IsConnected) Matched("Found a player!");
                    else if (Transport.IsClosed) { Phase = State.Listening; _listenUntil = _time + UnityEngine.Random.Range(0.5f, 1.5f); }
                    break;
            }
        }

        public void Cancel()
        {
            if (Phase != State.Matched) Transport?.Close();
            Stop();
            if (Phase != State.Matched) Phase = State.Failed;
        }

        // ------------------------------------------------------------------ steps

        private void Host()
        {
            // The first free port from the base: two copies on one PC can't share one.
            for (int i = 0; i < 10; i++)
            {
                int port = _tcpPortBase + i;
                if (!PortFree(port)) continue;
                _hostPort = port;
                Transport = TcpMatchTransport.Host(port);
                IsHost = true;
                Phase = State.Hosting;
                Status = "No one yet — waiting for a player to search...";
                _nextBeacon = 0f;
                return;
            }
            Fail("No free port to host on.");
        }

        private void Join(IPAddress host, int port)
        {
            Transport = TcpMatchTransport.Join(host.ToString(), port);
            IsHost = false;
            Phase = State.Joining;
            Status = "Found a game, connecting...";
        }

        private void Matched(string status)
        {
            Phase = State.Matched;
            Status = status;
            Stop();
        }

        private void Fail(string status)
        {
            Phase = State.Failed;
            Status = status;
            Stop();
        }

        private void Stop()
        {
            try { _listen?.Close(); } catch (Exception) { }
            try { _send?.Close(); } catch (Exception) { }
            _listen = null;
            _send = null;
        }

        // ------------------------------------------------------------------ beacons

        private void SendBeacon()
        {
            byte[] data = Encoding.ASCII.GetBytes($"{Magic} {_hostPort} {_ticket}");
            try { _send.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)); } catch (Exception) { }
            if (_loopbackBeacons)
                try { _send.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort)); } catch (Exception) { }
        }

        private bool TryHearBeacon(out IPAddress from, out int port, out int ticket)
        {
            from = null;
            port = ticket = 0;
            if (_listen == null) return false;

            try
            {
                while (_listen.Available > 0)
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    string[] parts = Encoding.ASCII.GetString(_listen.Receive(ref ep)).Split(' ');
                    if (parts.Length != 3 || parts[0] != Magic) continue;
                    if (!int.TryParse(parts[1], out port) || !int.TryParse(parts[2], out ticket)) continue;
                    if (ticket == _ticket) continue;   // our own beacon, looped back
                    from = ep.Address;
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        private static bool PortFree(int port)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Any, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch (SocketException) { return false; }
        }
    }
}

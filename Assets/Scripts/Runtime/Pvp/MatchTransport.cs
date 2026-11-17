using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SliceNServe.Pvp
{
    /// <summary>
    /// One message of the online PvP protocol. Boards are never synced — only these small events
    /// (docs/plans/section-5-pvp.md). One flat shape for every type keeps it JsonUtility-friendly.
    /// </summary>
    [Serializable]
    public sealed class PvpMessage
    {
        public const string Start = "start";        // host → client: seed, duration (lobby)
        public const string Ready = "ready";        // both: my scene is loaded
        public const string Go = "go";              // host → client: the clock starts now
        public const string Order = "order";        // host → client: a customer arrived
        public const string ServeRequest = "serve"; // client → host: I want to serve order id
        public const string Granted = "granted";    // host → client: it's yours
        public const string Taken = "taken";        // host → client: the host served id first (or it's gone)
        public const string Score = "score";        // both: my total
        public const string End = "end";            // both: my clock ran out, final score

        public string type;
        public int id;
        public int a, b;       // order: dish index, customer index · start: seed
        public float f, g;     // order: patience, reward scale · start: duration
        public int[] counts;   // order: required count per recipe line

        public static PvpMessage Of(string type, int id = 0) => new PvpMessage { type = type, id = id };
    }

    /// <summary>The connection between two PvP players. Messages are polled on the main thread.</summary>
    public interface IMatchTransport
    {
        bool IsConnected { get; }
        /// <summary>The other side went away (or never came).</summary>
        bool IsClosed { get; }
        string Status { get; }
        void Send(PvpMessage message);
        bool TryReceive(out PvpMessage message);
        void Close();
    }

    /// <summary>
    /// Online PvP over a plain TCP connection on the local network: one device hosts on a port,
    /// the other joins by IP. No packages or online services needed; matchmaking across the
    /// internet would replace this with a relay (see the PvP plan). Reads on a background
    /// thread, one JSON message per line; everything else happens on the main thread.
    /// </summary>
    public sealed class TcpMatchTransport : IMatchTransport
    {
        public const int DefaultPort = 7777;

        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        private readonly object _writeLock = new object();
        private TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;
        private volatile bool _connected, _closed;
        private volatile string _status = "";

        public bool IsConnected => _connected && !_closed;
        public bool IsClosed => _closed;
        public string Status => _status;

        private TcpMatchTransport() => Application.quitting += Close;

        /// <summary>Waits for one player to join on <paramref name="port"/>.</summary>
        public static TcpMatchTransport Host(int port = DefaultPort)
        {
            var t = new TcpMatchTransport();
            try
            {
                t._listener = new TcpListener(IPAddress.Any, port);
                t._listener.Start();
                t._status = $"Waiting for a player on {string.Join(" / ", LocalAddresses())} (port {port})...";
                new Thread(t.AcceptAndRead) { IsBackground = true, Name = "PvP host" }.Start();
            }
            catch (Exception e) { t.Fail($"Could not host: {e.Message}"); }
            return t;
        }

        /// <summary>Connects to a host at <paramref name="address"/>.</summary>
        public static TcpMatchTransport Join(string address, int port = DefaultPort)
        {
            var t = new TcpMatchTransport { _status = $"Connecting to {address}..." };
            new Thread(() => t.ConnectAndRead(address, port)) { IsBackground = true, Name = "PvP client" }.Start();
            return t;
        }

        /// <summary>This machine's IPv4 addresses, for the host to read out to the other player.</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) list.Add(ip.ToString());
            }
            catch (Exception) { /* no network: the list stays empty */ }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }

        private void AcceptAndRead()
        {
            try
            {
                _client = _listener.AcceptTcpClient();
                _listener.Stop();
                Opened("A player joined.");
                ReadLoop();
            }
            catch (Exception e) { Fail(_closed ? "Closed." : $"Connection lost: {e.Message}"); }
        }

        private void ConnectAndRead(string address, int port)
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(address, port);
                Opened("Connected. Waiting for the host to start...");
                ReadLoop();
            }
            catch (Exception e) { Fail(_closed ? "Closed." : $"Could not connect: {e.Message}"); }
        }

        private void Opened(string status)
        {
            _client.NoDelay = true;
            _stream = _client.GetStream();
            _connected = true;
            _status = status;
        }

        private void ReadLoop()
        {
            using (var reader = new StreamReader(_stream, Encoding.UTF8))
            {
                string line;
                while (!_closed && (line = reader.ReadLine()) != null)
                    if (line.Length > 0) _inbox.Enqueue(line);
            }
            Fail("The other player left.");
        }

        public void Send(PvpMessage message)
        {
            if (!IsConnected) return;
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message) + "\n");
            try
            {
                lock (_writeLock) _stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception e) { Fail($"Connection lost: {e.Message}"); }
        }

        public bool TryReceive(out PvpMessage message)
        {
            message = null;
            while (_inbox.TryDequeue(out string line))
            {
                try { message = JsonUtility.FromJson<PvpMessage>(line); }
                catch (Exception) { continue; }   // not ours: skip it
                if (message != null && !string.IsNullOrEmpty(message.type)) return true;
            }
            return false;
        }

        private void Fail(string status)
        {
            _status = status;
            Close();
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _connected = false;
            Application.quitting -= Close;
            try { _client?.Close(); } catch (Exception) { }
            try { _listener?.Stop(); } catch (Exception) { }
        }
    }

    /// <summary>Two in-memory ends wired to each other: online PvP without a network, for tests.</summary>
    public sealed class LoopbackTransport : IMatchTransport
    {
        private readonly Queue<string> _inbox = new Queue<string>();
        private LoopbackTransport _peer;
        private bool _closed;

        public static (LoopbackTransport host, LoopbackTransport client) Pair()
        {
            var a = new LoopbackTransport();
            var b = new LoopbackTransport();
            a._peer = b;
            b._peer = a;
            return (a, b);
        }

        public bool IsConnected => !_closed && _peer != null && !_peer._closed;
        public bool IsClosed => _closed || _peer == null || _peer._closed;
        public string Status => IsConnected ? "Connected (loopback)." : "Closed.";

        public void Send(PvpMessage message)
        {
            if (IsConnected) _peer._inbox.Enqueue(JsonUtility.ToJson(message));
        }

        public bool TryReceive(out PvpMessage message)
        {
            message = null;
            if (_inbox.Count == 0) return false;
            message = JsonUtility.FromJson<PvpMessage>(_inbox.Dequeue());
            return true;
        }

        public void Close() => _closed = true;
    }
}

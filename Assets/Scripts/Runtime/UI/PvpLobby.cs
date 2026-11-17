using System;
using SliceNServe.Pvp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>
    /// "Enter PvP match": press Find match and the <see cref="LanMatchmaker"/> pairs you with
    /// another player searching on the same network — no address to type. Once connected, the
    /// host picks the seed and both start (<see cref="Starting"/>), carrying the connection
    /// across the scene reload.
    /// </summary>
    public sealed class PvpLobby : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Button find;
        [SerializeField] private TMP_Text findLabel;
        [SerializeField] private Button back;
        [SerializeField] private TMP_Text status;

        private Action _onBack;
        private LanMatchmaker _matchmaker;
        private IMatchTransport _transport;
        private bool _isHost, _started;

        public bool IsOpen => gameObject.activeSelf;
        public bool IsSearching => _matchmaker != null;

        /// <summary>Raised once both players are connected: the runner sets up the match and reloads.</summary>
        public event Action<IMatchTransport, bool, int, float> Starting;

        private void Awake()
        {
            find.onClick.AddListener(ToggleSearch);
            back.onClick.AddListener(Close);
        }

        public void Open(Action onBack)
        {
            _onBack = onBack;
            _started = false;
            gameObject.SetActive(true);
            if (group != null) { group.alpha = 1f; group.blocksRaycasts = true; }
            SetStatus("Find another player on the same network (Wi-Fi or cable).\nBoth of you press Find match.");
            SetSearching(false);
        }

        public void Close()
        {
            StopSearching();
            if (_transport != null && !_started) _transport.Close();
            _transport = null;
            gameObject.SetActive(false);
            _onBack?.Invoke();
        }

        private void ToggleSearch()
        {
            if (_matchmaker != null || _transport != null)
            {
                StopSearching();
                _transport?.Close();
                _transport = null;
                SetStatus("Search cancelled.");
                SetSearching(false);
                return;
            }

            _matchmaker = new LanMatchmaker();
            _matchmaker.Start();
            SetSearching(true);
        }

        private void StopSearching()
        {
            _matchmaker?.Cancel();
            _matchmaker = null;
        }

        private void Update()
        {
            if (_started) return;

            if (_matchmaker != null)
            {
                _matchmaker.Tick(Time.unscaledDeltaTime);
                SetStatus(_matchmaker.Status);
                if (_matchmaker.Phase == LanMatchmaker.State.Failed)
                {
                    _matchmaker = null;
                    SetSearching(false);
                    return;
                }
                if (_matchmaker.Phase != LanMatchmaker.State.Matched) return;

                _transport = _matchmaker.Transport;
                _isHost = _matchmaker.IsHost;
                _matchmaker = null;
            }

            if (_transport == null) return;
            if (_transport.IsClosed) { SetStatus(_transport.Status); _transport = null; SetSearching(false); return; }
            if (!_transport.IsConnected) return;

            if (_isHost)
            {
                int seed = Environment.TickCount;
                float duration = MatchSetup.DefaultDurationSeconds;
                _transport.Send(new PvpMessage { type = PvpMessage.Start, a = seed, f = duration });
                Begin(seed, duration);
                return;
            }

            // The joiner waits for the host's start message.
            while (_transport.TryReceive(out PvpMessage m))
                if (m.type == PvpMessage.Start) { Begin(m.a, m.f > 0f ? m.f : MatchSetup.DefaultDurationSeconds); return; }
        }

        private void Begin(int seed, float duration)
        {
            _started = true;
            SetStatus("Match found! Starting...");
            Starting?.Invoke(_transport, _isHost, seed, duration);
        }

        private void SetSearching(bool searching)
        {
            if (findLabel != null) findLabel.SetText(searching ? "Cancel" : "Find match");
        }

        private void SetStatus(string text)
        {
            if (status != null) status.SetText(text);
        }
    }
}

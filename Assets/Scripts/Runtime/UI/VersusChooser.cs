using System;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>"Play with Friends" → who with: a friend on this device, or the CPU (to try the
    /// mode alone). Knows nothing about matches; the runner hands it the three actions.</summary>
    public sealed class VersusChooser : MonoBehaviour
    {
        [SerializeField] private Button friend;
        [SerializeField] private Button cpu;
        [SerializeField] private Button back;

        private Action _onFriend, _onCpu, _onBack;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            friend.onClick.AddListener(() => Choose(_onFriend));
            cpu.onClick.AddListener(() => Choose(_onCpu));
            back.onClick.AddListener(Close);
        }

        public void Open(Action onFriend, Action onCpu, Action onBack)
        {
            _onFriend = onFriend;
            _onCpu = onCpu;
            _onBack = onBack;
            gameObject.SetActive(true);
        }

        public void Close() => Choose(_onBack);

        private void Choose(Action action)
        {
            if (!IsOpen) return;
            gameObject.SetActive(false);
            action?.Invoke();
        }
    }
}

using SliceNServe.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SliceNServe.UI
{
    /// <summary>Reputation as a row of hearts. A lost heart greys out with a shake.</summary>
    public sealed class ReputationReadout : MonoBehaviour
    {
        [SerializeField] private Image[] hearts;

        private static readonly Color Lost = new Color(0.25f, 0.2f, 0.22f, 0.6f);

        private EventBus _events;
        private int _reputation;
        private int _lostIndex = -1;
        private float _shake;

        public void Initialize(EventBus events, int startReputation)
        {
            _events = events;
            _events.Subscribe<ReputationChanged>(OnChanged);

            _reputation = startReputation;
            for (int i = 0; i < hearts.Length; i++)
            {
                hearts[i].gameObject.SetActive(i < startReputation);
                hearts[i].color = Color.white;
            }
        }

        private void OnDestroy() => _events?.Unsubscribe<ReputationChanged>(OnChanged);

        private void OnChanged(ReputationChanged e)
        {
            _reputation = e.Reputation;
            for (int i = 0; i < hearts.Length; i++)
                hearts[i].color = i < _reputation ? Color.white : Lost;

            if (e.Delta < 0)
            {
                _lostIndex = Mathf.Clamp(_reputation, 0, hearts.Length - 1);
                _shake = 1f;
            }
        }

        private void Update()
        {
            if (_lostIndex < 0) return;

            _shake = Mathf.MoveTowards(_shake, 0f, Time.unscaledDeltaTime * 1.5f);
            var rt = (RectTransform)hearts[_lostIndex].transform;
            rt.localScale = Vector3.one * (1f + 0.6f * _shake);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 40f) * 20f * _shake);
            if (_shake <= 0f) _lostIndex = -1;
        }
    }
}

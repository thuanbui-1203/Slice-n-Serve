using TMPro;
using UnityEngine;

namespace SliceNServe.UI
{
    /// <summary>The PvP scoreboard: both players' cash, the match clock, and a centre message
    /// ("Waiting for your rival...", "3, 2, 1"). Hidden in solo play.</summary>
    public sealed class PvpHud : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text leftName;
        [SerializeField] private TMP_Text leftScore;
        [SerializeField] private TMP_Text rightName;
        [SerializeField] private TMP_Text rightScore;
        [SerializeField] private TMP_Text clock;
        [SerializeField] private TMP_Text message;

        private int _left = -1, _right = -1, _seconds = -1;
        private float _punchLeft, _punchRight;

        public void Show(string left, string right)
        {
            gameObject.SetActive(true);
            if (group != null) group.alpha = 1f;
            leftName.SetText(left);
            rightName.SetText(right);
            SetMessage("");
        }

        public void Hide() => gameObject.SetActive(false);

        public void SetMessage(string text)
        {
            if (message != null) message.SetText(text);
        }

        public void Set(float secondsLeft, int left, int right)
        {
            int s = Mathf.CeilToInt(secondsLeft);
            if (s != _seconds)
            {
                _seconds = s;
                clock.SetText("{0}:{1:00}", s / 60, s % 60);
                clock.color = s <= 10 ? new Color(1f, 0.4f, 0.3f) : Color.white;
            }
            if (left != _left) { if (_left >= 0) _punchLeft = 1f; _left = left; leftScore.SetText("${0}", left); }
            if (right != _right) { if (_right >= 0) _punchRight = 1f; _right = right; rightScore.SetText("${0}", right); }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _punchLeft = Mathf.MoveTowards(_punchLeft, 0f, dt * 3f);
            _punchRight = Mathf.MoveTowards(_punchRight, 0f, dt * 3f);
            leftScore.transform.localScale = Vector3.one * (1f + 0.3f * _punchLeft);
            rightScore.transform.localScale = Vector3.one * (1f + 0.3f * _punchRight);
        }
    }
}

using System;
using SliceNServe.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SliceNServe.Board
{
    /// <summary>
    /// Drag start → drag → release, on one code path for mouse, touch and pen (decision D9).
    /// Raises <see cref="OnLaunch"/> with the drag in world units.
    ///
    /// The Input System has no built-in Drag/Swipe interaction, so the release is read from the
    /// Board map's Launch action being cancelled.
    /// </summary>
    public sealed class AimInput : MonoBehaviour
    {
        [SerializeField] private Camera boardCamera;
        [SerializeField] private InputActionAsset actions;

        [Tooltip("Screen-space-overlay HUD buttons that sit over the board: a press on one is a "
               + "button press, never the start of a throw.")]
        [SerializeField] private RectTransform[] blockers;

        private InputActionMap _board;
        private InputAction _aim;
        private InputAction _launch;
        private RuntimeTuning _tuning;

        // The device that started the drag. Reading its position directly, rather than the Aim
        // action's last value, avoids a stale position when a touch press and its first position
        // arrive in the same input event.
        private Pointer _pointer;
        private Vector2 _dragStartScreen;
        private bool _dragging;

        /// <summary>Live drag in world units, or zero when not dragging. For aim-guide rendering.</summary>
        public Vector2 CurrentDragWorld { get; private set; }

        /// <summary>World position the drag started from, for the rubber-band visual.</summary>
        public Vector2 DragStartWorld { get; private set; }

        public bool IsDragging => _dragging;

        /// <summary>False while an overlay is up, so the tap that dismisses it cannot also aim.</summary>
        public bool Accepting { get; set; } = true;

        public event Action<Vector2> OnLaunch;

        /// <summary>Claims a press before it can start a throw: given the press's world position,
        /// returns true if it was used (a tap on the customers opens the orders panel).</summary>
        public Func<Vector2, bool> PressFilter { get; set; }

        /// <summary>Auto-throw mode: a press on the board is a tap (<see cref="OnTap"/>, in world
        /// units), never the start of a throw.</summary>
        public bool TapMode { get; set; }

        public event Action<Vector2> OnTap;

        public void Initialize(RuntimeTuning tuning)
        {
            _tuning = tuning;

            _board = actions.FindActionMap("Board", throwIfNotFound: true);
            _aim = _board.FindAction("Aim", throwIfNotFound: true);
            _launch = _board.FindAction("Launch", throwIfNotFound: true);

            _launch.started += OnPressStarted;
            _launch.canceled += OnPressReleased;
            _board.Enable();
        }

        private void OnDestroy()
        {
            if (_launch == null) return;

            _launch.started -= OnPressStarted;
            _launch.canceled -= OnPressReleased;
            _board.Disable();
        }

        /// <summary>Registers another screen-space UI rect that presses should never aim through.</summary>
        public void AddBlocker(RectTransform blocker)
        {
            if (blocker == null) return;
            int n = blockers?.Length ?? 0;
            Array.Resize(ref blockers, n + 1);
            blockers[n] = blocker;
        }


        // ------------------------------------------------------------------ §5: sharing a screen

        /// <summary>Split screen (§5): a press only aims if it starts inside this world rect — this
        /// player's half. Null (solo) accepts the whole camera.</summary>
        public Rect? Region { get; set; }

        /// <summary>Split screen on a touch device: track each finger separately, so two players
        /// can aim at once. Mouse and pen keep using the shared action.</summary>
        public bool MultiTouch { get; set; }

        public enum KeyScheme { None, Wasd, Arrows }

        /// <summary>Aiming from a keyboard, for a second player sharing a PC: left/right turn the
        /// aim, up/down set the power, the fire key throws (Space for WASD, Enter for arrows).</summary>
        public KeyScheme Keys { get; set; }

        private const float KeyTurnDegreesPerSecond = 110f;
        private float _keyAngle = 90f, _keyPower = 0.75f;
        private bool _keyAiming;
        private int _touchId = -1;

        public void CancelDrag()
        {
            _dragging = false;
            _keyAiming = false;
            _touchId = -1;
            CurrentDragWorld = Vector2.zero;
        }

        private void OnPressStarted(InputAction.CallbackContext ctx)
        {
            if (!Accepting) return;
            if (MultiTouch && ctx.control.device is Touchscreen) return;   // handled per finger in Update

            _pointer = ctx.control.device as Pointer;
            BeginPress(ReadScreenPosition());
        }

        private bool BeginPress(Vector2 screen)
        {
            if (_dragging) return false;
            if (boardCamera != null && !boardCamera.pixelRect.Contains(screen)) return false;
            if (OverBlocker(screen)) return false;
            if (Region.HasValue && !Region.Value.Contains(ScreenToWorld(screen))) return false;
            if (PressFilter != null && PressFilter(ScreenToWorld(screen))) return false;
            if (TapMode)
            {
                OnTap?.Invoke(ScreenToWorld(screen));
                return false;
            }

            _keyAiming = false;
            _dragStartScreen = screen;
            _dragging = true;
            CurrentDragWorld = Vector2.zero;
            DragStartWorld = ScreenToWorld(screen);
            return true;
        }

        private void OnPressReleased(InputAction.CallbackContext ctx)
        {
            if (!_dragging || _touchId >= 0 || _keyAiming) return;
            if (MultiTouch && ctx.control.device is Touchscreen) return;
            Release(ReadScreenPosition());
        }

        private void Release(Vector2 screen)
        {
            // Take the final position at release, not the last Update's.
            Vector2 dragWorld = ScreenToWorldDelta(screen - _dragStartScreen);
            CancelDrag();

            // A tap, or a drag inside the dead zone, must not fire a knife.
            if (!Accepting || !KnifeLauncher.IsValidDrag(dragWorld, _tuning)) return;

            OnLaunch?.Invoke(dragWorld);
        }

        private void Update()
        {
            if (MultiTouch) UpdateTouches();
            if (Keys != KeyScheme.None) UpdateKeys();

            if (!_dragging || _keyAiming || _touchId >= 0) return;
            CurrentDragWorld = ScreenToWorldDelta(ReadScreenPosition() - _dragStartScreen);
        }

        private void UpdateTouches()
        {
            if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled)
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();

            bool held = false;
            foreach (UnityEngine.InputSystem.EnhancedTouch.Touch t in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                UnityEngine.InputSystem.TouchPhase phase = t.phase;
                if (_touchId < 0)
                {
                    if (phase == UnityEngine.InputSystem.TouchPhase.Began && Accepting && BeginPress(t.screenPosition))
                    {
                        _touchId = t.touchId;
                        held = true;
                    }
                    continue;
                }
                if (t.touchId != _touchId) continue;

                held = true;
                if (phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    _touchId = -1;
                    Release(t.screenPosition);
                    return;
                }
                CurrentDragWorld = ScreenToWorldDelta(t.screenPosition - _dragStartScreen);
            }
            if (_touchId >= 0 && !held) CancelDrag();   // the finger vanished without an end
        }

        private void UpdateKeys()
        {
            Keyboard k = Keyboard.current;
            if (k == null || !Accepting || TapMode || (_dragging && !_keyAiming)) return;

            bool wasd = Keys == KeyScheme.Wasd;
            float turn = ((wasd ? k.aKey : k.leftArrowKey).isPressed ? 1f : 0f) - ((wasd ? k.dKey : k.rightArrowKey).isPressed ? 1f : 0f);
            float power = ((wasd ? k.wKey : k.upArrowKey).isPressed ? 1f : 0f) - ((wasd ? k.sKey : k.downArrowKey).isPressed ? 1f : 0f);
            bool fire = wasd ? k.spaceKey.wasPressedThisFrame : (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);

            if (turn != 0f || power != 0f) _keyAiming = true;
            if (!_keyAiming && !fire) return;

            float dt = Time.unscaledDeltaTime;
            _keyAngle = Mathf.Clamp(_keyAngle + turn * KeyTurnDegreesPerSecond * dt, 5f, 175f);
            _keyPower = Mathf.Clamp(_keyPower + power * dt, 0.2f, 1f);

            // The same drag a finger would make: pulled back, opposite the throw.
            float a = _keyAngle * Mathf.Deg2Rad;
            Vector2 drag = -new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (_keyPower * _tuning.Config.maxDragLength);
            _dragging = true;
            DragStartWorld = _tuning.LauncherPosition;
            CurrentDragWorld = drag;

            if (!fire) return;
            _keyAiming = false;
            _dragging = false;
            CurrentDragWorld = Vector2.zero;
            OnLaunch?.Invoke(drag);
        }

        // Not EventSystem.IsPointerOverGameObject: it is unreliable inside Input System callbacks.
        private bool OverBlocker(Vector2 screen)
        {
            if (blockers == null) return false;

            for (int i = 0; i < blockers.Length; i++)
            {
                RectTransform b = blockers[i];
                if (b != null && b.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(b, screen, null))
                    return true;
            }
            return false;
        }

        private Vector2 ReadScreenPosition()
            => _pointer != null ? _pointer.position.ReadValue() : _aim.ReadValue<Vector2>();

        private Vector2 ScreenToWorld(Vector2 screen)
            => boardCamera == null ? screen : (Vector2)boardCamera.ScreenToWorldPoint(screen);

        private Vector2 ScreenToWorldDelta(Vector2 screenDelta)
            => boardCamera == null
                ? screenDelta
                : (Vector2)boardCamera.ScreenToWorldPoint(screenDelta) - (Vector2)boardCamera.ScreenToWorldPoint(Vector2.zero);
    }
}

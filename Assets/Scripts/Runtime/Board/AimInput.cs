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

        public void CancelDrag()
        {
            _dragging = false;
            CurrentDragWorld = Vector2.zero;
        }

        private void OnPressStarted(InputAction.CallbackContext ctx)
        {
            if (!Accepting) return;

            _pointer = ctx.control.device as Pointer;
            Vector2 screen = ReadScreenPosition();
            if (boardCamera != null && !boardCamera.pixelRect.Contains(screen)) return;

            _dragStartScreen = screen;
            _dragging = true;
            CurrentDragWorld = Vector2.zero;
            DragStartWorld = ScreenToWorld(screen);
        }

        private void OnPressReleased(InputAction.CallbackContext _)
        {
            if (!_dragging) return;

            // Take the final position at release, not the last Update's.
            Vector2 dragWorld = ScreenToWorldDelta(ReadScreenPosition() - _dragStartScreen);
            CancelDrag();

            // A tap, or a drag inside the dead zone, must not fire a knife.
            if (!Accepting || !KnifeLauncher.IsValidDrag(dragWorld, _tuning)) return;

            OnLaunch?.Invoke(dragWorld);
        }

        private void Update()
        {
            if (!_dragging) return;

            CurrentDragWorld = ScreenToWorldDelta(ReadScreenPosition() - _dragStartScreen);
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

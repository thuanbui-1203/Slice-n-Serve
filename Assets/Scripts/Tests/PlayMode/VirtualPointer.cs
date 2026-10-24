using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SliceNServe.Tests
{
    /// <summary>
    /// Drives the Board action map through virtual Input System devices, so a test exercises the
    /// real input path — bindings, AimInput's state machine, the world-unit conversion — rather
    /// than calling the launcher directly.
    /// </summary>
    internal sealed class VirtualPointer
    {
        private readonly Mouse _mouse;
        private readonly Touchscreen _touch;
        private readonly InputSettings.EditorInputBehaviorInPlayMode _editorBehavior;
        private readonly InputSettings.BackgroundBehavior _backgroundBehavior;

        public VirtualPointer()
        {
            // Batch mode has no focused Game view; without these the devices are ignored.
            InputSettings settings = InputSystem.settings;
            _editorBehavior = settings.editorInputBehaviorInPlayMode;
            _backgroundBehavior = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            _mouse = InputSystem.AddDevice<Mouse>("TestMouse");
            _touch = InputSystem.AddDevice<Touchscreen>("TestTouch");
        }

        public void Dispose()
        {
            InputSystem.RemoveDevice(_mouse);
            InputSystem.RemoveDevice(_touch);
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorBehavior;
            InputSystem.settings.backgroundBehavior = _backgroundBehavior;
        }

        public void Mouse(Vector2 screen, bool pressed)
        {
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen, buttons = (ushort)(pressed ? 1 : 0) });
            InputSystem.Update();
        }

        public void Touch(Vector2 screen, UnityEngine.InputSystem.TouchPhase phase)
        {
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = 1, phase = phase, position = screen });
            InputSystem.Update();
        }
    }
}

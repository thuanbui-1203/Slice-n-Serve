using System.Collections;
using NUnit.Framework;
using SliceNServe.Board;
using SliceNServe.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace SliceNServe.Tests
{
    /// <summary>
    /// §1 workstream F6 / decision D9: one drag of the same world-unit length must give the
    /// same launch from the mouse and from touch, because they are the same gesture.
    /// </summary>
    public sealed class InputParityTests
    {
        private GameRunner _runner;
        private KnifeManager _knives;
        private AimInput _aim;
        private Camera _camera;
        private VirtualPointer _pointer;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            _runner = Object.FindAnyObjectByType<GameRunner>();
            _knives = Object.FindAnyObjectByType<KnifeManager>();
            _aim = Object.FindAnyObjectByType<AimInput>();
            _camera = Camera.main;
            _pointer = new VirtualPointer();
            _runner.StartRun();
        }

        [TearDown]
        public void TearDown() => _pointer?.Dispose();

        private Vector2 ToScreen(Vector2 world) => _camera.WorldToScreenPoint(world);

        [UnityTest]
        public IEnumerator MouseAndTouch_ProduceTheSameLaunch()
        {
            Vector2 start = new Vector2(2f, 0f), end = new Vector2(1f, -2.5f);
            Vector2? launched = null;
            _aim.OnLaunch += d => launched = d;

            _pointer.Mouse(ToScreen(start), false);
            _pointer.Mouse(ToScreen(start), true);
            yield return null;
            _pointer.Mouse(ToScreen(end), true);
            yield return null;
            Assert.That(_aim.IsDragging, Is.True);
            _pointer.Mouse(ToScreen(end), false);

            Assert.That(launched.HasValue, Is.True, "mouse drag-and-release must launch");
            Vector2 mouseDrag = launched.Value;
            Assert.That(_knives.CanLaunch, Is.False);

            _knives.ReturnAllKnives();
            launched = null;
            yield return null;

            _pointer.Touch(ToScreen(start), TouchPhase.Began);
            yield return null;
            _pointer.Touch(ToScreen(end), TouchPhase.Moved);
            yield return null;
            _pointer.Touch(ToScreen(end), TouchPhase.Ended);
            yield return null;

            Assert.That(launched.HasValue, Is.True, "touch drag-and-release must launch");
            Vector2 touchDrag = launched.Value;

            Assert.That(Vector2.Distance(mouseDrag, touchDrag), Is.LessThan(1e-3f));
            Assert.That(Vector2.Distance(mouseDrag, end - start), Is.LessThan(0.02f), "the drag is measured in world units");

            RuntimeTuning tuning = ServiceRegistry.Current.Tuning;
            Assert.That(KnifeLauncher.ResolvePower(mouseDrag, tuning), Is.EqualTo(KnifeLauncher.ResolvePower(touchDrag, tuning)).Within(1e-4f));
            Assert.That(Vector2.Dot(KnifeLauncher.ResolveDirection(mouseDrag), KnifeLauncher.ResolveDirection(touchDrag)), Is.GreaterThan(0.99999f));
        }

        [UnityTest]
        public IEnumerator ATap_LaunchesNothing()
        {
            bool launched = false;
            _aim.OnLaunch += _ => launched = true;
            Vector2 at = ToScreen(new Vector2(0f, 1f));

            _pointer.Mouse(at, false);
            _pointer.Mouse(at, true);
            yield return null;
            _pointer.Mouse(at + new Vector2(2f, 2f), false);
            yield return null;

            Assert.That(launched, Is.False);
            Assert.That(_knives.CanLaunch, Is.True);
        }
    }
}

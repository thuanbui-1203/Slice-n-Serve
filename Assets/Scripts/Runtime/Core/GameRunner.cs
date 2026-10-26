using SliceNServe.Board;
using SliceNServe.Orders;
using SliceNServe.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SliceNServe.Core
{
    /// <summary>
    /// Builds the run from the services GameBootstrap installed, closes the loop between them,
    /// and owns the run lifecycle: title → playing ⇄ paused → over → restart.
    ///
    /// This is the composition root, so it is the one place gameplay and UI meet. Ordering
    /// matters: the board bounds must exist before the knife (the knife needs the play area)
    /// and the ingredient spawner must exist before the knife (the knife needs the registry).
    /// </summary>
    public sealed class GameRunner : MonoBehaviour
    {
        public enum RunState { Title, Playing, Paused, Over }

        [SerializeField] private GameBootstrap bootstrap;
        [SerializeField] private BoardBounds bounds;
        [SerializeField] private IngredientSpawner ingredients;
        [SerializeField] private KnifeManager knives;
        [SerializeField] private AimInput aimInput;
        [SerializeField] private TrajectoryPreview trajectoryPreview;
        [SerializeField] private OrderSpawner orderSpawner;
        [SerializeField] private BoardFx boardFx;

        [Header("Presentation (optional — the run works without it)")]
        [SerializeField] private OrderTicketHud hud;
        [SerializeField] private RunOverlay overlay;
        [SerializeField] private GameAudio gameAudio;

        private ServiceRegistry _services;
        private SousChefCrew _sousChefs;
        private int _served;
        private int _expired;
        private int _bestCombo;

        public RunState State { get; private set; } = RunState.Title;
        public IOrderQueue Queue { get; private set; }
        public ReputationService Reputation { get; private set; }

        private void Start()
        {
            _services = bootstrap != null ? bootstrap.Services : ServiceRegistry.Current;
            if (_services == null)
            {
                Debug.LogError("GameRunner: GameBootstrap has not run. Add it to the scene.", this);
                enabled = false;
                return;
            }

            RuntimeTuning tuning = _services.Tuning;
            EventBus events = _services.Events;

            bounds.Initialize(tuning.Config);
            ingredients.Initialize(tuning, events);
            knives.Initialize(tuning, events, bounds, ingredients);

            aimInput.Initialize(tuning);
            trajectoryPreview.Initialize(aimInput, tuning, knives);
            aimInput.OnLaunch += OnLaunchGesture;

            Queue = new LocalOrderQueue(events, tuning);
            Reputation = new ReputationService(events, tuning);
            orderSpawner.Initialize(Queue, _services.Clock, tuning, new ConstantDifficultyProvider(), ingredients);
            _sousChefs = new SousChefCrew(tuning, knives, ingredients, Queue);

            // The loop: slices feed orders, orders pay or cost reputation, reputation ends the run.
            events.Subscribe<IngredientSliced>(OnSliced);
            events.Subscribe<OrderServed>(OnServed);
            events.Subscribe<OrderExpired>(OnExpired);
            events.Subscribe<GameOver>(OnGameOver);

            if (boardFx != null) boardFx.Initialize(events);
            if (gameAudio != null) gameAudio.Initialize(events);
            if (hud != null) hud.Initialize(Queue, events, _services.Wallet, Reputation.StartReputation);

            ingredients.FillImmediately();
            EnterTitle();
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            if (aimInput != null) aimInput.OnLaunch -= OnLaunchGesture;
            if (_services == null) return;

            EventBus events = _services.Events;
            events.Unsubscribe<IngredientSliced>(OnSliced);
            events.Unsubscribe<OrderServed>(OnServed);
            events.Unsubscribe<OrderExpired>(OnExpired);
            events.Unsubscribe<GameOver>(OnGameOver);
        }

        private void Update()
        {
            if (_services == null) return;

            _services.Clock.Advance();

            if (State == RunState.Playing && _sousChefs != null)
                _sousChefs.Tick(_services.Clock.DeltaTime);

            // Pause is a keyboard convenience on PC; mobile pauses when the app loses focus.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (State == RunState.Playing) Pause();
                else if (State == RunState.Paused) Resume();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && State == RunState.Playing) Pause();
        }

        private void OnLaunchGesture(Vector2 dragWorld)
        {
            if (State != RunState.Playing) return;
            knives.Launch(KnifeLauncher.ResolveVelocity(dragWorld, _services.Tuning), LaunchSource.Manual);
        }

        private void OnSliced(IngredientSliced e)
        {
            if (e.Combo > _bestCombo) _bestCombo = e.Combo;
            if (State == RunState.Playing) Queue.SubmitIngredient(e.Ingredient, e.Position);
        }

        private void OnServed(OrderServed e)
        {
            _served++;
            _services.Wallet.Add(e.Cash);
        }

        private void OnExpired(OrderExpired e)
        {
            _expired++;
            Reputation.Penalise();
        }

        private void OnGameOver(GameOver e) => EnterOver();

        private void EnterTitle()
        {
            State = RunState.Title;
            SetLive(false);
            ShowOverlay(
                "Slice & Serve",
                "Drag <b>back</b> from anywhere and release to throw the knife.\n"
              + "It bounces off the walls and slices what it touches.\n"
              + "Sliced ingredients go to the customer who has waited longest.\n"
              + "Serve before their patience runs out!",
                "Open the kitchen",
                StartRun);

            if (overlay == null) StartRun();
        }

        /// <summary>Starts the run from the title card. Public so a test or a future menu can skip the card.</summary>
        public void StartRun()
        {
            if (State != RunState.Title) return;

            if (overlay != null) overlay.Hide();
            State = RunState.Playing;
            SetLive(true);
        }

        private void Pause()
        {
            State = RunState.Paused;
            aimInput.CancelDrag();
            SetLive(false);
            Time.timeScale = 0f;
            ShowOverlay("Paused", "The customers will wait. For now.", "Resume", Resume);
        }

        private void Resume()
        {
            if (overlay != null) overlay.Hide();
            Time.timeScale = 1f;
            State = RunState.Playing;
            SetLive(true);
        }

        private void EnterOver()
        {
            State = RunState.Over;
            aimInput.CancelDrag();
            SetLive(false);
            knives.ReturnAllKnives();

            ShowOverlay(
                "Kitchen closed!",
                $"Your reputation ran out.\n\n"
              + $"Orders served: <b>{_served}</b>\n"
              + $"Cash earned: <b>${_services.Wallet.Amount}</b>\n"
              + $"Best combo: <b>x{_bestCombo}</b>",
                "Play again",
                Restart);
        }

        private void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        private void ShowOverlay(string heading, string message, string action, System.Action onConfirm)
        {
            if (overlay != null) overlay.Show(heading, message, action, onConfirm);
        }

        /// <summary>Only a live run accepts input, ages orders and spawns customers.</summary>
        private void SetLive(bool live)
        {
            aimInput.Accepting = live;
            orderSpawner.Running = live;
            _services.Clock.SetPaused(!live);
        }
    }
}

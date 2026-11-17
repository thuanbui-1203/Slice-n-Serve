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
    /// and owns the run lifecycle: main menu → playing ⇄ paused (or shopping) → over → restart.
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
        [SerializeField] private KitchenDecor kitchen;
        [Tooltip("The customers in the dining room. Tapping them opens the orders panel.")]
        [SerializeField] private CustomerLine customers;
        [Tooltip("The die beside the launcher: shows each roll, and counts the knife's bounces down.")]
        [SerializeField] private DiceView dice;

        [Header("Presentation (optional — the run works without it)")]
        [SerializeField] private OrderTicketHud hud;
        [SerializeField] private RunOverlay overlay;
        [SerializeField] private GameAudio gameAudio;
        [SerializeField] private ShopPanel shop;
        [SerializeField] private MainMenu mainMenu;
        [SerializeField] private SettingsPanel settingsPanel;
        [Tooltip("The HUD's Upgrades button: opens the shop mid-run, with the run paused behind it.")]
        [SerializeField] private UnityEngine.UI.Button hudUpgrades;
        [SerializeField] private HudBanner banner;
        [Tooltip("Slides in over the dining room: tap a ready order there to serve it.")]
        [SerializeField] private OrdersPanel ordersPanel;
        [Tooltip("HUD switch for auto-throw; shown once it is unlocked.")]
        [SerializeField] private UnityEngine.UI.Button autoToggle;
        [SerializeField] private TMPro.TMP_Text autoLabel;

        // "Play again" reloads the scene for a clean board; this carries the intent across the
        // load, so the new scene skips the menu. Consumed in Start.
        private static bool _startOnLoad;

        private ServiceRegistry _services;
        private TimeRampDifficultyProvider _difficulty;
        private int _demandLevel;
        private SousChefCrew _crew;
        private int _runCash;
        private int _served;
        private int _expired;
        private int _bestCombo;
        private int _readyHints;
        private AutoThrower _auto;
        private bool _playerKnifeWasLive;
        private float _lastRestock = -1f;

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

            GameRandom random = _services.Random ?? new GameRandom();
            bounds.Initialize(tuning.Config);
            ingredients.Initialize(tuning, events, random);
            knives.Initialize(tuning, events, bounds, ingredients, random);

            aimInput.Initialize(tuning);
            trajectoryPreview.Initialize(aimInput, tuning, knives);
            aimInput.OnLaunch += OnLaunchGesture;
            aimInput.OnTap += OnRestockTap;

            Queue = new LocalOrderQueue(events, tuning);
            Reputation = new ReputationService(events, tuning);
            _difficulty = new TimeRampDifficultyProvider(_services.Clock, tuning.Config);
            orderSpawner.Initialize(Queue, _services.Clock, tuning, _difficulty, ingredients, random);
            _crew = new SousChefCrew(tuning, knives, ingredients, Queue);
            _auto = new AutoThrower(tuning, knives, random);

            // The loop: slices feed orders, orders pay or cost reputation, reputation ends the run.
            events.Subscribe<IngredientSliced>(OnSliced);
            events.Subscribe<OrderServed>(OnServed);
            events.Subscribe<OrderExpired>(OnExpired);
            events.Subscribe<OrderReady>(OnReady);
            events.Subscribe<KnifeBounced>(OnBounced);
            events.Subscribe<GameOver>(OnGameOver);
            if (_services.Upgrades != null) _services.Upgrades.Purchased += OnPurchased;
            if (autoToggle != null) autoToggle.onClick.AddListener(ToggleAutoThrow);

            if (customers != null)
            {
                customers.Initialize(tuning, events, Queue);
                aimInput.PressFilter = OnPressCustomers;
            }
            if (boardFx != null) boardFx.Initialize(events);
            if (kitchen != null) kitchen.Initialize(tuning.Config, events);
            if (gameAudio != null)
            {
                gameAudio.Initialize(events);
                if (_services.Settings != null) gameAudio.BindSettings(_services.Settings);
            }
            if (hud != null) hud.Initialize(Queue, events, _services.Wallet, Reputation.StartReputation);
            if (shop != null)
            {
                shop.Initialize(_services.Upgrades, _services.Wallet);
                if (gameAudio != null) shop.BuyAttempted += gameAudio.PlayPurchase;
            }
            if (settingsPanel != null && _services.Settings != null) settingsPanel.Initialize(_services.Settings);
            if (hudUpgrades != null) hudUpgrades.onClick.AddListener(OpenShopMidRun);

            ingredients.FillImmediately();
            EnterTitle();

            if (_startOnLoad)
            {
                _startOnLoad = false;
                StartRun();
            }
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            if (aimInput != null)
            {
                aimInput.OnLaunch -= OnLaunchGesture;
                aimInput.OnTap -= OnRestockTap;
            }
            if (_services == null) return;
            if (_services.Upgrades != null) _services.Upgrades.Purchased -= OnPurchased;

            EventBus events = _services.Events;
            events.Unsubscribe<IngredientSliced>(OnSliced);
            events.Unsubscribe<OrderServed>(OnServed);
            events.Unsubscribe<OrderExpired>(OnExpired);
            events.Unsubscribe<OrderReady>(OnReady);
            events.Unsubscribe<KnifeBounced>(OnBounced);
            events.Unsubscribe<GameOver>(OnGameOver);
        }

        private void Update()
        {
            if (_services == null) return;

            _services.Clock.Advance();
            if (State == RunState.Playing)
            {
                float dt = _services.Clock.DeltaTime;

                // Each throw ends -> the die rolls for the next one.
                bool knifeLive = knives.PlayerKnifeLive;
                if (_playerKnifeWasLive && !knifeLive) RollDie();
                _playerKnifeWasLive = knifeLive;

                bool auto = AutoThrowActive;
                if (auto && !aimInput.TapMode) aimInput.CancelDrag();
                aimInput.TapMode = auto;
                _auto.Enabled = auto;
                _auto.Tick(dt, dice == null || !dice.IsRolling);

                _crew.Tick(dt);
                UpdateRunEvents();
            }

            // Pause is a keyboard convenience on PC; mobile pauses when the app loses focus.
            // Escape backs out of whatever panel is on top first.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (shop != null && shop.IsOpen) shop.Close();
                else if (ordersPanel != null && ordersPanel.IsOpen && State == RunState.Playing) ordersPanel.Close();
                else if (settingsPanel != null && settingsPanel.IsOpen) settingsPanel.Close();
                else if (State == RunState.Playing) Pause();
                else if (State == RunState.Paused) Resume();
            }
        }

        /// <summary>Rush hour starts and stops, and demand levels up, on the run clock.</summary>
        private void UpdateRunEvents()
        {
            var config = _services.Tuning.Config;
            RushHour rushHour = orderSpawner.Rush;

            bool rush = rushHour.IsActive;
            if (rush != Queue.FrontOnly)
            {
                Queue.FrontOnly = rush;
                _services.Events.Publish(new RushHourChanged(rush));
            }
            if (banner != null)
            {
                banner.SetRush(rush, orderSpawner.RushCustomersWaiting(), config.rushCashBonus);
                if (!rush && rushHour.UntilNext > 0f && rushHour.UntilNext <= 5f)
                    banner.Notify($"Rush hour in {Mathf.CeilToInt(rushHour.UntilNext)}...", 0.25f);
            }

            int level = _difficulty.DemandLevel;
            if (level > _demandLevel)
            {
                _demandLevel = level;
                int pay = Mathf.RoundToInt(level * config.demandRewardPerLevel * 100f);
                if (banner != null) banner.Notify($"Bigger orders!  Level {level + 1}  -  +{pay}% pay");
            }
        }

        // ------------------------------------------------------------------ dice and auto-throw

        /// <summary>Auto-throw is unlocked (Knife Speed and Split Chance maxed) and switched on.</summary>
        public bool AutoThrowActive
            => _services.Upgrades != null && _services.Upgrades.AutoThrowUnlocked
            && (_services.Settings == null || _services.Settings.AutoThrow);

        /// <summary>The die decides how many times the next knife may bounce off the border.</summary>
        private void RollDie()
        {
            int roll = _services.Random.RollDie();
            knives.BounceBudget = roll;
            if (dice != null) dice.Roll(roll);
            _services.Events.Publish(new DiceRolled(roll));
        }

        /// <summary>The die counts the player's knife's bounces down as it flies.</summary>
        private void OnBounced(KnifeBounced e)
        {
            KnifeController mine = knives.PlayerKnife;
            if (dice != null && mine != null && mine.KnifeId == e.KnifeId) dice.ShowRemaining(e.Remaining);
        }

        /// <summary>With auto-throw on, tapping the board brings out another ingredient (rate-limited).</summary>
        private void OnRestockTap(Vector2 world)
        {
            if (State != RunState.Playing) return;
            float now = _services.Clock.Elapsed;
            if (_lastRestock >= 0f && now - _lastRestock < _services.Tuning.Config.restockTapCooldown) return;

            _lastRestock = now;
            ingredients.SpawnNear(world);
        }

        public void ToggleAutoThrow()
        {
            if (_services.Settings == null) return;
            _services.Settings.AutoThrow = !_services.Settings.AutoThrow;
            _services.Settings.Save();
            RefreshAutoToggle(State == RunState.Playing);
        }

        private void RefreshAutoToggle(bool live)
        {
            if (autoToggle == null) return;
            bool unlocked = _services.Upgrades != null && _services.Upgrades.AutoThrowUnlocked;
            autoToggle.gameObject.SetActive(live && unlocked);
            if (autoLabel != null) autoLabel.SetText(AutoThrowActive ? "Auto: ON" : "Auto: OFF");
        }

        private void OnPurchased(Data.UpgradeDefinition upgrade)
        {
            bool unlocked = _services.Upgrades.AutoThrowUnlocked;
            if (unlocked && banner != null && (upgrade.Kind == Data.UpgradeKind.KnifeSpeed || upgrade.Kind == Data.UpgradeKind.SplitChance))
                banner.Notify("Auto-throw unlocked!  The knife throws itself - tap the board for ingredients", 4f);
            RefreshAutoToggle(State == RunState.Playing || State == RunState.Paused);
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
            _runCash += e.Cash;
            _services.Wallet.Add(e.Cash);
        }

        /// <summary>A press in the dining room opens (or closes) the orders panel instead of aiming.</summary>
        private bool OnPressCustomers(Vector2 world)
        {
            if (State != RunState.Playing || !customers.Area.Contains(world)) return false;
            ToggleOrders();
            return true;
        }

        /// <summary>Opens or closes the orders panel. Public for tests.</summary>
        public void ToggleOrders()
        {
            if (ordersPanel != null && State == RunState.Playing) ordersPanel.Toggle();
        }

        /// <summary>Orders are served by hand now: the first couple of times, say so.</summary>
        private void OnReady(OrderReady e)
        {
            if (banner == null || _readyHints >= 2 || (ordersPanel != null && ordersPanel.IsOpen)) return;
            _readyHints++;
            banner.Notify("Order ready!  Tap the customers to serve it", 3f);
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
            if (ordersPanel != null) ordersPanel.Close(instant: true);
            ShowMenu();

            if (mainMenu == null)
            {
                Debug.LogWarning("GameRunner: no MainMenu in the scene, so the run starts straight away. "
                               + "Run Slice & Serve → Setup → 2. Build Everything to regenerate Game.unity.", this);
                StartRun();
            }
        }

        private void ShowMenu()
        {
            if (mainMenu == null) return;

            mainMenu.Show(_services.Wallet.Amount, _services.Save.Data.bestServed, AnyUpgradeAffordable(),
                          StartRun, () => OpenShop(ShowMenu), OpenSettings, QuitGame);
        }

        /// <summary>The menu's Exit. Progress is saved first; in the Editor it stops Play mode.</summary>
        private void QuitGame()
        {
            _services.Save.Save();
            _services.Settings?.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OpenSettings()
        {
            if (settingsPanel == null) { ShowMenu(); return; }
            settingsPanel.Open(ShowMenu);
        }

        private bool AnyUpgradeAffordable()
        {
            var upgrades = _services.Upgrades;
            if (upgrades == null) return false;
            for (int i = 0; i < upgrades.Catalog.Count; i++)
                if (upgrades.CanBuy(upgrades.Catalog[i])) return true;
            return false;
        }

        /// <summary>Starts the run from the main menu. Public so a test can skip the menu.</summary>
        public void StartRun()
        {
            if (State != RunState.Title) return;

            if (mainMenu != null) mainMenu.Hide();
            if (overlay != null) overlay.Hide();
            State = RunState.Playing;
            SetLive(true);
            RollDie();   // the first knife's bounces
        }

        private void Pause()
        {
            Freeze();
            ShowPause();
        }

        private void ShowPause()
            => ShowOverlay("Paused", "The customers will wait. For now.", "Resume", Resume,
                           "Upgrades", () => OpenShop(ShowPause));

        /// <summary>
        /// The HUD button: spend the cash you are earning without leaving the run. The run is
        /// frozen while the shop is open, and a purchase applies the moment it is made — new
        /// customers, the next throw and the sous-chefs all read the upgraded tuning.
        /// </summary>
        public void OpenShopMidRun()
        {
            if (State != RunState.Playing || shop == null) return;

            Freeze();
            shop.Open(Resume);
        }

        private void Freeze()
        {
            State = RunState.Paused;
            aimInput.CancelDrag();
            SetLive(false);
            Time.timeScale = 0f;
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
            if (ordersPanel != null) ordersPanel.Close(instant: true);
            if (banner != null) banner.SetRush(false, 0, 1f);

            // End of run is the main save checkpoint: the cash is banked for the shop.
            SaveData save = _services.Save.Data;
            save.runsPlayed++;
            if (_served > save.bestServed) save.bestServed = _served;
            _services.Save.Save();

            ShowResults();
        }

        private void ShowResults()
        {
            SaveData save = _services.Save.Data;
            ShowOverlay(
                "Kitchen closed!",
                $"Your reputation ran out.\n\n"
              + $"Orders served: <b>{_served}</b>  (best {save.bestServed})\n"
              + $"Cash earned: <b>${_runCash}</b>  ·  In the till: <b>${_services.Wallet.Amount}</b>\n"
              + $"Best combo: <b>x{_bestCombo}</b>  ·  Order level reached: <b>{_demandLevel + 1}</b>",
                "Play again",
                () => Reload(startRun: true),
                "Main menu",
                () => Reload(startRun: false));
        }

        /// <summary>Opens the shop over whatever card is showing, and returns to it on Back.
        /// Patience is fixed at spawn, so a purchase never rewrites a live order.</summary>
        private void OpenShop(System.Action onClose)
        {
            if (shop == null) { onClose(); return; }
            shop.Open(onClose);
        }

        private void Reload(bool startRun)
        {
            _startOnLoad = startRun;
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        private void ShowOverlay(string heading, string message, string action, System.Action onConfirm,
                                 string secondaryAction = null, System.Action onSecondary = null)
        {
            if (overlay != null) overlay.Show(heading, message, action, onConfirm, secondaryAction, onSecondary);
        }

        /// <summary>Only a live run accepts input, ages orders and spawns customers.</summary>
        private void SetLive(bool live)
        {
            aimInput.Accepting = live;
            orderSpawner.Running = live;
            _services.Clock.SetPaused(!live);
            if (hudUpgrades != null) hudUpgrades.gameObject.SetActive(live);
            if (!live) aimInput.TapMode = false;
            RefreshAutoToggle(live);
        }
    }
}

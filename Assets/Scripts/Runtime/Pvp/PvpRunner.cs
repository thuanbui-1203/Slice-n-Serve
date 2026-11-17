using System.Collections.Generic;
using SliceNServe.Board;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.Orders;
using SliceNServe.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SliceNServe.Pvp
{
    /// <summary>
    /// The composition root for PvP (concept §5), in the same scene as solo play. When
    /// <see cref="MatchSetup"/> asks for a match, <see cref="GameRunner"/> stands aside and this
    /// builds it from the same scene objects:
    ///
    /// * <b>Play with Friends</b> — the board rig and the customer spawner are cloned for
    ///   player 2: two boards side by side on one screen, each with its own customers, ticket
    ///   strip, die and controls. A straight race: who earns more from their own customers.
    /// * <b>Enter PvP match</b> — one board per device, laid out like solo play (dining room and
    ///   orders panel), with one shared line of customers synced over the network
    ///   (<see cref="OnlineSync"/>): first to serve an order gets the cash.
    ///
    /// Either way: a fixed clock, base stats for everyone (no upgrades, no auto-throw, no
    /// sous-chefs), no rush hour, and no reputation — the higher score when time runs out wins.
    /// </summary>
    public sealed class PvpRunner : MonoBehaviour
    {
        private const float CountdownSeconds = 3f;

        [SerializeField] private GameBootstrap bootstrap;

        [Header("Board rig (player 1; cloned for player 2 on a shared screen)")]
        [SerializeField] private BoardBounds bounds;
        [SerializeField] private IngredientSpawner ingredients;
        [SerializeField] private KnifeManager knives;
        [SerializeField] private AimInput aimInput;
        [SerializeField] private TrajectoryPreview trajectoryPreview;
        [SerializeField] private DiceView dice;

        [Header("Shared")]
        [SerializeField] private OrderSpawner orderSpawner;
        [SerializeField] private BoardFx boardFx;
        [SerializeField] private KitchenDecor kitchen;
        [SerializeField] private CustomerLine customers;
        [SerializeField] private GameAudio gameAudio;

        [Header("HUD")]
        [SerializeField] private OrderTicketHud hud;
        [SerializeField] private OrdersPanel ordersPanel;
        [SerializeField] private PvpHud pvpHud;
        [SerializeField] private RunOverlay overlay;
        [Tooltip("Ticket strips for the two players sharing a screen, left then right.")]
        [SerializeField] private OrderTicketHud[] versusTickets;
        [Tooltip("Solo-only HUD hidden in PvP: cash and hearts, Upgrades, Auto-throw.")]
        [SerializeField] private GameObject[] soloOnly;

        private enum Phase { Waiting, Countdown, Playing, Over }

        private sealed class Rig
        {
            public OrderSpawner Spawner;
            public bool IsCpu;
            public int Id;
            public RuntimeTuning Tuning;
            public BoardBounds Bounds;
            public IngredientSpawner Ingredients;
            public KnifeManager Knives;
            public AimInput Aim;
            public TrajectoryPreview Preview;
            public DiceView Dice;
            public GameRandom Random;
            public EventBus OrderEvents;
            public LocalOrderQueue Queue;
            public bool WasLive;
        }

        private readonly List<Rig> _rigs = new List<Rig>(2);
        private ServiceRegistry _services;
        private PvpMatch _match;
        private OnlineSync _online;
        private CpuPlayer _cpu;
        private Phase _phase;
        private float _countdown = CountdownSeconds;
        private int _sentScore = -1;
        private bool _local;

        public PvpMatch Match => _match;
        public bool IsPlaying => _phase == Phase.Playing;
        public int PlayerCount => _rigs.Count;
        public KnifeManager KnivesOf(int player) => _rigs[player].Knives;
        public IngredientSpawner IngredientsOf(int player) => _rigs[player].Ingredients;
        public LocalOrderQueue QueueOf(int player) => _rigs[player].Queue;
        public AimInput AimOf(int player) => _rigs[player].Aim;

        /// <summary>Play with CPU: player 2 plays itself.</summary>
        public bool VsCpu => _cpu != null;

        private void Start()
        {
            if (!MatchSetup.IsPvp)
            {
                enabled = false;
                return;
            }

            _services = bootstrap != null && bootstrap.Services != null ? bootstrap.Services : ServiceRegistry.Current;
            EventBus events = _services.Events;
            BoardConfig baseConfig = _services.Tuning.Config;
            _local = MatchSetup.Mode == MatchMode.LocalVersus;
            _services.Random.Reseed(MatchSetup.Seed);

            foreach (GameObject go in soloOnly) if (go != null) go.SetActive(false);

            if (_local)
            {
                // Clone the untouched rig for player 2 before anything initialises it.
                BoardBounds bounds2 = Instantiate(bounds);
                IngredientSpawner ingredients2 = Instantiate(ingredients);
                KnifeManager knives2 = Instantiate(knives);
                AimInput aim2 = Instantiate(aimInput);
                DiceView dice2 = dice != null ? Instantiate(dice) : null;
                OrderSpawner spawner2 = Instantiate(orderSpawner);
                spawner2.name = "Orders P2";
                bounds2.name = "Board P2";
                ingredients2.name = "Ingredients P2";
                knives2.name = "Knives P2";
                aim2.name = "Aim P2";

                AddRig(0, PvpLayout.Half(baseConfig, left: true), bounds, ingredients, knives, aimInput, trajectoryPreview, dice, events);
                AddRig(1, PvpLayout.Half(baseConfig, left: false), bounds2, ingredients2, knives2, aim2,
                       aim2.GetComponent<TrajectoryPreview>(), dice2, new EventBus());
                _rigs[0].Spawner = orderSpawner;
                _rigs[1].Spawner = spawner2;
            }
            else
            {
                AddRig(0, PvpLayout.Online(baseConfig), bounds, ingredients, knives, aimInput, trajectoryPreview, dice, events);
                _rigs[0].Spawner = orderSpawner;
            }

            // Customers. On one screen, each player has their own, from their own random stream;
            // online, the host's line is everyone's.
            Rig first = _rigs[0];
            IOrderQueue spawnInto = first.Queue;
            if (!_local && !MatchSetup.IsHost) spawnInto = new RefereedOrderQueue(first.Queue);
            foreach (Rig rig in _rigs)
            {
                IOrderQueue into = rig == first ? spawnInto : rig.Queue;
                var difficulty = new TimeRampDifficultyProvider(_services.Clock, rig.Tuning.Config);
                var customersRandom = _local ? new GameRandom(MatchSetup.Seed + 101 * (rig.Id + 1)) : _services.Random;
                rig.Spawner.Initialize(into, _services.Clock, rig.Tuning, difficulty, rig.Ingredients, customersRandom);
                rig.Spawner.SpawnsOrders = _local || MatchSetup.IsHost;
            }

            if (!_local)
            {
                _online = new OnlineSync(MatchSetup.Transport, MatchSetup.IsHost, first.Queue, first.OrderEvents,
                                         orderSpawner.Dishes, orderSpawner.Customers);
                if (spawnInto is RefereedOrderQueue refereed) refereed.Sync = _online;
            }

            events.Subscribe<IngredientSliced>(OnSliced);
            events.Subscribe<KnifeBounced>(OnBounced);
            foreach (Rig rig in _rigs)
            {
                int player = rig.Id;
                rig.OrderEvents.Subscribe<OrderServed>(e => _match.AddScore(player, e.Cash));
            }

            // Presentation.
            if (boardFx != null) boardFx.Initialize(events);
            if (gameAudio != null)
            {
                gameAudio.Initialize(events);
                if (_services.Settings != null) gameAudio.BindSettings(_services.Settings);
            }
            if (kitchen != null) kitchen.Initialize(_local ? PvpLayout.Decor(baseConfig) : first.Tuning.Config, events);

            if (_local) SetUpSharedScreen(baseConfig);
            else SetUpOnline(spawnInto);

            _match = new PvpMatch(2, MatchSetup.DurationSeconds);
            _match.Ended += OnMatchEnded;
            _phase = _local ? Phase.Countdown : Phase.Waiting;
            SetLive(false);
        }

        private void AddRig(int id, BoardConfig config, BoardBounds b, IngredientSpawner spawner, KnifeManager manager,
                            AimInput aim, TrajectoryPreview preview, DiceView die, EventBus orderEvents)
        {
            EventBus events = _services.Events;
            var rig = new Rig
            {
                Id = id, Tuning = new RuntimeTuning(config), Bounds = b, Ingredients = spawner, Knives = manager,
                Aim = aim, Preview = preview, Dice = die, OrderEvents = orderEvents,
                Random = new GameRandom(MatchSetup.Seed + 7919)   // the same rolls and boards for both players
            };

            b.Initialize(config);
            spawner.PlayerId = id;
            spawner.Initialize(rig.Tuning, events, rig.Random);
            manager.PlayerId = id;
            manager.Initialize(rig.Tuning, events, b, spawner, rig.Random);
            aim.Initialize(rig.Tuning);
            if (preview != null) preview.Initialize(aim, rig.Tuning, manager);
            aim.OnLaunch += drag => Launch(rig, drag);
            if (die != null) die.transform.position = config.launcherPosition + new Vector2(1.6f, -0.1f);

            rig.Queue = new LocalOrderQueue(orderEvents, rig.Tuning);
            spawner.FillImmediately();
            _rigs.Add(rig);
        }

        private void SetUpSharedScreen(BoardConfig baseConfig)
        {
            if (customers != null) customers.gameObject.SetActive(false);
            if (ordersPanel != null) ordersPanel.gameObject.SetActive(false);

            Rect view = baseConfig.viewBounds;
            if (MatchSetup.VsCpu)
            {
                // Player 2 is the CPU: auto-throw and auto-serve, never input.
                Rig cpu = _rigs[1];
                cpu.IsCpu = true;
                cpu.Aim.Accepting = false;
                _cpu = new CpuPlayer(cpu.Tuning, cpu.Knives, cpu.Queue, new GameRandom(MatchSetup.Seed + 4441));
            }

            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig rig = _rigs[i];
                if (rig.IsCpu)
                {
                    TicketsFor(i, rig);
                    continue;
                }
                // Each player aims on their own half: mouse, their own finger, or their own keys.
                rig.Aim.Region = i == 0
                    ? Rect.MinMaxRect(view.xMin - 100f, view.yMin - 100f, 0f, view.yMax + 100f)
                    : Rect.MinMaxRect(0f, view.yMin - 100f, view.xMax + 100f, view.yMax + 100f);
                rig.Aim.MultiTouch = true;
                rig.Aim.Keys = i == 0 ? AimInput.KeyScheme.Wasd : AimInput.KeyScheme.Arrows;
                if (_cpu != null) rig.Aim.Region = null;   // alone at the controls: aim from anywhere
                TicketsFor(i, rig);
            }
            if (pvpHud != null) pvpHud.Show(_cpu != null ? "You" : "Player 1", _cpu != null ? "CPU" : "Player 2");
        }

        private void TicketsFor(int i, Rig rig)
        {
            if (versusTickets == null || i >= versusTickets.Length || versusTickets[i] == null) return;
            versusTickets[i].gameObject.SetActive(true);
            versusTickets[i].Initialize(rig.Queue, rig.OrderEvents, null, 0);
            foreach (Rig other in _rigs) other.Aim.AddBlocker(versusTickets[i].Container);
        }

        private void SetUpOnline(IOrderQueue queue)
        {
            Rig rig = _rigs[0];
            if (hud != null) hud.Initialize(queue, rig.OrderEvents, null, 0);
            if (customers != null)
            {
                customers.Initialize(rig.Tuning, rig.OrderEvents, rig.Queue);
                rig.Aim.PressFilter = world =>
                {
                    if (_phase != Phase.Playing || !customers.Area.Contains(world)) return false;
                    if (ordersPanel != null) ordersPanel.Toggle();
                    return true;
                };
            }
            if (pvpHud != null) pvpHud.Show("You", "Rival");
        }

        private void OnDestroy()
        {
            _online?.Dispose();
            if (_services == null || !MatchSetup.IsPvp) return;
            _services.Events.Unsubscribe<IngredientSliced>(OnSliced);
            _services.Events.Unsubscribe<KnifeBounced>(OnBounced);
        }

        // ------------------------------------------------------------------ the loop

        private void Update()
        {
            if (_services == null) return;
            _services.Clock.Advance();

            if (_online != null)
            {
                _online.Pump();
                if (_online.Disconnected && _phase != Phase.Over)
                {
                    if (_phase == Phase.Playing) _match.End();
                    else ShowResults("Your rival left.");
                }
            }

            switch (_phase)
            {
                case Phase.Waiting:
                    pvpHud?.SetMessage("Waiting for your rival...");
                    if (_online != null && _online.Started) _phase = Phase.Countdown;
                    break;

                case Phase.Countdown:
                    _countdown -= Time.unscaledDeltaTime;
                    pvpHud?.SetMessage(_countdown > 0f ? Mathf.CeilToInt(_countdown).ToString() : "");
                    if (_countdown <= 0f) BeginPlay();
                    break;

                case Phase.Playing:
                    _match.Tick(_services.Clock.DeltaTime);
                    if (_cpu != null)
                    {
                        Rig cpu = _rigs[1];
                        _cpu.Tick(_services.Clock.DeltaTime, cpu.Dice == null || !cpu.Dice.IsRolling);
                    }
                    foreach (Rig rig in _rigs)
                    {
                        bool live = rig.Knives.PlayerKnifeLive;
                        if (rig.WasLive && !live) RollDie(rig);
                        rig.WasLive = live;
                    }
                    if (_online != null && _match.Score(0) != _sentScore)
                    {
                        _sentScore = _match.Score(0);
                        _online.SendScore(_sentScore);
                    }
                    break;
            }

            if (_match != null && pvpHud != null) pvpHud.Set(_match.Remaining, _match.Score(0), OpponentScore);

            Keyboard k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame && _phase != Phase.Over) Leave();
        }

        /// <summary>For tests: skip the countdown.</summary>
        public void BeginPlay()
        {
            if (_phase == Phase.Playing || _phase == Phase.Over) return;
            _phase = Phase.Playing;
            pvpHud?.SetMessage("");
            SetLive(true);
            _match.Start();
            foreach (Rig rig in _rigs) RollDie(rig);
        }

        private int OpponentScore => _local ? _match.Score(1) : (_online != null ? _online.PeerScore : 0);

        private void Launch(Rig rig, Vector2 drag)
        {
            if (_phase != Phase.Playing) return;
            rig.Knives.Launch(KnifeLauncher.ResolveVelocity(drag, rig.Tuning), LaunchSource.Manual);
        }

        private void RollDie(Rig rig)
        {
            int roll = rig.Random.RollDie();
            rig.Knives.BounceBudget = roll;
            if (rig.Dice != null) rig.Dice.Roll(roll);
            _services.Events.Publish(new DiceRolled(roll));
        }

        private void OnSliced(IngredientSliced e)
        {
            if (_phase == Phase.Playing && e.PlayerId >= 0 && e.PlayerId < _rigs.Count)
                _rigs[e.PlayerId].Queue.SubmitIngredient(e.Ingredient, e.Position);
        }

        private void OnBounced(KnifeBounced e)
        {
            if (e.PlayerId < 0 || e.PlayerId >= _rigs.Count) return;
            Rig rig = _rigs[e.PlayerId];
            KnifeController mine = rig.Knives.PlayerKnife;
            if (rig.Dice != null && mine != null && mine.KnifeId == e.KnifeId) rig.Dice.ShowRemaining(e.Remaining);
        }

        private void SetLive(bool live)
        {
            foreach (Rig rig in _rigs)
            {
                rig.Aim.Accepting = live && !rig.IsCpu;
                if (!live) rig.Aim.CancelDrag();
            }
            foreach (Rig rig in _rigs) rig.Spawner.Running = live;
            _services.Clock.SetPaused(!live);
        }

        // ------------------------------------------------------------------ the end

        private void OnMatchEnded()
        {
            SetLive(false);
            foreach (Rig rig in _rigs) rig.Knives.ReturnAllKnives();
            if (ordersPanel != null) ordersPanel.Close(instant: true);
            _online?.SendEnd(_match.Score(0));
            ShowResults(_online != null && _online.Disconnected ? "Your rival left." : null);
        }

        private void ShowResults(string note)
        {
            _phase = Phase.Over;
            SetLive(false);
            pvpHud?.SetMessage("");

            int mine = _match != null ? _match.Score(0) : 0, theirs = _match != null ? OpponentScore : 0;
            bool played = _match != null && _match.Finished;
            string title;
            if (_cpu != null) title = mine == theirs ? "It's a draw!" : mine > theirs ? "You win!" : "The CPU wins";
            else if (_local) title = mine == theirs ? "It's a draw!" : mine > theirs ? "Player 1 wins!" : "Player 2 wins!";
            else if (!played) title = "Match over";
            else title = mine == theirs ? "It's a draw!" : mine > theirs ? "You win!" : "Your rival wins";

            string left = _local && _cpu == null ? "Player 1" : "You";
            string right = _cpu != null ? "CPU" : _local ? "Player 2" : "Rival";
            string body = (note != null ? note + "\n\n" : "")
                        + $"{left}: <color=#B5381F>${mine}</color>\n{right}: <color=#B5381F>${theirs}</color>";

            if (overlay == null) return;
            if (_local)
                overlay.Show(title, body, "Rematch", () => { MatchSetup.BeginLocal(System.Environment.TickCount, MatchSetup.DurationSeconds, MatchSetup.VsCpu); Reload(); },
                             "Main menu", BackToMenu);
            else
                overlay.Show(title, body, "Main menu", BackToMenu);
        }

        /// <summary>Escape: leave the match for the menu.</summary>
        private void Leave()
        {
            if (_match != null && _phase == Phase.Playing) _online?.SendEnd(_match.Score(0));
            BackToMenu();
        }

        private void BackToMenu()
        {
            MatchSetup.Clear();
            Reload();
        }

        private void Reload()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.buildIndex);
        }
    }

    /// <summary>Board layouts for PvP, as copies of the solo <see cref="BoardConfig"/>.</summary>
    public static class PvpLayout
    {
        private const float CentreGap = 0.8f;   // half the gap between the two boards
        private const float TopBand = 1.8f;     // over the boards: the two ticket strips and the clock

        /// <summary>One player's half of a shared screen.</summary>
        public static BoardConfig Half(BoardConfig solo, bool left)
        {
            BoardConfig c = Copy(solo);
            Rect view = solo.viewBounds;
            float bottom = solo.worldBounds.yMin, top = view.yMax - TopBand;
            float xMin = left ? view.xMin + 0.6f : CentreGap, xMax = left ? -CentreGap : view.xMax - 0.6f;

            c.worldBounds = Rect.MinMaxRect(xMin, bottom, xMax, top);
            c.launcherPosition = new Vector2((xMin + xMax) * 0.5f, solo.launcherPosition.y);
            c.ingredientZone = Rect.MinMaxRect(xMin + 0.5f, solo.ingredientZone.yMin, xMax - 0.5f, top - 0.7f);
            return c;
        }

        /// <summary>The decor around both halves: the cooking line runs the full width, no dining room.</summary>
        public static BoardConfig Decor(BoardConfig solo)
        {
            BoardConfig c = Copy(solo);
            Rect view = solo.viewBounds;
            c.worldBounds = Rect.MinMaxRect(view.xMin + 0.6f, solo.worldBounds.yMin, view.xMax - 0.6f, view.yMax - TopBand);
            return c;
        }

        /// <summary>Online: the solo board, with PvP's fixed rules.</summary>
        public static BoardConfig Online(BoardConfig solo) => Copy(solo);

        /// <summary>A runtime copy with PvP's fixed rules: no rush hour (the match is short and shared).</summary>
        private static BoardConfig Copy(BoardConfig solo)
        {
            BoardConfig c = Object.Instantiate(solo);
            c.name = solo.name + " (PvP)";
            c.rushFirstAt = 1e9f;
            return c;
        }
    }
}

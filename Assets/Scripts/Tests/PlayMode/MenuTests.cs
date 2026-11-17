using System.Collections;
using NUnit.Framework;
using SliceNServe.Core;
using SliceNServe.Data;
using SliceNServe.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SliceNServe.Tests
{
    /// <summary>The main menu, the settings panel, and the shop opened in the middle of a run.</summary>
    public sealed class MenuTests
    {
        private GameRunner _runner;
        private MainMenu _menu;
        private ShopPanel _shop;
        private SettingsPanel _settings;
        private ServiceRegistry _services;
        private float _music, _sfx;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            SaveService.StoreOverride = new MemorySaveStore();
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            _runner = Object.FindAnyObjectByType<GameRunner>();
            _menu = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);
            _shop = Object.FindAnyObjectByType<ShopPanel>(FindObjectsInactive.Include);
            _settings = Object.FindAnyObjectByType<SettingsPanel>(FindObjectsInactive.Include);
            _services = ServiceRegistry.Current;
            _music = _services.Settings.MusicVolume;
            _sfx = _services.Settings.SfxVolume;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_services?.Settings != null)
            {
                // Settings live in PlayerPrefs: put the player's own values back.
                _services.Settings.MusicVolume = _music;
                _services.Settings.SfxVolume = _sfx;
                _services.Settings.Save();
            }
            SaveService.StoreOverride = null;
        }

        private Button MenuButton(string name) => _menu.transform.Find($"Card/{name}").GetComponent<Button>();
        private Button HudUpgrades
            => Object.FindAnyObjectByType<OrderTicketHud>().transform.Find("SafeAreaRoot/Upgrades").GetComponent<Button>();

        [Test]
        public void Boot_ShowsTheMainMenu_WithPlayUpgradeSettings()
        {
            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Title));
            Assert.That(_menu.IsVisible, Is.True);
            foreach (string name in new[] { "Play", "Upgrade", "Settings", "Exit" })
                Assert.That(MenuButton(name).gameObject.activeInHierarchy, Is.True, name);
            Assert.That(HudUpgrades.gameObject.activeInHierarchy, Is.False, "the in-run Upgrades button is hidden on the menu");
        }

        private Button HudPause
            => Object.FindAnyObjectByType<OrderTicketHud>().transform.Find("SafeAreaRoot/Pause").GetComponent<Button>();

        [UnityTest]
        public IEnumerator PauseButton_PausesTheRun_AndShowsOnlyWhilePlaying()
        {
            Assert.That(HudPause.gameObject.activeInHierarchy, Is.False, "no pause button on the menu");
            _runner.StartRun();
            yield return null;
            Assert.That(HudPause.gameObject.activeInHierarchy, Is.True);

            HudPause.onClick.Invoke();
            yield return null;
            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Paused));
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(HudPause.gameObject.activeInHierarchy, Is.False, "the pause card's Resume takes over");
        }

        [UnityTest]
        public IEnumerator Play_StartsTheRun()
        {
            MenuButton("Play").onClick.Invoke();
            yield return null;

            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Playing));
            Assert.That(_menu.IsVisible, Is.False);
            Assert.That(HudUpgrades.gameObject.activeInHierarchy, Is.True, "the in-run Upgrades button appears with the run");
        }

        [UnityTest]
        public IEnumerator Upgrade_OpensTheShop_AndBackReturnsToTheMenu()
        {
            MenuButton("Upgrade").onClick.Invoke();
            yield return null;
            Assert.That(_shop.IsOpen, Is.True);
            Assert.That(_menu.IsVisible, Is.False);

            _shop.Close();
            yield return null;
            Assert.That(_menu.IsVisible, Is.True);
            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Title));
        }

        [UnityTest]
        public IEnumerator Settings_ChangeVolumes_AndBackReturnsToTheMenu()
        {
            MenuButton("Settings").onClick.Invoke();
            yield return null;
            Assert.That(_settings.IsOpen, Is.True);

            Slider music = _settings.transform.Find("Card/MusicRow/Slider").GetComponent<Slider>();
            music.value = 0.25f;
            Assert.That(_services.Settings.MusicVolume, Is.EqualTo(0.25f).Within(1e-4f), "a slider writes through immediately");

            _settings.Close();
            yield return null;
            Assert.That(_menu.IsVisible, Is.True);
        }

        [UnityTest]
        public IEnumerator ShopMidRun_FreezesTheRun_AndPurchasesApplyAtOnce()
        {
            _runner.StartRun();
            yield return null;
            _services.Wallet.Add(500);

            UpgradeDefinition speed = null;
            foreach (UpgradeDefinition u in _services.Upgrades.Catalog)
                if (u.Kind == UpgradeKind.KnifeSpeed) speed = u;

            HudUpgrades.onClick.Invoke();
            yield return null;

            Assert.That(_shop.IsOpen, Is.True);
            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Paused), "the run waits while you shop");
            Assert.That(_services.Clock.IsPaused, Is.True, "customers' patience does not drain behind the shop");

            float before = _services.Tuning.LaunchMaxSpeed;
            Assert.That(_services.Upgrades.TryBuy(speed), Is.True);
            Assert.That(_services.Tuning.LaunchMaxSpeed, Is.GreaterThan(before), "the next throw is already faster");

            _shop.Close();
            yield return null;
            Assert.That(_runner.State, Is.EqualTo(GameRunner.RunState.Playing));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }
    }
}

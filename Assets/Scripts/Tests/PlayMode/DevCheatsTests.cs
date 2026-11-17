using System.Collections;
using NUnit.Framework;
using SliceNServe.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SliceNServe.Tests
{
    /// <summary>
    /// The developer buttons. Found by name, never by type, so this assembly does not reference
    /// DevTools: delete that folder and these tests are skipped, not broken.
    /// </summary>
    public sealed class DevCheatsTests
    {
        private MemorySaveStore _store;

        private IEnumerator Load()
        {
            SaveService.StoreOverride = _store;
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _store = new MemorySaveStore();
            yield return Load();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            SaveService.StoreOverride = null;
        }

        private static Button Find(string name)
        {
            GameObject root = GameObject.Find("DevCheats");
            if (root == null) Assert.Ignore("DevTools is not in this build (detached).");
            Transform t = root.transform.Find($"DevCheatsCanvas/Bar/{name}");
            Assert.That(t, Is.Not.Null, name);
            return t.GetComponent<Button>();
        }

        private static IEnumerator AfterReload()
        {
            for (int i = 0; i < 3; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator AddMoney_AddsToTheSavedWallet()
        {
            int before = ServiceRegistry.Current.Wallet.Amount;
            Find("AddMoney").onClick.Invoke();
            yield return AfterReload();

            Assert.That(ServiceRegistry.Current.Wallet.Amount, Is.EqualTo(before + 1000));
            Assert.That(new SaveService(_store).Data.cash, Is.EqualTo(before + 1000), "saved, so it survives a restart");
        }

        [UnityTest]
        public IEnumerator ResetProgress_NeedsTwoTaps_ThenWipesTheSave()
        {
            ServiceRegistry services = ServiceRegistry.Current;
            services.Wallet.Add(5000);
            foreach (var u in services.Upgrades.Catalog) services.Upgrades.TryBuy(u);
            services.Save.Save();

            Button reset = Find("ResetProgress");
            reset.onClick.Invoke();
            yield return null;
            Assert.That(ServiceRegistry.Current.Wallet.Amount, Is.GreaterThan(0), "one tap only asks");

            reset.onClick.Invoke();
            yield return AfterReload();

            services = ServiceRegistry.Current;
            Assert.That(services.Wallet.Amount, Is.EqualTo(0));
            foreach (var u in services.Upgrades.Catalog) Assert.That(services.Upgrades.LevelOf(u), Is.EqualTo(0), u.Id);
        }
    }
}

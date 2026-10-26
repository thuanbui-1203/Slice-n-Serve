using SliceNServe.Data;
using SliceNServe.Economy;
using UnityEngine;

namespace SliceNServe.Core
{
    /// <summary>
    /// Single entry point for the scene (plan §4.4): builds the services before anything that
    /// consumes the registry. <see cref="GameRunner"/> wires them and owns the run lifecycle.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private BoardConfig boardConfig;
        [SerializeField] private UpgradeDefinition[] upgrades;

        public ServiceRegistry Services { get; private set; }

        private void Awake()
        {
            if (boardConfig == null)
            {
                Debug.LogError("GameBootstrap: no BoardConfig assigned.", this);
                enabled = false;
                return;
            }

            var save = new SaveService(SaveService.StoreOverride ?? FileSaveStore.Default());
            var tuning = new RuntimeTuning(boardConfig);
            var wallet = new Wallet(save.Data.cash);
            wallet.Changed += amount => save.Data.cash = amount;   // written to disk at checkpoints

            Services = ServiceRegistry.Install(new ServiceRegistry(new EventBus(), new RunClock(), tuning, wallet));
            Services.Save = save;
            Services.Upgrades = new UpgradeService(upgrades ?? new UpgradeDefinition[0], save, wallet, tuning);

            // O3 is still open; this is the 60 FPS floor, which mobile does not default to.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
        }

        // Checkpoints: losing focus is the last reliable moment on mobile; quitting on PC.
        private void OnApplicationPause(bool paused) { if (paused) Services?.Save.Save(); }
        private void OnApplicationQuit() => Services?.Save.Save();

        private void OnDestroy()
        {
            if (ServiceRegistry.Current == Services) ServiceRegistry.Clear();
        }
    }
}

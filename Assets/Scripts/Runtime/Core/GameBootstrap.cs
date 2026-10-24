using SliceNServe.Data;
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

        public ServiceRegistry Services { get; private set; }

        private void Awake()
        {
            if (boardConfig == null)
            {
                Debug.LogError("GameBootstrap: no BoardConfig assigned.", this);
                enabled = false;
                return;
            }

            var tuning = new RuntimeTuning(boardConfig);
            Services = ServiceRegistry.Install(new ServiceRegistry(
                new EventBus(), new RunClock(), tuning, new Wallet()));

            // O3 is still open; this is the 60 FPS floor, which mobile does not default to.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
        }

        private void OnDestroy()
        {
            if (ServiceRegistry.Current == Services) ServiceRegistry.Clear();
        }
    }
}

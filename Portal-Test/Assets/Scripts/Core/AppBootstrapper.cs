using FacilityViewer.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace FacilityViewer.Core
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AppState))]
    [RequireComponent(typeof(LevelTeleportService))]
    public sealed class AppBootstrapper : MonoBehaviour
    {
        [SerializeField] private AppState appState;
        [SerializeField] private LevelTeleportService levelTeleportService;
        [SerializeField] private UIDocument applicationUi;

        public bool IsReady { get; private set; }
        public AppState State => appState;
        public LevelTeleportService TeleportService => levelTeleportService;
        public UIDocument ApplicationUi => applicationUi;

        private void Reset()
        {
            appState = GetComponent<AppState>();
            levelTeleportService = GetComponent<LevelTeleportService>();
            applicationUi = GetComponentInChildren<UIDocument>(true);
        }

        private void Awake()
        {
            IsReady = false;
            appState ??= GetComponent<AppState>();
            levelTeleportService ??= GetComponent<LevelTeleportService>();
            applicationUi ??= GetComponentInChildren<UIDocument>(true);

            if (appState == null || levelTeleportService == null || applicationUi == null)
            {
                Debug.LogError(
                    "AppBootstrapper is missing AppState, LevelTeleportService, or Application UI.",
                    this);
                enabled = false;
                return;
            }

            appState.Initialize();

            if (!levelTeleportService.Initialize())
            {
                appState.SetFailure("Application service configuration failed");
                enabled = false;
                return;
            }

            IsReady = true;
            Debug.Log("[Bootstrap] Persistent application services initialized.", this);
        }

        private void Start()
        {
            if (IsReady && !levelTeleportService.RequestInitialLevel() && !appState.HasError)
            {
                appState.SetFailure("Initial facility request was rejected");
            }
        }
    }
}

using FacilityViewer.Core;
using FacilityViewer.Player;
using FacilityViewer.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace FacilityViewer.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class FacilityTransitionPresenter : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private AppState appState;
        [SerializeField] private LevelTeleportService levelTeleportService;
        [SerializeField] private PlayerInputCoordinator inputCoordinator;

        private VisualElement facilityPanel;
        private VisualElement loadingOverlay;
        private Label currentLevelLabel;
        private Label transitionStatusLabel;
        private Label loadingStatusLabel;
        private Button lobbyButton;
        private Button operationsButton;
        private Button plantButton;
        private PlayerInputRouter inputRouter;
        private bool isSubscribed;

        private void Reset()
        {
            uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            uiDocument ??= GetComponent<UIDocument>();

            if (uiDocument == null || appState == null || levelTeleportService == null || inputCoordinator == null)
            {
                Debug.LogError("[Facility UI] Presenter references are incomplete.", this);
                enabled = false;
                return;
            }

            VisualElement root = uiDocument.rootVisualElement;
            facilityPanel = root.Q<VisualElement>("facility-panel");
            loadingOverlay = root.Q<VisualElement>("loading-overlay");
            currentLevelLabel = root.Q<Label>("current-level-label");
            transitionStatusLabel = root.Q<Label>("transition-status-label");
            loadingStatusLabel = root.Q<Label>("loading-status-label");
            lobbyButton = root.Q<Button>("level-lobby-button");
            operationsButton = root.Q<Button>("level-operations-button");
            plantButton = root.Q<Button>("level-plant-button");

            if (facilityPanel == null
                || loadingOverlay == null
                || currentLevelLabel == null
                || transitionStatusLabel == null
                || loadingStatusLabel == null
                || lobbyButton == null
                || operationsButton == null
                || plantButton == null)
            {
                Debug.LogError("[Facility UI] Required BootstrapShell elements are missing.", this);
                enabled = false;
                return;
            }

            inputRouter = inputCoordinator.InputRouter;

            if (inputRouter == null)
            {
                Debug.LogError("[Facility UI] Player input router is missing.", this);
                enabled = false;
                return;
            }

            Subscribe();
            RefreshState(appState);
            ApplyInputOwnership(inputRouter.InputOwner);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            lobbyButton.clicked += RequestLobby;
            operationsButton.clicked += RequestOperationsFloor;
            plantButton.clicked += RequestPlantRoom;
            appState.Changed += RefreshState;
            inputRouter.InputOwnerChanged += ApplyInputOwnership;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            lobbyButton.clicked -= RequestLobby;
            operationsButton.clicked -= RequestOperationsFloor;
            plantButton.clicked -= RequestPlantRoom;
            appState.Changed -= RefreshState;
            inputRouter.InputOwnerChanged -= ApplyInputOwnership;
            isSubscribed = false;
        }

        private void RequestLobby()
        {
            levelTeleportService.RequestTransition("lobby");
        }

        private void RequestOperationsFloor()
        {
            levelTeleportService.RequestTransition("operations-floor");
        }

        private void RequestPlantRoom()
        {
            levelTeleportService.RequestTransition("plant-room");
        }

        private void RefreshState(AppState state)
        {
            bool isBusy = state.IsTransitioning;
            string currentName = string.IsNullOrWhiteSpace(state.CurrentLevelName)
                ? "Starting..."
                : state.CurrentLevelName;

            currentLevelLabel.text = $"CURRENT AREA  /  {currentName}";
            transitionStatusLabel.text = state.StatusMessage;
            loadingStatusLabel.text = state.StatusMessage;
            loadingOverlay.EnableInClassList("is-visible", isBusy);

            lobbyButton.SetEnabled(!isBusy && state.CurrentLevelId != "lobby");
            operationsButton.SetEnabled(!isBusy && state.CurrentLevelId != "operations-floor");
            plantButton.SetEnabled(!isBusy && state.CurrentLevelId != "plant-room");
        }

        private void ApplyInputOwnership(PlayerInputOwner owner)
        {
            facilityPanel.EnableInClassList(
                "facility-panel--open",
                owner == PlayerInputOwner.UserInterface);
        }
    }
}

using FacilityViewer.Core;
using FacilityViewer.Player;
using FacilityViewer.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FacilityViewer.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class TeleportPadPromptPresenter : MonoBehaviour
    {
        private const string PromptName = "teleport-prompt";
        private const string ActionLabelName = "teleport-prompt-action-label";
        private const string DestinationLabelName = "teleport-prompt-destination-label";
        private const string VisibleClassName = "teleport-prompt--visible";

        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private AppState appState;
        [SerializeField] private PlayerTeleportPadInteractor playerInteractor;
        [SerializeField] private PlayerInputRouter inputRouter;

        private VisualElement prompt;
        private Label actionLabel;
        private Label destinationLabel;

        public bool IsPromptVisible => prompt?.ClassListContains(VisibleClassName) == true;
        public string PresentedDestination => destinationLabel?.text ?? string.Empty;

        private void Reset()
        {
            uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            uiDocument ??= GetComponent<UIDocument>();

            if (uiDocument == null || appState == null || playerInteractor == null || inputRouter == null)
            {
                Debug.LogError("[Teleport Pad UI] Presenter references are incomplete.", this);
                enabled = false;
                return;
            }

            VisualElement root = uiDocument.rootVisualElement;
            prompt = root.Q<VisualElement>(PromptName);
            actionLabel = root.Q<Label>(ActionLabelName);
            destinationLabel = root.Q<Label>(DestinationLabelName);

            if (prompt == null || actionLabel == null || destinationLabel == null)
            {
                Debug.LogError("[Teleport Pad UI] Required BootstrapShell elements are missing.", this);
                enabled = false;
                return;
            }

            playerInteractor.ActivePadChanged += OnActivePadChanged;
            appState.Changed += OnAppStateChanged;
            inputRouter.InputOwnerChanged += OnInputOwnerChanged;
            inputRouter.InputModeChanged += OnInputModeChanged;
            Present();
        }

        private void OnDisable()
        {
            if (playerInteractor != null)
            {
                playerInteractor.ActivePadChanged -= OnActivePadChanged;
            }

            if (appState != null)
            {
                appState.Changed -= OnAppStateChanged;
            }

            if (inputRouter != null)
            {
                inputRouter.InputOwnerChanged -= OnInputOwnerChanged;
                inputRouter.InputModeChanged -= OnInputModeChanged;
            }

            prompt?.EnableInClassList(VisibleClassName, false);
            prompt = null;
            actionLabel = null;
            destinationLabel = null;
        }

        private void OnActivePadChanged(TeleportPad _)
        {
            Present();
        }

        private void OnAppStateChanged(AppState _)
        {
            Present();
        }

        private void OnInputOwnerChanged(PlayerInputOwner _)
        {
            Present();
        }

        private void OnInputModeChanged(PlayerInputMode _)
        {
            Present();
        }

        private void Present()
        {
            if (prompt == null)
            {
                return;
            }

            TeleportPad activePad = playerInteractor.ActivePad;
            bool shouldShow = activePad != null
                && activePad.CanInteract
                && !appState.IsTransitioning
                && inputRouter.InputOwner == PlayerInputOwner.Gameplay;

            if (shouldShow)
            {
                actionLabel.text = inputRouter.InputMode == PlayerInputMode.Mobile
                    ? "TAP USE"
                    : "PRESS E";
                destinationLabel.text = activePad.InteractionPrompt;
            }
            else
            {
                actionLabel.text = string.Empty;
                destinationLabel.text = string.Empty;
            }

            prompt.EnableInClassList(VisibleClassName, shouldShow);
        }
    }
}

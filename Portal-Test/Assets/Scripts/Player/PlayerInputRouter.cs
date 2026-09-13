using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FacilityViewer.Player
{
    public enum PlayerInputMode
    {
        Desktop,
        Mobile
    }

    public enum PlayerInputOwner
    {
        Gameplay,
        UserInterface
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        private const string GameplayMapName = "Gameplay";
        private const string MoveActionName = "Move";
        private const string LookActionName = "Look";
        private const string SprintActionName = "Sprint";
        private const string InteractActionName = "Interact";
        private const string TogglePanelActionName = "TogglePanel";
        private const string KeyboardAndMouseSchemeName = "Keyboard&Mouse";

        [SerializeField] private PlayerInput playerInput;

        private bool isSubscribed;
        private Vector2 desktopMoveInput;
        private Vector2 desktopLookInput;
        private bool desktopSprintInput;
        private Vector2 mobileMoveInput;
        private Vector2 mobileLookDelta;

        public PlayerInputMode InputMode { get; private set; } = PlayerInputMode.Desktop;
        public PlayerInputOwner InputOwner { get; private set; } = PlayerInputOwner.Gameplay;
        public bool IsGameplayInputActive => InputOwner == PlayerInputOwner.Gameplay;
        public Vector2 MoveInput => GetMoveInput();
        public Vector2 LookInput => GetLookInput();
        public bool IsSprinting => IsDesktopGameplayActive && desktopSprintInput;

        public event Action InteractRequested;
        public event Action TogglePanelRequested;
        public event Action<PlayerInputMode> InputModeChanged;
        public event Action<PlayerInputOwner> InputOwnerChanged;

        private bool IsDesktopGameplayActive =>
            InputMode == PlayerInputMode.Desktop && InputOwner == PlayerInputOwner.Gameplay;

        private bool IsMobileGameplayActive =>
            InputMode == PlayerInputMode.Mobile && InputOwner == PlayerInputOwner.Gameplay;

        private void Reset()
        {
            playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            playerInput ??= GetComponent<PlayerInput>();

            if (!TryResolveActions())
            {
                enabled = false;
                return;
            }

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearContinuousInput();
        }

        private void Start()
        {
            EnsureDesktopInputIsReady();
        }

        private bool TryResolveActions()
        {
            if (playerInput == null || playerInput.actions == null)
            {
                Debug.LogError("PlayerInputRouter requires a PlayerInput with an Input Actions asset.", this);
                return false;
            }

            InputActionMap gameplayMap = playerInput.actions.FindActionMap(GameplayMapName, false);

            if (gameplayMap == null)
            {
                Debug.LogError($"PlayerInputRouter could not find the '{GameplayMapName}' action map.", this);
                return false;
            }

            if (gameplayMap.FindAction(MoveActionName, false) != null
                && gameplayMap.FindAction(LookActionName, false) != null
                && gameplayMap.FindAction(SprintActionName, false) != null
                && gameplayMap.FindAction(InteractActionName, false) != null
                && gameplayMap.FindAction(TogglePanelActionName, false) != null)
            {
                return true;
            }

            Debug.LogError("PlayerInputRouter is missing one or more required Gameplay actions.", this);
            return false;
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            playerInput.onActionTriggered += OnActionTriggered;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            playerInput.onActionTriggered -= OnActionTriggered;
            isSubscribed = false;
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            if (context.action.actionMap.name != GameplayMapName)
            {
                return;
            }

            switch (context.action.name)
            {
                case MoveActionName when context.performed || context.canceled:
                    OnMoveChanged(context);
                    break;
                case LookActionName when context.performed || context.canceled:
                    OnLookChanged(context);
                    break;
                case SprintActionName when context.performed || context.canceled:
                    OnSprintChanged(context);
                    break;
                case InteractActionName when context.performed:
                    OnInteractPerformed(context);
                    break;
                case TogglePanelActionName when context.performed:
                    OnTogglePanelPerformed(context);
                    break;
            }
        }

        private void OnMoveChanged(InputAction.CallbackContext context)
        {
            desktopMoveInput = context.canceled || !IsDesktopGameplayActive
                ? Vector2.zero
                : context.ReadValue<Vector2>();
        }

        private void OnLookChanged(InputAction.CallbackContext context)
        {
            desktopLookInput = context.canceled || !IsDesktopGameplayActive
                ? Vector2.zero
                : context.ReadValue<Vector2>();
        }

        private void OnSprintChanged(InputAction.CallbackContext context)
        {
            desktopSprintInput = !context.canceled && IsDesktopGameplayActive;
        }

        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            if (IsDesktopGameplayActive)
            {
                InteractRequested?.Invoke();
            }
        }

        private void OnTogglePanelPerformed(InputAction.CallbackContext context)
        {
            if (InputMode == PlayerInputMode.Desktop)
            {
                TogglePanelRequested?.Invoke();
            }
        }

        public void SetInputMode(PlayerInputMode inputMode)
        {
            if (InputMode == inputMode)
            {
                EnsureDesktopInputIsReady();
                return;
            }

            ClearContinuousInput();
            InputMode = inputMode;
            InputModeChanged?.Invoke(InputMode);
            EnsureDesktopInputIsReady();
        }

        private void EnsureDesktopInputIsReady()
        {
            if (!isActiveAndEnabled || InputMode != PlayerInputMode.Desktop || playerInput == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard == null || mouse == null)
            {
                Debug.LogWarning(
                    "Desktop input is waiting for both a keyboard and mouse to become available.",
                    this);
                return;
            }

            bool hasKeyboard = false;
            bool hasMouse = false;

            foreach (InputDevice device in playerInput.devices)
            {
                hasKeyboard |= device == keyboard;
                hasMouse |= device == mouse;
            }

            if (playerInput.currentControlScheme != KeyboardAndMouseSchemeName
                || !hasKeyboard
                || !hasMouse)
            {
                playerInput.SwitchCurrentControlScheme(KeyboardAndMouseSchemeName, keyboard, mouse);
            }

            if (playerInput.currentActionMap == null ||
                playerInput.currentActionMap.name != GameplayMapName)
            {
                playerInput.SwitchCurrentActionMap(GameplayMapName);
            }

            playerInput.ActivateInput();
        }

        public void SetInputOwner(PlayerInputOwner inputOwner)
        {
            if (InputOwner == inputOwner)
            {
                return;
            }

            ClearContinuousInput();
            InputOwner = inputOwner;
            InputOwnerChanged?.Invoke(InputOwner);
        }

        public void SetMobileMoveInput(Vector2 value)
        {
            mobileMoveInput = IsMobileGameplayActive
                ? Vector2.ClampMagnitude(value, 1f)
                : Vector2.zero;
        }

        public void AddMobileLookDelta(Vector2 delta)
        {
            if (IsMobileGameplayActive)
            {
                mobileLookDelta += delta;
            }
        }

        public Vector2 ConsumeLookInput()
        {
            Vector2 value = LookInput;
            mobileLookDelta = Vector2.zero;
            return value;
        }

        public void RequestMobileInteract()
        {
            if (IsMobileGameplayActive)
            {
                InteractRequested?.Invoke();
            }
        }

        public void RequestMobileTogglePanel()
        {
            if (InputMode == PlayerInputMode.Mobile)
            {
                TogglePanelRequested?.Invoke();
            }
        }

        public void ClearMobileInput()
        {
            mobileMoveInput = Vector2.zero;
            mobileLookDelta = Vector2.zero;
        }

        public void ClearContinuousInput()
        {
            desktopMoveInput = Vector2.zero;
            desktopLookInput = Vector2.zero;
            desktopSprintInput = false;
            ClearMobileInput();
        }

        private Vector2 GetMoveInput()
        {
            if (!IsGameplayInputActive)
            {
                return Vector2.zero;
            }

            Vector2 value = InputMode == PlayerInputMode.Desktop
                ? desktopMoveInput
                : mobileMoveInput;
            return Vector2.ClampMagnitude(value, 1f);
        }

        private Vector2 GetLookInput()
        {
            if (!IsGameplayInputActive)
            {
                return Vector2.zero;
            }

            return InputMode == PlayerInputMode.Desktop
                ? desktopLookInput
                : mobileLookDelta;
        }
    }
}

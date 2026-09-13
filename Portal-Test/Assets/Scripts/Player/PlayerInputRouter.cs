using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FacilityViewer.Player
{
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

        [SerializeField] private PlayerInput playerInput;

        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction sprintAction;
        private InputAction interactAction;
        private InputAction togglePanelAction;
        private bool isSubscribed;

        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool IsSprinting { get; private set; }

        public event Action InteractRequested;
        public event Action TogglePanelRequested;

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
            ClearState();
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

            moveAction = gameplayMap.FindAction(MoveActionName, false);
            lookAction = gameplayMap.FindAction(LookActionName, false);
            sprintAction = gameplayMap.FindAction(SprintActionName, false);
            interactAction = gameplayMap.FindAction(InteractActionName, false);
            togglePanelAction = gameplayMap.FindAction(TogglePanelActionName, false);

            if (moveAction != null
                && lookAction != null
                && sprintAction != null
                && interactAction != null
                && togglePanelAction != null)
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

            moveAction.performed += OnMoveChanged;
            moveAction.canceled += OnMoveChanged;
            lookAction.performed += OnLookChanged;
            lookAction.canceled += OnLookChanged;
            sprintAction.performed += OnSprintChanged;
            sprintAction.canceled += OnSprintChanged;
            interactAction.performed += OnInteractPerformed;
            togglePanelAction.performed += OnTogglePanelPerformed;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            moveAction.performed -= OnMoveChanged;
            moveAction.canceled -= OnMoveChanged;
            lookAction.performed -= OnLookChanged;
            lookAction.canceled -= OnLookChanged;
            sprintAction.performed -= OnSprintChanged;
            sprintAction.canceled -= OnSprintChanged;
            interactAction.performed -= OnInteractPerformed;
            togglePanelAction.performed -= OnTogglePanelPerformed;
            isSubscribed = false;
        }

        private void OnMoveChanged(InputAction.CallbackContext context)
        {
            MoveInput = context.canceled ? Vector2.zero : context.ReadValue<Vector2>();
        }

        private void OnLookChanged(InputAction.CallbackContext context)
        {
            LookInput = context.canceled ? Vector2.zero : context.ReadValue<Vector2>();
        }

        private void OnSprintChanged(InputAction.CallbackContext context)
        {
            IsSprinting = !context.canceled;
        }

        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            InteractRequested?.Invoke();
        }

        private void OnTogglePanelPerformed(InputAction.CallbackContext context)
        {
            TogglePanelRequested?.Invoke();
        }

        private void ClearState()
        {
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            IsSprinting = false;
        }
    }
}

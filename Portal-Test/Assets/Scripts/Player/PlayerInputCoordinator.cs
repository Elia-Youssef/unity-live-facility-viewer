using UnityEngine;

namespace FacilityViewer.Player
{
    public enum PlayerInputModeOverride
    {
        Automatic,
        Desktop,
        Mobile
    }

    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputRouter))]
    public sealed class PlayerInputCoordinator : MonoBehaviour
    {
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private PlayerInputModeOverride inputModeOverride = PlayerInputModeOverride.Automatic;
        [SerializeField] private bool lockCursorInDesktopGameplay = true;

        private bool hasFocus;

        public bool IsReady { get; private set; }
        public PlayerInputMode ActiveInputMode => inputRouter.InputMode;
        public bool IsUiInputActive => inputRouter.InputOwner == PlayerInputOwner.UserInterface;
        public bool WantsLockedCursor =>
            lockCursorInDesktopGameplay
            && inputRouter.InputMode == PlayerInputMode.Desktop
            && inputRouter.InputOwner == PlayerInputOwner.Gameplay;

        private void Reset()
        {
            inputRouter = GetComponent<PlayerInputRouter>();
        }

        private void OnEnable()
        {
            inputRouter ??= GetComponent<PlayerInputRouter>();

            if (inputRouter == null)
            {
                Debug.LogError("PlayerInputCoordinator requires a PlayerInputRouter.", this);
                enabled = false;
                return;
            }

            inputRouter.TogglePanelRequested += ToggleUiInput;
            inputRouter.InputModeChanged += OnInputModeChanged;
            inputRouter.InputOwnerChanged += OnInputOwnerChanged;
            Application.focusChanged += OnApplicationFocusChanged;

            hasFocus = Application.isFocused;
            inputRouter.SetInputMode(ResolveInputMode(inputModeOverride, Application.isMobilePlatform));
            inputRouter.SetInputOwner(PlayerInputOwner.Gameplay);
            ApplyCursorState();
            IsReady = true;
        }

        private void OnDisable()
        {
            Application.focusChanged -= OnApplicationFocusChanged;

            if (inputRouter != null)
            {
                inputRouter.TogglePanelRequested -= ToggleUiInput;
                inputRouter.InputModeChanged -= OnInputModeChanged;
                inputRouter.InputOwnerChanged -= OnInputOwnerChanged;
                inputRouter.ClearContinuousInput();
            }

            ReleaseCursor();
            IsReady = false;
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                inputRouter?.ClearContinuousInput();
                ReleaseCursor();
                return;
            }

            ApplyCursorState();
        }

        public void SetInputModeOverride(PlayerInputModeOverride modeOverride)
        {
            inputModeOverride = modeOverride;
            inputRouter.SetInputMode(ResolveInputMode(inputModeOverride, Application.isMobilePlatform));
            ApplyCursorState();
        }

        public void SetUiInputActive(bool isActive)
        {
            inputRouter.SetInputOwner(
                isActive ? PlayerInputOwner.UserInterface : PlayerInputOwner.Gameplay);
        }

        internal static PlayerInputMode ResolveInputMode(
            PlayerInputModeOverride modeOverride,
            bool isMobilePlatform)
        {
            return modeOverride switch
            {
                PlayerInputModeOverride.Desktop => PlayerInputMode.Desktop,
                PlayerInputModeOverride.Mobile => PlayerInputMode.Mobile,
                _ => isMobilePlatform ? PlayerInputMode.Mobile : PlayerInputMode.Desktop
            };
        }

        private void ToggleUiInput()
        {
            SetUiInputActive(!IsUiInputActive);
        }

        private void OnInputModeChanged(PlayerInputMode inputMode)
        {
            ApplyCursorState();
        }

        private void OnInputOwnerChanged(PlayerInputOwner inputOwner)
        {
            ApplyCursorState();
        }

        private void OnApplicationFocusChanged(bool isFocused)
        {
            hasFocus = isFocused;

            if (!hasFocus)
            {
                inputRouter.ClearContinuousInput();
            }

            ApplyCursorState();
        }

        private void ApplyCursorState()
        {
            bool shouldLock = hasFocus && WantsLockedCursor;
            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

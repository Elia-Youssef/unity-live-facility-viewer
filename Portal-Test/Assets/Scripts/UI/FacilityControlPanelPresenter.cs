using System;
using System.Collections.Generic;
using FacilityViewer.Core;
using FacilityViewer.Player;
using FacilityViewer.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace FacilityViewer.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class FacilityControlPanelPresenter : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private AppState appState;
        [SerializeField] private LevelTeleportService levelTeleportService;
        [SerializeField] private PlayerInputCoordinator inputCoordinator;

        private FacilityControlPanelView panelView;
        private PlayerInputRouter inputRouter;
        private VisualElement rootVisualElement;
        private bool isSubscribed;
        private bool themeControlsAvailable;
        private bool lightingControlsAvailable;
        private FacilityThemeId? selectedTheme;
        private readonly Dictionary<FacilityLightGroupId, bool> lightGroupStates = new();

        public event Action<FacilityThemeId> ThemeRequested;
        public event Action<FacilityLightGroupId> LightGroupRequested;

        public string FocusedControlName => panelView?.FocusedControlName ?? string.Empty;
        public bool HasThemeControlContract => isActiveAndEnabled && panelView != null;

        public void SetThemeControlsAvailable(bool isAvailable)
        {
            themeControlsAvailable = isAvailable;
            panelView?.PresentThemeControls(themeControlsAvailable, selectedTheme);
            PresentControlAvailability();
        }

        public void SetLightingControlsAvailable(bool isAvailable)
        {
            lightingControlsAvailable = isAvailable;
            panelView?.PresentLightControls(lightingControlsAvailable, lightGroupStates);
            PresentControlAvailability();
        }

        public void PresentSelectedTheme(FacilityThemeId? theme)
        {
            selectedTheme = theme;
            panelView?.PresentThemeControls(themeControlsAvailable, selectedTheme);
        }

        public void PresentLightGroupState(FacilityLightGroupId group, bool isActive)
        {
            lightGroupStates[group] = isActive;
            panelView?.PresentLightControls(lightingControlsAvailable, lightGroupStates);
        }

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

            rootVisualElement = uiDocument.rootVisualElement;

            if (!FacilityControlPanelView.TryCreate(rootVisualElement, out panelView))
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

            panelView.BuildNavigation(levelTeleportService.OrderedLevelCatalog, RequestLevel);
            panelView.SubscribeControlRequests(RequestTheme, RequestLightGroup);
            panelView.PresentThemeControls(themeControlsAvailable, selectedTheme);
            panelView.PresentLightControls(lightingControlsAvailable, lightGroupStates);
            PresentControlAvailability();
            Subscribe();
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnRootKeyDown);
            Application.focusChanged += OnApplicationFocusChanged;
            panelView.Present(appState);
            panelView.PresentInputOwnership(inputRouter.InputOwner);
            RefreshResponsiveLayout();
        }

        private void OnDisable()
        {
            if (inputCoordinator != null && inputCoordinator.IsUiInputActive)
            {
                inputCoordinator.SetUiInputActive(false);
            }

            Unsubscribe();
            Application.focusChanged -= OnApplicationFocusChanged;

            if (rootVisualElement != null)
            {
                rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
                rootVisualElement.UnregisterCallback<KeyDownEvent>(OnRootKeyDown);
            }

            panelView?.ClearFocus();
            rootVisualElement = null;
            panelView = null;
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            appState.Changed += RefreshState;
            inputRouter.InputOwnerChanged += ApplyInputOwnership;
            panelView.CloseRequested += ClosePanel;
            panelView.SubscribePanelCommands();
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            panelView.ClearNavigation();
            panelView.UnsubscribeControlRequests();
            panelView.UnsubscribePanelCommands();
            panelView.CloseRequested -= ClosePanel;
            appState.Changed -= RefreshState;
            inputRouter.InputOwnerChanged -= ApplyInputOwnership;
            isSubscribed = false;
        }

        private void RequestLevel(LevelDefinition destination)
        {
            levelTeleportService.RequestTransition(destination);
        }

        private void RequestTheme(FacilityThemeId theme)
        {
            if (themeControlsAvailable)
            {
                ThemeRequested?.Invoke(theme);
            }
        }

        private void RequestLightGroup(FacilityLightGroupId group)
        {
            if (lightingControlsAvailable)
            {
                LightGroupRequested?.Invoke(group);
            }
        }

        private void ClosePanel()
        {
            inputCoordinator.SetUiInputActive(false);
        }

        private void PresentControlAvailability()
        {
            panelView?.PresentControlAvailability(themeControlsAvailable, lightingControlsAvailable);
        }

        private void RefreshState(AppState state)
        {
            panelView.Present(state);
        }

        private void ApplyInputOwnership(PlayerInputOwner owner)
        {
            panelView.PresentInputOwnership(owner);
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            panelView.TryCloseFromKeyboard(evt);
        }

        private void OnApplicationFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                panelView?.ClearFocus();
            }
        }

        private void OnRootGeometryChanged(GeometryChangedEvent _)
        {
            RefreshResponsiveLayout();
        }

        private void RefreshResponsiveLayout()
        {
            if (rootVisualElement == null || panelView == null)
            {
                return;
            }

            Vector2 panelSize = rootVisualElement.contentRect.size;
            panelView.PresentLayoutMode(FacilityControlPanelLayout.IsCompactLandscape(panelSize));
            panelView.ApplySafeArea(FacilityControlPanelLayout.CalculateSafeArea(
                Screen.safeArea,
                new Vector2(Screen.width, Screen.height),
                panelSize));
        }
    }
}

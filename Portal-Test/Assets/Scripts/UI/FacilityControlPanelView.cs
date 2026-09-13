using System;
using System.Collections.Generic;
using FacilityViewer.Core;
using FacilityViewer.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace FacilityViewer.UI
{
    internal sealed class FacilityControlPanelView
    {
        private readonly VisualElement appShell;
        private readonly VisualElement appSafeArea;
        private readonly VisualElement facilityPanel;
        private readonly VisualElement statusCard;
        private readonly VisualElement loadingOverlay;
        private readonly Button closeButton;
        private readonly Label currentLevelLabel;
        private readonly Label controlAvailabilityLabel;
        private readonly Label transitionPhaseLabel;
        private readonly Label transitionStatusLabel;
        private readonly Label loadingStatusLabel;
        private readonly VisualElement levelButtons;
        private readonly List<LevelButtonBinding> levelButtonBindings = new();
        private readonly Dictionary<FacilityThemeId, Button> themeButtons = new();
        private readonly Dictionary<FacilityLightGroupId, Button> lightButtons = new();
        private readonly Dictionary<FacilityThemeId, Action> themeClickHandlers = new();
        private readonly Dictionary<FacilityLightGroupId, Action> lightClickHandlers = new();
        private Action<FacilityThemeId> themeRequestHandler;
        private Action<FacilityLightGroupId> lightRequestHandler;
        private VisualElement focusedControl;
        private bool isPanelOpen;

        public event Action CloseRequested;
        public string FocusedControlName => focusedControl?.name ?? string.Empty;

        private FacilityControlPanelView(
            VisualElement appShell,
            VisualElement appSafeArea,
            VisualElement facilityPanel,
            VisualElement statusCard,
            VisualElement loadingOverlay,
            Button closeButton,
            Label currentLevelLabel,
            Label controlAvailabilityLabel,
            Label transitionPhaseLabel,
            Label transitionStatusLabel,
            Label loadingStatusLabel,
            VisualElement levelButtons)
        {
            this.appShell = appShell;
            this.appSafeArea = appSafeArea;
            this.facilityPanel = facilityPanel;
            this.statusCard = statusCard;
            this.loadingOverlay = loadingOverlay;
            this.closeButton = closeButton;
            this.currentLevelLabel = currentLevelLabel;
            this.controlAvailabilityLabel = controlAvailabilityLabel;
            this.transitionPhaseLabel = transitionPhaseLabel;
            this.transitionStatusLabel = transitionStatusLabel;
            this.loadingStatusLabel = loadingStatusLabel;
            this.levelButtons = levelButtons;
        }

        public static bool TryCreate(VisualElement root, out FacilityControlPanelView view)
        {
            view = null;

            if (root == null)
            {
                return false;
            }

            VisualElement appShell = root.Q<VisualElement>(FacilityControlPanelElementNames.AppShell);
            VisualElement appSafeArea = root.Q<VisualElement>(FacilityControlPanelElementNames.AppSafeArea);
            VisualElement facilityPanel = root.Q<VisualElement>(FacilityControlPanelElementNames.FacilityPanel);
            VisualElement statusCard = root.Q<VisualElement>(FacilityControlPanelElementNames.StatusCard);
            VisualElement loadingOverlay = root.Q<VisualElement>(FacilityControlPanelElementNames.LoadingOverlay);
            Button closeButton = root.Q<Button>(FacilityControlPanelElementNames.CloseButton);
            Label currentLevelLabel = root.Q<Label>(FacilityControlPanelElementNames.CurrentLevelLabel);
            Label controlAvailabilityLabel = root.Q<Label>(FacilityControlPanelElementNames.ControlAvailabilityLabel);
            Label transitionPhaseLabel = root.Q<Label>(FacilityControlPanelElementNames.TransitionPhaseLabel);
            Label transitionStatusLabel = root.Q<Label>(FacilityControlPanelElementNames.TransitionStatusLabel);
            Label loadingStatusLabel = root.Q<Label>(FacilityControlPanelElementNames.LoadingStatusLabel);
            VisualElement levelButtons = root.Q<VisualElement>(FacilityControlPanelElementNames.LevelButtons);

            if (appShell == null
                || appSafeArea == null
                || facilityPanel == null
                || statusCard == null
                || loadingOverlay == null
                || closeButton == null
                || currentLevelLabel == null
                || controlAvailabilityLabel == null
                || transitionPhaseLabel == null
                || transitionStatusLabel == null
                || loadingStatusLabel == null
                || levelButtons == null)
            {
                return false;
            }

            root.pickingMode = PickingMode.Ignore;
            view = new FacilityControlPanelView(
                appShell,
                appSafeArea,
                facilityPanel,
                statusCard,
                loadingOverlay,
                closeButton,
                currentLevelLabel,
                controlAvailabilityLabel,
                transitionPhaseLabel,
                transitionStatusLabel,
                loadingStatusLabel,
                levelButtons);
            view.ConfigurePickingModes();
            return true;
        }

        public void Present(AppState state)
        {
            bool isBusy = state.IsTransitioning;
            string currentName = string.IsNullOrWhiteSpace(state.CurrentLevelName)
                ? "Starting..."
                : state.CurrentLevelName;
            string statusMessage = GetStatusMessage(state);

            currentLevelLabel.text = $"CURRENT AREA  /  {currentName}";
            transitionPhaseLabel.text = $"PHASE  /  {GetPhaseName(state.TransitionPhase)}";
            transitionStatusLabel.text = statusMessage;
            loadingStatusLabel.text = statusMessage;
            loadingOverlay.EnableInClassList(FacilityControlPanelStyleClasses.LoadingVisible, isBusy);
            loadingOverlay.pickingMode = isBusy ? PickingMode.Position : PickingMode.Ignore;
            appShell.EnableInClassList(FacilityControlPanelStyleClasses.Idle, state.TransitionPhase == FacilityTransitionPhase.Idle);
            appShell.EnableInClassList(FacilityControlPanelStyleClasses.Loading, isBusy);
            appShell.EnableInClassList(FacilityControlPanelStyleClasses.Success, state.TransitionPhase == FacilityTransitionPhase.Complete);
            appShell.EnableInClassList(FacilityControlPanelStyleClasses.Error, state.HasError);

            for (int index = 0; index < levelButtonBindings.Count; index++)
            {
                LevelButtonBinding binding = levelButtonBindings[index];
                bool isCurrentLevel = string.Equals(
                    state.CurrentLevelId,
                    binding.Definition.LevelId,
                    StringComparison.OrdinalIgnoreCase);
                binding.Button.SetEnabled(!isBusy && !isCurrentLevel);
                binding.Button.EnableInClassList(FacilityControlPanelStyleClasses.Selected, isCurrentLevel);
            }
        }

        public void PresentInputOwnership(PlayerInputOwner owner)
        {
            isPanelOpen = owner == PlayerInputOwner.UserInterface;
            facilityPanel.EnableInClassList(FacilityControlPanelStyleClasses.PanelOpen, isPanelOpen);
            facilityPanel.pickingMode = isPanelOpen ? PickingMode.Position : PickingMode.Ignore;

            if (isPanelOpen)
            {
                appShell.schedule.Execute(FocusFirstAvailableControl);
            }
            else
            {
                ClearFocus();
            }
        }

        public void PresentLayoutMode(bool isCompact)
        {
            appShell.EnableInClassList(FacilityControlPanelStyleClasses.AppShellCompact, isCompact);
            appShell.EnableInClassList(FacilityControlPanelStyleClasses.AppShellWide, !isCompact);
        }

        public void ApplySafeArea(Rect safeArea)
        {
            appSafeArea.style.left = safeArea.xMin;
            appSafeArea.style.right = appShell.contentRect.width - safeArea.xMax;
            appSafeArea.style.top = appShell.contentRect.height - safeArea.yMax;
            appSafeArea.style.bottom = safeArea.yMin;
        }

        public void BuildNavigation(
            IReadOnlyList<LevelDefinition> catalog,
            Action<LevelDefinition> requestTransition)
        {
            ClearNavigation();

            if (catalog == null || requestTransition == null)
            {
                return;
            }

            for (int index = 0; index < catalog.Count; index++)
            {
                LevelDefinition definition = catalog[index];

                if (definition == null)
                {
                    continue;
                }

                Button button = new()
                {
                    name = FacilityControlPanelLayout.BuildLevelButtonName(definition.LevelId),
                    text = definition.DisplayName.ToUpperInvariant(),
                    tooltip = $"Move to {definition.DisplayName}"
                };
                button.AddToClassList(FacilityControlPanelStyleClasses.LevelButton);

                EventCallback<ClickEvent> clickHandler = _ => requestTransition(definition);
                button.RegisterCallback(clickHandler);
                levelButtons.Add(button);
                levelButtonBindings.Add(new LevelButtonBinding(definition, button, clickHandler));
            }
        }

        public void ClearNavigation()
        {
            for (int index = 0; index < levelButtonBindings.Count; index++)
            {
                LevelButtonBinding binding = levelButtonBindings[index];
                binding.Button.UnregisterCallback(binding.ClickHandler);
            }

            levelButtonBindings.Clear();
            levelButtons.Clear();
        }

        public void SubscribePanelCommands()
        {
            closeButton.RegisterCallback<ClickEvent>(OnCloseClicked);
            facilityPanel.RegisterCallback<FocusInEvent>(OnPanelFocusIn);
        }

        public void UnsubscribePanelCommands()
        {
            closeButton.UnregisterCallback<ClickEvent>(OnCloseClicked);
            facilityPanel.UnregisterCallback<FocusInEvent>(OnPanelFocusIn);
            ClearFocus();
        }

        public bool TryCloseFromKeyboard(KeyDownEvent evt)
        {
            if (!isPanelOpen || evt.keyCode != KeyCode.Escape)
            {
                return false;
            }

            evt.StopPropagation();
            CloseRequested?.Invoke();
            return true;
        }

        public void ClearFocus()
        {
            focusedControl?.Blur();
            focusedControl = null;
        }

        public void SubscribeControlRequests(
            Action<FacilityThemeId> requestTheme,
            Action<FacilityLightGroupId> requestLightGroup)
        {
            UnsubscribeControlRequests();
            themeRequestHandler = requestTheme;
            lightRequestHandler = requestLightGroup;

            RegisterThemeButton(FacilityThemeId.Standard, FacilityControlPanelIds.StandardThemeButtonName);
            RegisterThemeButton(FacilityThemeId.Maintenance, FacilityControlPanelIds.MaintenanceThemeButtonName);
            RegisterThemeButton(FacilityThemeId.Emergency, FacilityControlPanelIds.EmergencyThemeButtonName);
            RegisterLightButton(FacilityLightGroupId.Ambient, FacilityControlPanelIds.AmbientLightButtonName);
            RegisterLightButton(FacilityLightGroupId.Operations, FacilityControlPanelIds.OperationsLightButtonName);
            RegisterLightButton(FacilityLightGroupId.Emergency, FacilityControlPanelIds.EmergencyLightButtonName);
        }

        public void UnsubscribeControlRequests()
        {
            foreach (KeyValuePair<FacilityThemeId, Button> entry in themeButtons)
            {
                entry.Value.clicked -= themeClickHandlers[entry.Key];
            }

            foreach (KeyValuePair<FacilityLightGroupId, Button> entry in lightButtons)
            {
                entry.Value.clicked -= lightClickHandlers[entry.Key];
            }

            themeButtons.Clear();
            lightButtons.Clear();
            themeClickHandlers.Clear();
            lightClickHandlers.Clear();
            themeRequestHandler = null;
            lightRequestHandler = null;
        }

        public void PresentThemeControls(bool isAvailable, FacilityThemeId? selected)
        {
            foreach (FacilityThemeId theme in Enum.GetValues(typeof(FacilityThemeId)))
            {
                if (!themeButtons.TryGetValue(theme, out Button button))
                {
                    continue;
                }

                bool isSelected = isAvailable && selected == theme;
                button.text = isAvailable
                    ? FacilityControlPanelIds.GetDisplayName(theme)
                    : $"{FacilityControlPanelIds.GetDisplayName(theme)} (UNAVAILABLE)";
                button.tooltip = isAvailable
                    ? $"Request {FacilityControlPanelIds.GetStableId(theme)} theme"
                    : "Theme switching is unavailable until a theme service is configured.";
                button.SetEnabled(isAvailable);
                button.EnableInClassList(FacilityControlPanelStyleClasses.Selected, isSelected);
            }
        }

        public void PresentLightControls(
            bool isAvailable,
            IReadOnlyDictionary<FacilityLightGroupId, bool> groupStates)
        {
            foreach (FacilityLightGroupId group in Enum.GetValues(typeof(FacilityLightGroupId)))
            {
                if (!lightButtons.TryGetValue(group, out Button button))
                {
                    continue;
                }

                bool isSelected = isAvailable
                    && groupStates != null
                    && groupStates.TryGetValue(group, out bool isActive)
                    && isActive;
                button.text = isAvailable
                    ? FacilityControlPanelIds.GetDisplayName(group)
                    : $"{FacilityControlPanelIds.GetDisplayName(group)} (UNAVAILABLE)";
                button.tooltip = isAvailable
                    ? $"Request {FacilityControlPanelIds.GetStableId(group)} light group"
                    : "Lighting controls are unavailable until a light-group service is configured.";
                button.SetEnabled(isAvailable);
                button.EnableInClassList(FacilityControlPanelStyleClasses.Selected, isSelected);
            }
        }

        public void PresentControlAvailability(bool themesAvailable, bool lightingAvailable)
        {
            controlAvailabilityLabel.text = themesAvailable && lightingAvailable
                ? "CONTROL SERVICES  /  READY"
                : themesAvailable
                    ? "CONTROL SERVICES  /  LIGHTING UNAVAILABLE"
                    : lightingAvailable
                        ? "CONTROL SERVICES  /  THEME UNAVAILABLE"
                        : "CONTROL SERVICES  /  THEME AND LIGHTING UNAVAILABLE";
        }

        private void RegisterThemeButton(FacilityThemeId theme, string buttonName)
        {
            Button button = appShell.Q<Button>(buttonName);

            if (button == null)
            {
                return;
            }

            Action clickHandler = () => themeRequestHandler?.Invoke(theme);
            button.clicked += clickHandler;
            themeButtons.Add(theme, button);
            themeClickHandlers.Add(theme, clickHandler);
        }

        private void RegisterLightButton(FacilityLightGroupId group, string buttonName)
        {
            Button button = appShell.Q<Button>(buttonName);

            if (button == null)
            {
                return;
            }

            Action clickHandler = () => lightRequestHandler?.Invoke(group);
            button.clicked += clickHandler;
            lightButtons.Add(group, button);
            lightClickHandlers.Add(group, clickHandler);
        }

        private void ConfigurePickingModes()
        {
            appShell.pickingMode = PickingMode.Ignore;
            appSafeArea.pickingMode = PickingMode.Ignore;
            statusCard.pickingMode = PickingMode.Ignore;
            facilityPanel.pickingMode = PickingMode.Ignore;
            loadingOverlay.pickingMode = PickingMode.Ignore;
        }

        private void FocusFirstAvailableControl()
        {
            if (!isPanelOpen)
            {
                return;
            }

            for (int index = 0; index < levelButtonBindings.Count; index++)
            {
                Button button = levelButtonBindings[index].Button;

                if (button.enabledSelf)
                {
                    FocusControl(button);
                    return;
                }
            }

            if (closeButton.enabledSelf)
            {
                FocusControl(closeButton);
            }
        }

        private void FocusControl(VisualElement control)
        {
            focusedControl = control;
            control.Focus();
        }

        private void OnPanelFocusIn(FocusInEvent evt)
        {
            focusedControl = evt.target as VisualElement;
        }

        private void OnCloseClicked(ClickEvent _)
        {
            CloseRequested?.Invoke();
        }

        private static string GetStatusMessage(AppState state)
        {
            if (!string.IsNullOrWhiteSpace(state.StatusMessage))
            {
                return state.StatusMessage;
            }

            return state.TransitionPhase switch
            {
                FacilityTransitionPhase.Idle => "Application services ready",
                FacilityTransitionPhase.Validating => "Validating destination",
                FacilityTransitionPhase.Loading => "Loading facility",
                FacilityTransitionPhase.Teleporting => "Moving to destination",
                FacilityTransitionPhase.Unloading => "Finalizing facility transition",
                FacilityTransitionPhase.Complete => "Facility transition complete",
                FacilityTransitionPhase.Failed => "Facility transition failed",
                _ => "Application services starting"
            };
        }

        private static string GetPhaseName(FacilityTransitionPhase phase)
        {
            return phase switch
            {
                FacilityTransitionPhase.Idle => "READY",
                FacilityTransitionPhase.Validating => "VALIDATING",
                FacilityTransitionPhase.Loading => "LOADING",
                FacilityTransitionPhase.Teleporting => "TELEPORTING",
                FacilityTransitionPhase.Unloading => "FINALIZING",
                FacilityTransitionPhase.Complete => "COMPLETE",
                FacilityTransitionPhase.Failed => "ERROR",
                _ => "STARTING"
            };
        }

        private readonly struct LevelButtonBinding
        {
            public LevelButtonBinding(
                LevelDefinition definition,
                Button button,
                EventCallback<ClickEvent> clickHandler)
            {
                Definition = definition;
                Button = button;
                ClickHandler = clickHandler;
            }

            public LevelDefinition Definition { get; }
            public Button Button { get; }
            public EventCallback<ClickEvent> ClickHandler { get; }
        }
    }
}

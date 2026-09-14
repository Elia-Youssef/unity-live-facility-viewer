using System;
using FacilityViewer.Core;
using FacilityViewer.Services;
using UnityEngine;

namespace FacilityViewer.UI
{
    /// <summary>
    /// Connects the panel's typed theme-request seam to the persistent material-theme service.
    /// It owns presentation availability and selection feedback; material assignment remains a
    /// world-service responsibility.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FacilityControlPanelPresenter))]
    public sealed class MaterialThemePanelCoordinator : MonoBehaviour
    {
        [SerializeField] private FacilityControlPanelPresenter panelPresenter;
        [SerializeField] private MaterialThemeService materialThemeService;
        [SerializeField] private AppState appState;

        private bool isSubscribed;

        public bool IsThemeControlsAvailable => CanUseThemeControls();

        private void Reset()
        {
            panelPresenter = GetComponent<FacilityControlPanelPresenter>();
        }

        private void OnEnable()
        {
            panelPresenter ??= GetComponent<FacilityControlPanelPresenter>();

            if (panelPresenter == null || materialThemeService == null || appState == null)
            {
                Debug.LogError("[Facility UI] Material theme panel coordinator references are incomplete.", this);
                panelPresenter?.SetThemeControlsAvailable(false);
                enabled = false;
                return;
            }

            Subscribe();
            RefreshPresentation();
        }

        private void Start()
        {
            RefreshPresentation();
        }

        private void OnDisable()
        {
            Unsubscribe();
            panelPresenter?.SetThemeControlsAvailable(false);
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            panelPresenter.ThemeRequested += RequestTheme;
            appState.Changed += OnAppStateChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            panelPresenter.ThemeRequested -= RequestTheme;
            appState.Changed -= OnAppStateChanged;
            isSubscribed = false;
        }

        private void RequestTheme(FacilityThemeId requestedTheme)
        {
            if (!TryGetThemeId(requestedTheme, out string themeId))
            {
                appState.SetStatusMessage("Theme request is invalid.");
                RefreshPresentation();
                return;
            }

            if (!CanUseThemeControls())
            {
                appState.SetStatusMessage("Theme service is unavailable.");
                RefreshPresentation();
                return;
            }

            if (!materialThemeService.RequestTheme(themeId))
            {
                appState.SetStatusMessage(
                    $"Unable to activate {FacilityControlPanelIds.GetDisplayName(requestedTheme)} theme.");
            }

            RefreshPresentation();
        }

        private void OnAppStateChanged(AppState _)
        {
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            if (panelPresenter == null)
            {
                return;
            }

            bool isAvailable = CanUseThemeControls();
            panelPresenter.SetThemeControlsAvailable(isAvailable);
            panelPresenter.PresentSelectedTheme(
                isAvailable && TryGetTheme(appState.SelectedThemeId, out FacilityThemeId selectedTheme)
                    ? selectedTheme
                    : null);
        }

        private bool CanUseThemeControls()
        {
            return panelPresenter != null
                && panelPresenter.HasThemeControlContract
                && materialThemeService != null
                && materialThemeService.IsReady
                && appState != null;
        }

        private static bool TryGetThemeId(FacilityThemeId theme, out string themeId)
        {
            switch (theme)
            {
                case FacilityThemeId.Standard:
                case FacilityThemeId.Maintenance:
                case FacilityThemeId.Emergency:
                    themeId = FacilityControlPanelIds.GetStableId(theme);
                    return true;
                default:
                    themeId = string.Empty;
                    return false;
            }
        }

        private static bool TryGetTheme(string themeId, out FacilityThemeId theme)
        {
            foreach (FacilityThemeId candidate in Enum.GetValues(typeof(FacilityThemeId)))
            {
                if (string.Equals(
                        FacilityControlPanelIds.GetStableId(candidate),
                        themeId,
                        StringComparison.Ordinal))
                {
                    theme = candidate;
                    return true;
                }
            }

            theme = default;
            return false;
        }
    }
}

using System;

namespace FacilityViewer.UI
{
    public enum FacilityThemeId
    {
        Standard,
        Maintenance,
        Emergency
    }

    public enum FacilityLightGroupId
    {
        Ambient,
        Operations,
        Emergency
    }

    public static class FacilityControlPanelIds
    {
        public const string StandardThemeButtonName = "theme-standard-button";
        public const string MaintenanceThemeButtonName = "theme-maintenance-button";
        public const string EmergencyThemeButtonName = "theme-emergency-button";
        public const string AmbientLightButtonName = "lighting-ambient-button";
        public const string OperationsLightButtonName = "lighting-operations-button";
        public const string EmergencyLightButtonName = "lighting-emergency-button";

        public static string GetStableId(FacilityThemeId theme)
        {
            return theme switch
            {
                FacilityThemeId.Standard => "standard",
                FacilityThemeId.Maintenance => "maintenance",
                FacilityThemeId.Emergency => "emergency",
                _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null)
            };
        }

        public static string GetStableId(FacilityLightGroupId group)
        {
            return group switch
            {
                FacilityLightGroupId.Ambient => "ambient",
                FacilityLightGroupId.Operations => "operations",
                FacilityLightGroupId.Emergency => "emergency",
                _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
            };
        }

        public static string GetDisplayName(FacilityThemeId theme)
        {
            return theme.ToString().ToUpperInvariant();
        }

        public static string GetDisplayName(FacilityLightGroupId group)
        {
            return group.ToString().ToUpperInvariant();
        }
    }

    internal static class FacilityControlPanelElementNames
    {
        public const string AppShell = "app-shell";
        public const string AppSafeArea = "app-safe-area";
        public const string FacilityPanel = "facility-panel";
        public const string StatusCard = "status-card";
        public const string LoadingOverlay = "loading-overlay";
        public const string CloseButton = "close-panel-button";
        public const string CurrentLevelLabel = "current-level-label";
        public const string ControlAvailabilityLabel = "control-availability-label";
        public const string TransitionPhaseLabel = "transition-phase-label";
        public const string TransitionStatusLabel = "transition-status-label";
        public const string LoadingStatusLabel = "loading-status-label";
        public const string LevelButtons = "level-buttons";
    }

    internal static class FacilityControlPanelStyleClasses
    {
        public const string PanelOpen = "facility-panel--open";
        public const string AppShellWide = "app-shell--wide";
        public const string AppShellCompact = "app-shell--compact";
        public const string LoadingVisible = "is-visible";
        public const string Selected = "is-selected";
        public const string LevelButton = "level-button";
        public const string Idle = "app-shell--state-idle";
        public const string Loading = "app-shell--state-loading";
        public const string Success = "app-shell--state-success";
        public const string Error = "app-shell--state-error";
    }
}

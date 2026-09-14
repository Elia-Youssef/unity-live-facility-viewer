using System;
using UnityEngine;

namespace FacilityViewer.Core
{
    [DisallowMultipleComponent]
    public sealed class AppState : MonoBehaviour
    {
        [SerializeField] private FacilityTransitionPhase transitionPhase =
            FacilityTransitionPhase.Uninitialized;
        [SerializeField] private string currentLevelId = string.Empty;
        [SerializeField] private string currentLevelName = string.Empty;
        [SerializeField] private string statusMessage = string.Empty;
        [SerializeField] private string selectedThemeId = MaterialThemeIds.Standard;

        public event Action<AppState> Changed;

        public FacilityTransitionPhase TransitionPhase => transitionPhase;
        public string CurrentLevelId => currentLevelId;
        public string CurrentLevelName => currentLevelName;
        public string StatusMessage => statusMessage;
        public string SelectedThemeId => selectedThemeId;
        public bool IsInitialized => transitionPhase != FacilityTransitionPhase.Uninitialized;
        public bool IsTransitioning => IsActiveTransitionPhase(transitionPhase);
        public bool HasError => transitionPhase == FacilityTransitionPhase.Failed;

        public void Initialize(string initialStatus = "Application services ready")
        {
            currentLevelId = string.Empty;
            currentLevelName = string.Empty;
            selectedThemeId = string.IsNullOrWhiteSpace(selectedThemeId)
                ? MaterialThemeIds.Standard
                : selectedThemeId.Trim();
            ApplyTransitionState(FacilityTransitionPhase.Idle, initialStatus);
        }

        public void SetTransitionPhase(FacilityTransitionPhase phase, string message)
        {
            if (phase == FacilityTransitionPhase.Uninitialized)
            {
                throw new ArgumentException(
                    "Use Initialize before assigning a transition phase.",
                    nameof(phase));
            }

            ApplyTransitionState(phase, message);
        }

        public void SetCurrentLevel(string levelId, string levelName)
        {
            currentLevelId = levelId?.Trim() ?? string.Empty;
            currentLevelName = levelName?.Trim() ?? string.Empty;
            Changed?.Invoke(this);
        }

        public void SetFailure(string message)
        {
            ApplyTransitionState(FacilityTransitionPhase.Failed, message);
        }

        /// <summary>
        /// Updates the selected material-theme ID without changing the active transition phase.
        /// </summary>
        public bool SetSelectedTheme(string themeId)
        {
            string normalizedThemeId = themeId?.Trim() ?? string.Empty;

            if (!MaterialThemeIds.IsKnown(normalizedThemeId))
            {
                return false;
            }

            if (string.Equals(selectedThemeId, normalizedThemeId, StringComparison.Ordinal))
            {
                return true;
            }

            selectedThemeId = normalizedThemeId;
            Changed?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Publishes a status message without changing the active transition phase.
        /// </summary>
        public void SetStatusMessage(string message)
        {
            statusMessage = message?.Trim() ?? string.Empty;
            Changed?.Invoke(this);
        }

        public static bool IsActiveTransitionPhase(FacilityTransitionPhase phase)
        {
            return phase is FacilityTransitionPhase.Validating
                or FacilityTransitionPhase.Loading
                or FacilityTransitionPhase.Teleporting
                or FacilityTransitionPhase.Unloading;
        }

        private void ApplyTransitionState(FacilityTransitionPhase phase, string message)
        {
            transitionPhase = phase;
            statusMessage = message?.Trim() ?? string.Empty;
            Changed?.Invoke(this);
        }
    }
}

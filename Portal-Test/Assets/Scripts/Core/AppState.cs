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

        public event Action<AppState> Changed;

        public FacilityTransitionPhase TransitionPhase => transitionPhase;
        public string CurrentLevelId => currentLevelId;
        public string CurrentLevelName => currentLevelName;
        public string StatusMessage => statusMessage;
        public bool IsInitialized => transitionPhase != FacilityTransitionPhase.Uninitialized;
        public bool IsTransitioning => IsActiveTransitionPhase(transitionPhase);
        public bool HasError => transitionPhase == FacilityTransitionPhase.Failed;

        public void Initialize(string initialStatus = "Application services ready")
        {
            currentLevelId = string.Empty;
            currentLevelName = string.Empty;
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

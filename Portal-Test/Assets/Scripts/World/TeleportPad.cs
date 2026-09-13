using System;
using FacilityViewer.Core;
using UnityEngine;

namespace FacilityViewer.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TeleportPad : MonoBehaviour
    {
        [SerializeField] private LevelDefinition destinationLevel;
        [SerializeField] private string destinationSpawnId = "entrance";
        [SerializeField] private string interactionPrompt = string.Empty;
        [SerializeField] private bool isAvailable = true;
        [SerializeField] private BoxCollider interactionTrigger;
        [SerializeField] private TextMesh destinationLabel;

        private bool isTransitionLocked;

        public LevelDefinition DestinationLevel => destinationLevel;
        public string DestinationSpawnId => destinationSpawnId;
        public string DestinationDisplayName => destinationLevel != null
            ? destinationLevel.DisplayName
            : string.Empty;
        public string InteractionPrompt
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(interactionPrompt))
                {
                    return interactionPrompt;
                }

                return string.IsNullOrWhiteSpace(DestinationDisplayName)
                    ? "USE TELEPORT PAD"
                    : $"USE: {DestinationDisplayName}";
            }
        }
        public bool IsAvailable => isAvailable;
        public bool IsTransitionLocked => isTransitionLocked;
        public bool HasValidConfiguration => TryValidate(out _);
        public bool CanInteract =>
            isAvailable && !isTransitionLocked && isActiveAndEnabled && HasValidConfiguration;

        public static event Action<TeleportPad> Enabled;
        public static event Action<TeleportPad> Disabled;
        public event Action<TeleportPad, bool> AvailabilityChanged;

        private void Reset()
        {
            interactionTrigger = GetComponent<BoxCollider>();
            interactionTrigger.isTrigger = true;
            destinationLabel = GetComponentInChildren<TextMesh>(true);
        }

        private void OnEnable()
        {
            RefreshDestinationLabel();
            Enabled?.Invoke(this);
            AvailabilityChanged?.Invoke(this, CanInteract);
        }

        private void OnDisable()
        {
            Disabled?.Invoke(this);
            AvailabilityChanged?.Invoke(this, false);
        }

        private void OnValidate()
        {
            destinationSpawnId = destinationSpawnId?.Trim() ?? string.Empty;
            interactionPrompt = interactionPrompt?.Trim() ?? string.Empty;
            interactionTrigger ??= GetComponent<BoxCollider>();
            destinationLabel ??= GetComponentInChildren<TextMesh>(true);
            RefreshDestinationLabel();
        }

        public void SetAvailable(bool available)
        {
            if (isAvailable == available)
            {
                return;
            }

            isAvailable = available;
            AvailabilityChanged?.Invoke(this, CanInteract);
        }

        public void SetTransitionLocked(bool isLocked)
        {
            if (isTransitionLocked == isLocked)
            {
                return;
            }

            isTransitionLocked = isLocked;
            AvailabilityChanged?.Invoke(this, CanInteract);
        }

        public void RefreshDestinationLabel()
        {
            if (destinationLabel == null)
            {
                return;
            }

            destinationLabel.text = string.IsNullOrWhiteSpace(DestinationDisplayName)
                ? "DESTINATION"
                : DestinationDisplayName.ToUpperInvariant();
        }

        public bool IsInteractionTrigger(Collider candidate)
        {
            return candidate != null && candidate == interactionTrigger;
        }

        public bool TryValidate(out string error)
        {
            if (destinationLevel == null)
            {
                error = "Destination level is not assigned.";
                return false;
            }

            if (!destinationLevel.TryValidate(out string levelError))
            {
                error = $"Destination level is invalid: {levelError}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(destinationSpawnId))
            {
                error = $"Teleport pad to '{destinationLevel.DisplayName}' has no destination spawn ID.";
                return false;
            }

            if (interactionTrigger == null)
            {
                error = $"Teleport pad to '{destinationLevel.DisplayName}' has no interaction trigger.";
                return false;
            }

            if (!interactionTrigger.isTrigger)
            {
                error = $"Teleport pad to '{destinationLevel.DisplayName}' requires a trigger collider.";
                return false;
            }

            if (interactionTrigger.transform != transform
                && !interactionTrigger.transform.IsChildOf(transform))
            {
                error = $"Teleport pad to '{destinationLevel.DisplayName}' does not own its interaction trigger.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}

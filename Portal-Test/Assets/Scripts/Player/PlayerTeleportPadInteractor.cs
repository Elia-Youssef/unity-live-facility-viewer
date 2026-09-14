using System;
using System.Collections.Generic;
using FacilityViewer.World;
using UnityEngine;

namespace FacilityViewer.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputRouter))]
    public sealed class PlayerTeleportPadInteractor : MonoBehaviour
    {
        private const int OverlapCapacity = 16;
        private const int RecoveryOverlapCapacity = 128;
        private const int TeleportPadLayer = 8;
        private const int TeleportPadLayerMask = 1 << TeleportPadLayer;

        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private CharacterController characterController;
        [SerializeField]
        [Tooltip("Only the TeleportPad layer (layer 8) is queried for pad interaction.")]
        private LayerMask interactionLayers = TeleportPadLayerMask;

        private readonly List<TeleportPad> overlappingPads = new();
        private readonly HashSet<TeleportPad> detectedPads = new();
        private readonly Collider[] overlapResults = new Collider[OverlapCapacity];
        private readonly Collider[] recoveryOverlapResults = new Collider[RecoveryOverlapCapacity];

        public TeleportPad ActivePad { get; private set; }
        public bool HasAvailablePad => ActivePad != null && ActivePad.CanInteract;
        public int DetectedPadCount => overlappingPads.Count;
        public bool UsedOverlapRecovery { get; private set; }
        public bool IsOverlapRecoverySaturated { get; private set; }

        public event Action<TeleportPad> ActivePadChanged;
        public event Action<TeleportPad> TeleportRequested;

        private void Reset()
        {
            inputRouter = GetComponent<PlayerInputRouter>();
            characterController = GetComponent<CharacterController>();
            interactionLayers = TeleportPadLayerMask;
        }

        private void OnEnable()
        {
            inputRouter ??= GetComponent<PlayerInputRouter>();
            characterController ??= GetComponent<CharacterController>();

            if (inputRouter == null || characterController == null)
            {
                Debug.LogError(
                    "PlayerTeleportPadInteractor requires a PlayerInputRouter and CharacterController.",
                    this);
                enabled = false;
                return;
            }

            inputRouter.InteractRequested += OnInteractRequested;
        }

        private void OnDisable()
        {
            if (inputRouter != null)
            {
                inputRouter.InteractRequested -= OnInteractRequested;
            }

            for (int index = 0; index < overlappingPads.Count; index++)
            {
                TeleportPad pad = overlappingPads[index];

                if (pad != null)
                {
                    pad.AvailabilityChanged -= OnPadAvailabilityChanged;
                }
            }

            overlappingPads.Clear();
            detectedPads.Clear();
            SetActivePad(null);
        }

        private void Update()
        {
            RefreshDetectedPads();
        }

        public void RefreshDetectedPads()
        {
            detectedPads.Clear();
            Vector3 scale = transform.lossyScale;
            float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float radius = characterController.radius * radiusScale;
            float height = Mathf.Max(characterController.height * Mathf.Abs(scale.y), radius * 2f);
            float segmentOffset = height * 0.5f - radius;
            Vector3 center = transform.TransformPoint(characterController.center);
            Vector3 verticalOffset = transform.up * segmentOffset;
            int overlapCount = Physics.OverlapCapsuleNonAlloc(
                center + verticalOffset,
                center - verticalOffset,
                radius,
                overlapResults,
                interactionLayers,
                QueryTriggerInteraction.Collide);

            UsedOverlapRecovery = overlapCount >= overlapResults.Length;

            if (UsedOverlapRecovery)
            {
                ClearOverlapResults(overlapResults, overlapCount);
                overlapCount = Physics.OverlapCapsuleNonAlloc(
                    center + verticalOffset,
                    center - verticalOffset,
                    radius,
                    recoveryOverlapResults,
                    interactionLayers,
                    QueryTriggerInteraction.Collide);
                CollectDetectedPads(recoveryOverlapResults, overlapCount);

                bool recoverySaturated = overlapCount >= recoveryOverlapResults.Length;

                if (recoverySaturated && !IsOverlapRecoverySaturated)
                {
                    Debug.LogWarning(
                        $"PlayerTeleportPadInteractor overlap recovery reached its "
                        + $"{RecoveryOverlapCapacity}-collider limit; some pads may not be detected.",
                        this);
                }

                IsOverlapRecoverySaturated = recoverySaturated;
            }
            else
            {
                CollectDetectedPads(overlapResults, overlapCount);
                IsOverlapRecoverySaturated = false;
            }

            for (int index = overlappingPads.Count - 1; index >= 0; index--)
            {
                TeleportPad pad = overlappingPads[index];

                if (pad == null)
                {
                    overlappingPads.RemoveAt(index);
                    continue;
                }

                if (!detectedPads.Contains(pad))
                {
                    pad.AvailabilityChanged -= OnPadAvailabilityChanged;
                    overlappingPads.RemoveAt(index);
                }
            }

            foreach (TeleportPad pad in detectedPads)
            {
                if (overlappingPads.Contains(pad))
                {
                    continue;
                }

                overlappingPads.Add(pad);
                pad.AvailabilityChanged += OnPadAvailabilityChanged;
            }

            RefreshActivePad();
        }

        private void CollectDetectedPads(Collider[] results, int resultCount)
        {
            for (int index = 0; index < resultCount; index++)
            {
                Collider candidate = results[index];
                TeleportPad pad = candidate != null
                    ? candidate.GetComponentInParent<TeleportPad>()
                    : null;

                if (pad != null && pad.IsInteractionTrigger(candidate))
                {
                    detectedPads.Add(pad);
                }
            }

            ClearOverlapResults(results, resultCount);
        }

        private static void ClearOverlapResults(Collider[] results, int resultCount)
        {
            for (int index = 0; index < resultCount; index++)
            {
                results[index] = null;
            }
        }

        public void RefreshActivePad()
        {
            TeleportPad closestPad = null;
            float closestDistance = float.PositiveInfinity;

            for (int index = overlappingPads.Count - 1; index >= 0; index--)
            {
                TeleportPad pad = overlappingPads[index];

                if (pad == null)
                {
                    overlappingPads.RemoveAt(index);
                    continue;
                }

                if (!pad.CanInteract)
                {
                    continue;
                }

                float distance = (pad.transform.position - transform.position).sqrMagnitude;

                if (distance < closestDistance)
                {
                    closestPad = pad;
                    closestDistance = distance;
                }
            }

            SetActivePad(closestPad);
        }

        private void OnPadAvailabilityChanged(TeleportPad _, bool __)
        {
            RefreshActivePad();
        }

        private void OnInteractRequested()
        {
            RefreshDetectedPads();

            if (ActivePad != null && ActivePad.CanInteract)
            {
                TeleportRequested?.Invoke(ActivePad);
            }
        }

        private void SetActivePad(TeleportPad pad)
        {
            if (ActivePad == pad)
            {
                return;
            }

            ActivePad = pad;
            ActivePadChanged?.Invoke(ActivePad);
        }
    }
}

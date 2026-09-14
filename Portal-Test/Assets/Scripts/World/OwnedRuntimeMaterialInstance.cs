using System.Collections.Generic;
using FacilityViewer.Core;
using UnityEngine;

namespace FacilityViewer.World
{
    /// <summary>
    /// Explicit owner for the comparison's one runtime material instance. It clones its saved
    /// source once, assigns that clone through shared-material APIs, restores the saved slot, and
    /// destroys the owned instance when this owner is destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OwnedRuntimeMaterialInstance : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID(FacilitySurfaceShader.BaseColor);

        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private int materialSlotIndex;
        [SerializeField] private Material sourceMaterial;
        [SerializeField] private Color baseColorOverride = new(0.25f, 0.85f, 1f, 1f);

        private readonly List<Material> sharedMaterials = new();
        private Material originalMaterial;
        private Material runtimeMaterial;

        public Material RuntimeMaterial => runtimeMaterial;
        public int CreatedInstanceCount { get; private set; }

        private void Reset()
        {
            targetRenderer = GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            TryCreateAndAssign(out _);
        }

        private void OnDestroy()
        {
            RestoreAndDestroy();
        }

        /// <summary>
        /// Creates and assigns exactly one owned material clone. Later enable calls retain the
        /// same clone, so this component cannot grow material instances during its lifetime.
        /// </summary>
        public bool TryCreateAndAssign(out string error)
        {
            if (runtimeMaterial != null)
            {
                error = string.Empty;
                return true;
            }

            if (!TryValidate(out error))
            {
                return false;
            }

            targetRenderer.GetSharedMaterials(sharedMaterials);
            originalMaterial = sharedMaterials[materialSlotIndex];
            runtimeMaterial = new Material(sourceMaterial)
            {
                name = $"{sourceMaterial.name} (Owned Runtime Comparison)"
            };
            runtimeMaterial.SetColor(BaseColorId, baseColorOverride);
            sharedMaterials[materialSlotIndex] = runtimeMaterial;
            targetRenderer.SetSharedMaterials(sharedMaterials);
            CreatedInstanceCount++;
            error = string.Empty;
            return true;
        }

        public bool TryValidate(out string error)
        {
            if (targetRenderer == null)
            {
                error = "Owned runtime material instance has no renderer.";
                return false;
            }

            if (materialSlotIndex < 0)
            {
                error = "Owned runtime material instance has a negative material-slot index.";
                return false;
            }

            if (!ShaderParameterController.IsValidFacilitySurfaceMaterial(sourceMaterial, out error))
            {
                return false;
            }

            targetRenderer.GetSharedMaterials(sharedMaterials);
            if (materialSlotIndex >= sharedMaterials.Count)
            {
                error = $"Owned runtime material instance references material slot {materialSlotIndex}, " +
                    $"but renderer '{targetRenderer.name}' has {sharedMaterials.Count} slots.";
                return false;
            }

            if (sharedMaterials[materialSlotIndex] != sourceMaterial)
            {
                error = "Owned runtime material instance renderer slot does not reference its saved source material.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void RestoreAndDestroy()
        {
            if (runtimeMaterial == null)
            {
                return;
            }

            if (targetRenderer != null && materialSlotIndex >= 0)
            {
                targetRenderer.GetSharedMaterials(sharedMaterials);
                if (materialSlotIndex < sharedMaterials.Count
                    && sharedMaterials[materialSlotIndex] == runtimeMaterial)
                {
                    sharedMaterials[materialSlotIndex] = originalMaterial;
                    targetRenderer.SetSharedMaterials(sharedMaterials);
                }
            }

            Material ownedMaterial = runtimeMaterial;
            runtimeMaterial = null;
            originalMaterial = null;

            if (Application.isPlaying)
            {
                Destroy(ownedMaterial);
            }
            else
            {
                DestroyImmediate(ownedMaterial);
            }
        }
    }
}

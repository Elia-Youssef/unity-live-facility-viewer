using System;
using System.Collections.Generic;
using FacilityViewer.Core;
using UnityEngine;

namespace FacilityViewer.World
{
    /// <summary>
    /// Applies the approved FacilitySurface maintenance controls through one reusable property
    /// block. This component never creates or assigns a renderer-specific material instance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShaderParameterController : MonoBehaviour
    {
        private static readonly int MaintenanceTintId =
            Shader.PropertyToID(FacilitySurfaceShader.MaintenanceTint);
        private static readonly int MaintenanceBlendId =
            Shader.PropertyToID(FacilitySurfaceShader.MaintenanceBlend);

        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private int materialSlotIndex;
        [SerializeField] private Material sourceMaterial;
        [SerializeField] private Color maintenanceTint = new(1f, 0.45f, 0f, 1f);
        [SerializeField, Range(0f, 1f)] private float maintenanceBlend = 0.7f;

        private readonly List<Material> sharedMaterials = new();
        private MaterialPropertyBlock propertyBlock;

        public static int MaintenanceTintPropertyId => MaintenanceTintId;
        public static int MaintenanceBlendPropertyId => MaintenanceBlendId;
        public bool IsApplied { get; private set; }

        private void Reset()
        {
            targetRenderer = GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            TryApply(out _);
        }

        private void OnDisable()
        {
            ClearOverride();
        }

        private void OnDestroy()
        {
            ClearOverride();
        }

        /// <summary>
        /// Validates the saved FacilitySurface material before applying this renderer-only
        /// override. Failure leaves the renderer and any assigned materials unchanged.
        /// </summary>
        public bool TryApply(out string error)
        {
            if (!TryValidate(out error))
            {
                return false;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetColor(MaintenanceTintId, maintenanceTint);
            propertyBlock.SetFloat(MaintenanceBlendId, maintenanceBlend);
            targetRenderer.SetPropertyBlock(propertyBlock, materialSlotIndex);
            IsApplied = true;
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Removes this controller's per-renderer overrides. The material slot remains assigned
        /// to its saved shared material throughout the controller lifecycle.
        /// </summary>
        public void ClearOverride()
        {
            if (!IsApplied || targetRenderer == null || materialSlotIndex < 0)
            {
                IsApplied = false;
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            targetRenderer.SetPropertyBlock(propertyBlock, materialSlotIndex);
            IsApplied = false;
        }

        public bool TryValidate(out string error)
        {
            if (targetRenderer == null)
            {
                error = "Shader parameter controller has no renderer.";
                return false;
            }

            if (materialSlotIndex < 0)
            {
                error = "Shader parameter controller has a negative material-slot index.";
                return false;
            }

            if (!IsValidFacilitySurfaceMaterial(sourceMaterial, out error))
            {
                return false;
            }

            targetRenderer.GetSharedMaterials(sharedMaterials);
            if (materialSlotIndex >= sharedMaterials.Count)
            {
                error = $"Shader parameter controller references material slot {materialSlotIndex}, " +
                    $"but renderer '{targetRenderer.name}' has {sharedMaterials.Count} slots.";
                return false;
            }

            if (sharedMaterials[materialSlotIndex] != sourceMaterial)
            {
                error = "Shader parameter controller renderer slot does not reference its saved source material.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        internal static bool IsValidFacilitySurfaceMaterial(Material material, out string error)
        {
            if (material == null)
            {
                error = "Material is not assigned.";
                return false;
            }

            if (material.shader == null
                || !string.Equals(material.shader.name, FacilitySurfaceShader.ShaderName, StringComparison.Ordinal))
            {
                error = $"Material '{material.name}' does not use {FacilitySurfaceShader.ShaderName}.";
                return false;
            }

            for (int index = 0; index < FacilitySurfaceShader.RequiredPropertyNames.Count; index++)
            {
                string propertyName = FacilitySurfaceShader.RequiredPropertyNames[index];
                if (!material.HasProperty(propertyName))
                {
                    error = $"Material '{material.name}' is missing FacilitySurface property '{propertyName}'.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using FacilityViewer.Core;
using UnityEngine;

namespace FacilityViewer.World
{
    [Serializable]
    public sealed class ThemeTargetMaterialSlot
    {
        [SerializeField] private Renderer renderer;
        [SerializeField] private int materialSlotIndex;
        [SerializeField] private string groupId = string.Empty;

        public Renderer Renderer => renderer;
        public int MaterialSlotIndex => materialSlotIndex;
        public string GroupId => groupId;

        internal void Normalize()
        {
            groupId = groupId?.Trim() ?? string.Empty;
        }
    }

    /// <summary>
    /// Scene-local mapping from explicit renderer material slots to material-theme groups.
    /// The target validates every mapping before it changes a renderer, so an invalid
    /// configuration cannot leave a partially applied facility theme behind.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThemeTarget : MonoBehaviour
    {
        [SerializeField] private List<ThemeTargetMaterialSlot> materialSlots = new();

        private readonly List<Renderer> rendererBuffer = new();
        private readonly List<Material> sharedMaterialBuffer = new();
        private readonly List<Material> mappedMaterialBuffer = new();
        private readonly HashSet<RendererSlotKey> mappedSlotKeys = new();

        public IReadOnlyList<ThemeTargetMaterialSlot> MaterialSlots => materialSlots;
        public bool HasValidConfiguration => TryValidate(out _);

        public static event Action<ThemeTarget> Enabled;
        public static event Action<ThemeTarget> Disabled;

        private void OnEnable()
        {
            Enabled?.Invoke(this);
        }

        private void OnDisable()
        {
            Disabled?.Invoke(this);
        }

        private void OnValidate()
        {
            if (materialSlots == null)
            {
                return;
            }

            for (int index = 0; index < materialSlots.Count; index++)
            {
                materialSlots[index]?.Normalize();
            }
        }

        /// <summary>
        /// Validates the serialized renderer-slot contract without changing renderer state.
        /// </summary>
        public bool TryValidate(out string error)
        {
            return TryPrepare(out error);
        }

        /// <summary>
        /// Validates both renderer-slot mappings and every theme material needed by this target
        /// without changing renderer state.
        /// </summary>
        public bool TryValidateTheme(MaterialThemeDefinition theme, out string error)
        {
            if (theme == null)
            {
                error = "Material theme definition is not assigned.";
                return false;
            }

            if (!TryPrepare(out error))
            {
                return false;
            }

            for (int index = 0; index < materialSlots.Count; index++)
            {
                ThemeTargetMaterialSlot mapping = materialSlots[index];
                if (!theme.TryGetMaterial(mapping.GroupId, out _))
                {
                    error = $"Material theme '{theme.name}' has no material for group ID '{mapping.GroupId}'.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Resolves all requested theme materials and validates every target slot before
        /// assigning anything. Successful assignments use only shared renderer materials.
        /// </summary>
        public bool TryApplyTheme(MaterialThemeDefinition theme, out string error)
        {
            if (!TryValidateTheme(theme, out error))
            {
                return false;
            }

            mappedMaterialBuffer.Clear();
            for (int index = 0; index < materialSlots.Count; index++)
            {
                ThemeTargetMaterialSlot mapping = materialSlots[index];
                if (!theme.TryGetMaterial(mapping.GroupId, out Material material))
                {
                    error = $"Material theme '{theme.name}' has no material for group ID '{mapping.GroupId}'.";
                    return false;
                }

                mappedMaterialBuffer.Add(material);
            }

            for (int rendererIndex = 0; rendererIndex < rendererBuffer.Count; rendererIndex++)
            {
                Renderer renderer = rendererBuffer[rendererIndex];
                renderer.GetSharedMaterials(sharedMaterialBuffer);

                for (int mappingIndex = 0; mappingIndex < materialSlots.Count; mappingIndex++)
                {
                    ThemeTargetMaterialSlot mapping = materialSlots[mappingIndex];
                    if (mapping.Renderer == renderer)
                    {
                        sharedMaterialBuffer[mapping.MaterialSlotIndex] = mappedMaterialBuffer[mappingIndex];
                    }
                }

                renderer.SetSharedMaterials(sharedMaterialBuffer);
            }

            error = string.Empty;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLifecycleEvents()
        {
            Enabled = null;
            Disabled = null;
        }

        private bool TryPrepare(out string error)
        {
            if (materialSlots == null || materialSlots.Count == 0)
            {
                error = "Theme target has no renderer material-slot mappings.";
                return false;
            }

            rendererBuffer.Clear();
            mappedSlotKeys.Clear();

            for (int index = 0; index < materialSlots.Count; index++)
            {
                ThemeTargetMaterialSlot mapping = materialSlots[index];
                if (mapping == null)
                {
                    error = $"Theme target has a missing material-slot mapping at index {index}.";
                    return false;
                }

                if (mapping.Renderer == null)
                {
                    error = $"Theme target mapping at index {index} has no renderer.";
                    return false;
                }

                if (mapping.Renderer.gameObject.scene != gameObject.scene)
                {
                    error = $"Theme target mapping at index {index} references renderer " +
                        $"'{mapping.Renderer.name}' from a different scene.";
                    return false;
                }

                if (mapping.MaterialSlotIndex < 0)
                {
                    error = $"Theme target mapping at index {index} has a negative material-slot index.";
                    return false;
                }

                if (!MaterialThemeGroupIds.IsKnown(mapping.GroupId))
                {
                    error = $"Theme target mapping at index {index} uses unknown group ID '{mapping.GroupId}'.";
                    return false;
                }

                RendererSlotKey key = new(mapping.Renderer, mapping.MaterialSlotIndex);
                if (!mappedSlotKeys.Add(key))
                {
                    error = $"Theme target maps renderer '{mapping.Renderer.name}' material slot " +
                        $"{mapping.MaterialSlotIndex} more than once.";
                    return false;
                }

                if (!rendererBuffer.Contains(mapping.Renderer))
                {
                    rendererBuffer.Add(mapping.Renderer);
                }
            }

            for (int rendererIndex = 0; rendererIndex < rendererBuffer.Count; rendererIndex++)
            {
                Renderer renderer = rendererBuffer[rendererIndex];
                renderer.GetSharedMaterials(sharedMaterialBuffer);

                for (int mappingIndex = 0; mappingIndex < materialSlots.Count; mappingIndex++)
                {
                    ThemeTargetMaterialSlot mapping = materialSlots[mappingIndex];
                    if (mapping.Renderer == renderer
                        && mapping.MaterialSlotIndex >= sharedMaterialBuffer.Count)
                    {
                        error = $"Theme target mapping for renderer '{renderer.name}' references material slot " +
                            $"{mapping.MaterialSlotIndex}, but the renderer has {sharedMaterialBuffer.Count} slots.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private readonly struct RendererSlotKey : IEquatable<RendererSlotKey>
        {
            private readonly Renderer renderer;
            private readonly int materialSlotIndex;

            public RendererSlotKey(Renderer renderer, int materialSlotIndex)
            {
                this.renderer = renderer;
                this.materialSlotIndex = materialSlotIndex;
            }

            public bool Equals(RendererSlotKey other)
            {
                return renderer == other.renderer
                    && materialSlotIndex == other.materialSlotIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is RendererSlotKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((renderer != null ? renderer.GetHashCode() : 0) * 397) ^ materialSlotIndex;
                }
            }
        }
    }
}

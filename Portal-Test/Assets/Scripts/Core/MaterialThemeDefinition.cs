using System;
using System.Collections.Generic;
using UnityEngine;

namespace FacilityViewer.Core
{
    public static class MaterialThemeIds
    {
        public const string Standard = "standard";
        public const string Maintenance = "maintenance";
        public const string Emergency = "emergency";

        private static readonly IReadOnlyList<string> KnownIds = Array.AsReadOnly(new[]
        {
            Standard,
            Maintenance,
            Emergency
        });

        public static IReadOnlyList<string> RequiredIds => KnownIds;

        public static bool IsKnown(string themeId)
        {
            for (int index = 0; index < KnownIds.Count; index++)
            {
                if (string.Equals(KnownIds[index], themeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class MaterialThemeGroupIds
    {
        public const string FacilityStructure = "facility-structure";
        public const string FacilityEquipment = "facility-equipment";

        private static readonly IReadOnlyList<string> KnownIds = Array.AsReadOnly(new[]
        {
            FacilityStructure,
            FacilityEquipment
        });

        public static IReadOnlyList<string> RequiredIds => KnownIds;

        public static bool IsKnown(string groupId)
        {
            for (int index = 0; index < KnownIds.Count; index++)
            {
                if (string.Equals(KnownIds[index], groupId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class MaterialThemeGroupMaterial
    {
        [SerializeField] private string groupId = string.Empty;
        [SerializeField] private Material material;

        public string GroupId => groupId;
        public Material Material => material;

        internal void Normalize()
        {
            groupId = groupId?.Trim() ?? string.Empty;
        }
    }

    [CreateAssetMenu(
        fileName = "MaterialThemeDefinition",
        menuName = "Facility Viewer/Material Theme Definition",
        order = 120)]
    public sealed class MaterialThemeDefinition : ScriptableObject
    {
        [SerializeField] private string themeId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private List<MaterialThemeGroupMaterial> groupMaterials = new();

        public string ThemeId => themeId;
        public string DisplayName => displayName;
        public IReadOnlyList<MaterialThemeGroupMaterial> GroupMaterials => groupMaterials;

        public bool TryGetMaterial(string groupId, out Material material)
        {
            material = null;

            if (string.IsNullOrWhiteSpace(groupId) || groupMaterials == null)
            {
                return false;
            }

            for (int index = 0; index < groupMaterials.Count; index++)
            {
                MaterialThemeGroupMaterial entry = groupMaterials[index];
                if (entry != null && string.Equals(entry.GroupId, groupId, StringComparison.Ordinal))
                {
                    material = entry.Material;
                    return material != null;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            themeId = themeId?.Trim() ?? string.Empty;
            displayName = displayName?.Trim() ?? string.Empty;

            if (groupMaterials == null)
            {
                return;
            }

            for (int index = 0; index < groupMaterials.Count; index++)
            {
                groupMaterials[index]?.Normalize();
            }
        }
    }
}

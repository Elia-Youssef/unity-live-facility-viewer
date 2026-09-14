using System;
using System.Collections.Generic;
using UnityEngine;

namespace FacilityViewer.Core
{
    /// <summary>
    /// Validates the data contract consumed by the future material-theme service.
    /// It contains no scene, renderer, or presentation dependencies.
    /// </summary>
    public static class MaterialThemeDefinitionValidator
    {
        public static bool TryValidateCatalog(
            IReadOnlyList<MaterialThemeDefinition> definitions,
            out IReadOnlyList<string> errors)
        {
            List<string> validationErrors = new();
            HashSet<string> definedThemeIds = new(StringComparer.Ordinal);

            if (definitions == null)
            {
                validationErrors.Add("Material theme definition catalog is missing.");
            }
            else
            {
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    MaterialThemeDefinition definition = definitions[definitionIndex];
                    if (definition == null)
                    {
                        validationErrors.Add($"Material theme definition at index {definitionIndex} is missing.");
                        continue;
                    }

                    string themeId = definition.ThemeId;
                    if (string.IsNullOrWhiteSpace(themeId))
                    {
                        validationErrors.Add(
                            $"Material theme definition '{definition.name}' has an empty theme ID.");
                    }
                    else
                    {
                        if (!MaterialThemeIds.IsKnown(themeId))
                        {
                            validationErrors.Add(
                                $"Material theme definition '{definition.name}' uses unknown theme ID '{themeId}'.");
                        }

                        if (!definedThemeIds.Add(themeId))
                        {
                            validationErrors.Add(
                                $"Material theme definition '{themeId}' is duplicated.");
                        }
                    }

                    ValidateDefinition(definition, validationErrors);
                }
            }

            for (int themeIndex = 0; themeIndex < MaterialThemeIds.RequiredIds.Count; themeIndex++)
            {
                string requiredThemeId = MaterialThemeIds.RequiredIds[themeIndex];
                if (!definedThemeIds.Contains(requiredThemeId))
                {
                    validationErrors.Add(
                        $"Missing a material theme definition for theme ID '{requiredThemeId}'.");
                }
            }

            errors = validationErrors;
            return validationErrors.Count == 0;
        }

        public static bool TryValidateDefinition(
            MaterialThemeDefinition definition,
            out IReadOnlyList<string> errors)
        {
            List<string> validationErrors = new();

            if (definition == null)
            {
                validationErrors.Add("Material theme definition is missing.");
            }
            else
            {
                ValidateDefinition(definition, validationErrors);
            }

            errors = validationErrors;
            return validationErrors.Count == 0;
        }

        private static void ValidateDefinition(
            MaterialThemeDefinition definition,
            ICollection<string> validationErrors)
        {
            string definitionLabel = string.IsNullOrWhiteSpace(definition.ThemeId)
                ? definition.name
                : definition.ThemeId;

            if (string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                validationErrors.Add(
                    $"Material theme definition '{definitionLabel}' has an empty display name.");
            }

            IReadOnlyList<MaterialThemeGroupMaterial> groupMaterials = definition.GroupMaterials;
            if (groupMaterials == null)
            {
                validationErrors.Add(
                    $"Material theme definition '{definitionLabel}' has no group mappings.");
                return;
            }

            HashSet<string> mappedGroupIds = new(StringComparer.Ordinal);
            for (int groupIndex = 0; groupIndex < groupMaterials.Count; groupIndex++)
            {
                MaterialThemeGroupMaterial entry = groupMaterials[groupIndex];
                if (entry == null)
                {
                    validationErrors.Add(
                        $"Material theme definition '{definitionLabel}' has a missing group mapping at index {groupIndex}.");
                    continue;
                }

                string groupId = entry.GroupId;
                if (string.IsNullOrWhiteSpace(groupId))
                {
                    validationErrors.Add(
                        $"Material theme definition '{definitionLabel}' has a group mapping with an empty group ID.");
                }
                else
                {
                    if (!MaterialThemeGroupIds.IsKnown(groupId))
                    {
                        validationErrors.Add(
                            $"Material theme definition '{definitionLabel}' uses unknown group ID '{groupId}'.");
                    }

                    if (!mappedGroupIds.Add(groupId))
                    {
                        validationErrors.Add(
                            $"Material theme definition '{definitionLabel}' maps group ID '{groupId}' more than once.");
                    }
                }

                ValidateMaterial(definitionLabel, groupId, entry.Material, validationErrors);
            }

            for (int groupIndex = 0; groupIndex < MaterialThemeGroupIds.RequiredIds.Count; groupIndex++)
            {
                string requiredGroupId = MaterialThemeGroupIds.RequiredIds[groupIndex];
                if (!mappedGroupIds.Contains(requiredGroupId))
                {
                    validationErrors.Add(
                        $"Material theme definition '{definitionLabel}' is missing group ID '{requiredGroupId}'.");
                }
            }
        }

        private static void ValidateMaterial(
            string definitionLabel,
            string groupId,
            Material material,
            ICollection<string> validationErrors)
        {
            string mappingLabel = string.IsNullOrWhiteSpace(groupId) ? "<empty>" : groupId;
            if (material == null)
            {
                validationErrors.Add(
                    $"Material theme definition '{definitionLabel}' has no material for group ID '{mappingLabel}'.");
                return;
            }

            if (material.shader == null)
            {
                validationErrors.Add(
                    $"Material '{material.name}' for group ID '{mappingLabel}' has no shader.");
                return;
            }

            if (!string.Equals(material.shader.name, FacilitySurfaceShader.ShaderName, StringComparison.Ordinal))
            {
                validationErrors.Add(
                    $"Material '{material.name}' for group ID '{mappingLabel}' uses shader " +
                    $"'{material.shader.name}', expected '{FacilitySurfaceShader.ShaderName}'.");
            }

            for (int propertyIndex = 0;
                 propertyIndex < FacilitySurfaceShader.RequiredPropertyNames.Count;
                 propertyIndex++)
            {
                string propertyName = FacilitySurfaceShader.RequiredPropertyNames[propertyIndex];
                if (!material.HasProperty(propertyName))
                {
                    validationErrors.Add(
                        $"Material '{material.name}' for group ID '{mappingLabel}' is missing required " +
                        $"FacilitySurface shader property '{propertyName}'.");
                }
            }
        }
    }
}

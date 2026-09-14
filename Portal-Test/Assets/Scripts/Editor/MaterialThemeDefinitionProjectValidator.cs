using System;
using System.Collections.Generic;
using FacilityViewer.Core;
using UnityEditor;
using UnityEngine;

namespace FacilityViewer.Editor
{
    public static class MaterialThemeDefinitionProjectValidator
    {
        public const string DefinitionFolderPath = "Assets/Data/ThemeDefinitions";

        [MenuItem("Tools/Facility Viewer/Validation/Material Themes")]
        private static void ValidateMaterialThemesFromMenu()
        {
            if (TryValidateProject(out IReadOnlyList<string> errors))
            {
                Debug.Log("Material theme definitions are valid.");
                return;
            }

            for (int index = 0; index < errors.Count; index++)
            {
                Debug.LogError(errors[index]);
            }

            throw new InvalidOperationException("Material theme definition validation failed.");
        }

        public static bool TryValidateProject(out IReadOnlyList<string> errors)
        {
            string[] assetGuids = AssetDatabase.FindAssets(
                "t:MaterialThemeDefinition",
                new[] { DefinitionFolderPath });
            List<MaterialThemeDefinition> definitions = new(assetGuids.Length);

            for (int index = 0; index < assetGuids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[index]);
                definitions.Add(AssetDatabase.LoadAssetAtPath<MaterialThemeDefinition>(assetPath));
            }

            return MaterialThemeDefinitionValidator.TryValidateCatalog(definitions, out errors);
        }
    }
}
